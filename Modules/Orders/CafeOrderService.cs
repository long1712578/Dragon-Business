using Dragon.Business.Data;
using Dragon.Business.Modules.Payments;
using Microsoft.EntityFrameworkCore;

namespace Dragon.Business.Modules.Orders;

/// <summary>
/// Enterprise Café Order Service — 100% Native AOT compatible via raw SQL.
/// Manages the full order lifecycle: create → prepare → ready → checkout (Payment integration).
/// </summary>
public class CafeOrderService
{
    private readonly AppDbContext _db;
    private readonly PaymentService _paymentService;
    private readonly ILogger<CafeOrderService> _logger;

    public CafeOrderService(AppDbContext db, PaymentService paymentService, ILogger<CafeOrderService> logger)
    {
        _db = db;
        _paymentService = paymentService;
        _logger = logger;
    }

    // ─── PRODUCT CATALOG ────────────────────────────────────────────

    public async Task<List<CafeProductRow>> GetProductsAsync(string? category = null)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = category == null
            ? "SELECT Id, Name, Category, Price, IsAvailable, ImageUrl FROM CafeProducts ORDER BY Category, Name"
            : "SELECT Id, Name, Category, Price, IsAvailable, ImageUrl FROM CafeProducts WHERE Category = @cat ORDER BY Name";

        if (category != null)
        {
            var p = cmd.CreateParameter(); p.ParameterName = "@cat"; p.Value = category; cmd.Parameters.Add(p);
        }

        var list = new List<CafeProductRow>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new CafeProductRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetInt32(4) == 1,
                reader.IsDBNull(5) ? null : reader.GetString(5)
            ));
        }
        return list;
    }

    public async Task<int> CreateProductAsync(CreateProductRequest req)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO CafeProducts (Name, Category, Price, IsAvailable, ImageUrl, CreatedAt) 
                            VALUES (@name, @cat, @price, 1, @img, @now);
                            SELECT last_insert_rowid();";
        
        var pName = cmd.CreateParameter(); pName.ParameterName = "@name"; pName.Value = req.Name; cmd.Parameters.Add(pName);
        var pCat  = cmd.CreateParameter(); pCat.ParameterName  = "@cat";  pCat.Value  = req.Category; cmd.Parameters.Add(pCat);
        var pPrice= cmd.CreateParameter(); pPrice.ParameterName= "@price";pPrice.Value= req.Price; cmd.Parameters.Add(pPrice);
        var pImg  = cmd.CreateParameter(); pImg.ParameterName  = "@img";  pImg.Value  = (object?)req.ImageUrl ?? DBNull.Value; cmd.Parameters.Add(pImg);
        var pNow  = cmd.CreateParameter(); pNow.ParameterName  = "@now";  pNow.Value  = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"); cmd.Parameters.Add(pNow);
        
        var id = await cmd.ExecuteScalarAsync();
        _logger.LogInformation("Created product {Name} at {Price}", req.Name, req.Price);
        return Convert.ToInt32(id);
    }

    public async Task<bool> UpdateProductAvailabilityAsync(int productId, bool isAvailable)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE CafeProducts SET IsAvailable = @av WHERE Id = @id";
        var pAv = cmd.CreateParameter(); pAv.ParameterName = "@av"; pAv.Value = isAvailable ? 1 : 0; cmd.Parameters.Add(pAv);
        var pId = cmd.CreateParameter(); pId.ParameterName = "@id"; pId.Value = productId; cmd.Parameters.Add(pId);
        var affected = await cmd.ExecuteNonQueryAsync();
        return affected > 0;
    }

    public async Task<bool> DeleteProductAsync(int productId)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM CafeProducts WHERE Id = @id";
        var pId = cmd.CreateParameter(); pId.ParameterName = "@id"; pId.Value = productId; cmd.Parameters.Add(pId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // ─── ORDER MANAGEMENT ───────────────────────────────────────────

    public async Task<CafeOrderDetailRow?> GetOrderByIdAsync(int orderId)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id, TableNumber, CustomerName, Note, TotalAmount, Status, StaffId, PaymentOrderId, CreatedAt, CompletedAt 
                            FROM CafeOrders WHERE Id = @id";
        var pId = cmd.CreateParameter(); pId.ParameterName = "@id"; pId.Value = orderId; cmd.Parameters.Add(pId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var order = new CafeOrderDetailRow(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetDecimal(4),
            reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            new List<CafeOrderItemRow>()
        );
        reader.Close();

        // Load items
        using var cmdItems = conn.CreateCommand();
        cmdItems.CommandText = "SELECT Id, ProductId, ProductName, UnitPrice, Quantity, CustomNote FROM CafeOrderItems WHERE CafeOrderId = @id";
        var pId2 = cmdItems.CreateParameter(); pId2.ParameterName = "@id"; pId2.Value = orderId; cmdItems.Parameters.Add(pId2);
        using var rItems = await cmdItems.ExecuteReaderAsync();
        while (await rItems.ReadAsync())
        {
            order.Items.Add(new CafeOrderItemRow(
                rItems.GetInt32(0),
                rItems.GetInt32(1),
                rItems.GetString(2),
                rItems.GetDecimal(3),
                rItems.GetInt32(4),
                rItems.IsDBNull(5) ? null : rItems.GetString(5)
            ));
        }
        return order;
    }

    public async Task<List<CafeOrderSummaryRow>> GetOrdersAsync(CafeOrderStatus? status = null, int limit = 50)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        var whereClause = status.HasValue ? "WHERE Status = @status" : "";
        cmd.CommandText = $@"SELECT Id, TableNumber, CustomerName, TotalAmount, Status, StaffId, PaymentOrderId, CreatedAt 
                             FROM CafeOrders {whereClause} ORDER BY CreatedAt DESC LIMIT @limit";

        if (status.HasValue)
        {
            var pSt = cmd.CreateParameter(); pSt.ParameterName = "@status"; pSt.Value = (int)status.Value; cmd.Parameters.Add(pSt);
        }
        var pLim = cmd.CreateParameter(); pLim.ParameterName = "@limit"; pLim.Value = limit; cmd.Parameters.Add(pLim);

        var list = new List<CafeOrderSummaryRow>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new CafeOrderSummaryRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7)
            ));
        }
        return list;
    }

    public async Task<int> CreateOrderAsync(CreateCafeOrderRequest req)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        // Validate + price lookup
        decimal total = 0;
        var itemsWithPrice = new List<(int ProductId, string Name, decimal Price, int Qty, string? Note)>();
        foreach (var item in req.Items)
        {
            using var cmdProd = conn.CreateCommand();
            cmdProd.CommandText = "SELECT Name, Price, IsAvailable FROM CafeProducts WHERE Id = @id";
            var pPId = cmdProd.CreateParameter(); pPId.ParameterName = "@id"; pPId.Value = item.ProductId; cmdProd.Parameters.Add(pPId);
            using var rProd = await cmdProd.ExecuteReaderAsync();
            if (!await rProd.ReadAsync()) throw new KeyNotFoundException($"Product {item.ProductId} not found");
            if (rProd.GetInt32(2) == 0) throw new InvalidOperationException($"Product '{rProd.GetString(0)}' is not available");
            var price = rProd.GetDecimal(1);
            itemsWithPrice.Add((item.ProductId, rProd.GetString(0), price, item.Quantity, item.CustomNote));
            total += price * item.Quantity;
        }

        // Insert Order
        using var cmdOrder = conn.CreateCommand();
        cmdOrder.CommandText = @"INSERT INTO CafeOrders (TableNumber, CustomerName, Note, TotalAmount, Status, StaffId, CreatedAt) 
                                 VALUES (@table, @name, @note, @total, 0, @staff, @now);
                                 SELECT last_insert_rowid();";
        var pT    = cmdOrder.CreateParameter(); pT.ParameterName = "@table"; pT.Value = req.TableNumber; cmdOrder.Parameters.Add(pT);
        var pCN   = cmdOrder.CreateParameter(); pCN.ParameterName = "@name";  pCN.Value = (object?)req.CustomerName ?? DBNull.Value; cmdOrder.Parameters.Add(pCN);
        var pNote = cmdOrder.CreateParameter(); pNote.ParameterName = "@note";  pNote.Value = (object?)req.Note ?? DBNull.Value; cmdOrder.Parameters.Add(pNote);
        var pTot  = cmdOrder.CreateParameter(); pTot.ParameterName = "@total"; pTot.Value = total; cmdOrder.Parameters.Add(pTot);
        var pSt   = cmdOrder.CreateParameter(); pSt.ParameterName  = "@staff"; pSt.Value = (object?)req.StaffId ?? DBNull.Value; cmdOrder.Parameters.Add(pSt);
        var pNow  = cmdOrder.CreateParameter(); pNow.ParameterName = "@now";   pNow.Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"); cmdOrder.Parameters.Add(pNow);

        var newId = Convert.ToInt32(await cmdOrder.ExecuteScalarAsync());

        // Insert Items
        foreach (var item in itemsWithPrice)
        {
            using var cmdItem = conn.CreateCommand();
            cmdItem.CommandText = @"INSERT INTO CafeOrderItems (CafeOrderId, ProductId, ProductName, UnitPrice, Quantity, CustomNote) 
                                    VALUES (@oid, @pid, @pname, @price, @qty, @cnote)";
            var p1 = cmdItem.CreateParameter(); p1.ParameterName = "@oid";   p1.Value = newId; cmdItem.Parameters.Add(p1);
            var p2 = cmdItem.CreateParameter(); p2.ParameterName = "@pid";   p2.Value = item.ProductId; cmdItem.Parameters.Add(p2);
            var p3 = cmdItem.CreateParameter(); p3.ParameterName = "@pname"; p3.Value = item.Name; cmdItem.Parameters.Add(p3);
            var p4 = cmdItem.CreateParameter(); p4.ParameterName = "@price"; p4.Value = item.Price; cmdItem.Parameters.Add(p4);
            var p5 = cmdItem.CreateParameter(); p5.ParameterName = "@qty";   p5.Value = item.Qty; cmdItem.Parameters.Add(p5);
            var p6 = cmdItem.CreateParameter(); p6.ParameterName = "@cnote"; p6.Value = (object?)item.Note ?? DBNull.Value; cmdItem.Parameters.Add(p6);
            await cmdItem.ExecuteNonQueryAsync();
        }

        _logger.LogInformation("Created CafeOrder #{Id} for table {Table}, total {Total}", newId, req.TableNumber, total);
        return newId;
    }

    public async Task<bool> UpdateStatusAsync(int orderId, CafeOrderStatus newStatus)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        if (newStatus == CafeOrderStatus.Completed)
        {
            cmd.CommandText = "UPDATE CafeOrders SET Status = @status, CompletedAt = @now WHERE Id = @id";
            var pNow = cmd.CreateParameter(); pNow.ParameterName = "@now"; pNow.Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"); cmd.Parameters.Add(pNow);
        }
        else
        {
            cmd.CommandText = "UPDATE CafeOrders SET Status = @status WHERE Id = @id";
        }
        var pSt = cmd.CreateParameter(); pSt.ParameterName = "@status"; pSt.Value = (int)newStatus; cmd.Parameters.Add(pSt);
        var pId = cmd.CreateParameter(); pId.ParameterName = "@id"; pId.Value = orderId; cmd.Parameters.Add(pId);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task CompleteOrderByPaymentIdAsync(string paymentOrderId)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE CafeOrders SET Status = @st, CompletedAt = @now WHERE PaymentOrderId = @pid AND Status != @st";
        var pSt = cmd.CreateParameter(); pSt.ParameterName = "@st"; pSt.Value = (int)CafeOrderStatus.Completed; cmd.Parameters.Add(pSt);
        var pNow = cmd.CreateParameter(); pNow.ParameterName = "@now"; pNow.Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"); cmd.Parameters.Add(pNow);
        var pPid = cmd.CreateParameter(); pPid.ParameterName = "@pid"; pPid.Value = paymentOrderId; cmd.Parameters.Add(pPid);
        
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Checkout: tạo Payment QR cho một CafeOrder đang ở trạng thái Ready.
    /// Liên kết CafeOrder.PaymentOrderId với Payment.OrderId sau khi thành công.
    /// </summary>
    public async Task<CheckoutResult> CheckoutOrderAsync(int orderId, string providerName, string? staffId)
    {
        var order = await GetOrderByIdAsync(orderId) ?? throw new KeyNotFoundException($"Order #{orderId} not found");
        
        if (order.Status == (int)CafeOrderStatus.Cancelled)
            throw new InvalidOperationException("Cannot checkout a cancelled order");
        if (order.Status == (int)CafeOrderStatus.Completed)
            throw new InvalidOperationException("Order is already completed");

        var description = $"Thanh toan Order #{orderId} - Ban {order.TableNumber} - {string.Join(", ", order.Items.Select(i => $"{i.ProductName} x{i.Quantity}"))}";
        
        var payment = await _paymentService.CreatePaymentRequestAsync(
            order.TotalAmount, 
            description, 
            staffId ?? order.StaffId, 
            providerName
        );

        // Link Payment → CafeOrder
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE CafeOrders SET PaymentOrderId = @pid WHERE Id = @id";
        var pPid = cmd.CreateParameter(); pPid.ParameterName = "@pid"; pPid.Value = payment.OrderId; cmd.Parameters.Add(pPid);
        var pId  = cmd.CreateParameter(); pId.ParameterName  = "@id";  pId.Value  = orderId; cmd.Parameters.Add(pId);
        await cmd.ExecuteNonQueryAsync();

        _logger.LogInformation("Checkout Order #{OrderId} → Payment {PaymentId} via {Provider}", orderId, payment.OrderId, providerName);
        return new CheckoutResult(orderId, payment.OrderId, payment.PaymentUrl, providerName, order.TotalAmount);
    }

    public async Task<bool> DeleteOrderAsync(int orderId)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        using var cmdItems = conn.CreateCommand();
        cmdItems.CommandText = "DELETE FROM CafeOrderItems WHERE CafeOrderId = @id";
        var pId1 = cmdItems.CreateParameter(); pId1.ParameterName = "@id"; pId1.Value = orderId; cmdItems.Parameters.Add(pId1);
        await cmdItems.ExecuteNonQueryAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM CafeOrders WHERE Id = @id";
        var pId = cmd.CreateParameter(); pId.ParameterName = "@id"; pId.Value = orderId; cmd.Parameters.Add(pId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}

// ─── Request / Response DTOs ────────────────────────────────────
public record CreateCafeOrderRequest(
    string TableNumber,
    string? CustomerName,
    string? Note,
    string? StaffId,
    List<CafeOrderItemRequest> Items
);

public record CafeOrderItemRequest(int ProductId, int Quantity, string? CustomNote);

public record CreateProductRequest(string Name, string Category, decimal Price, string? ImageUrl);

public record CheckoutRequest(string Provider, string? StaffId);

public record CheckoutResult(int CafeOrderId, string PaymentOrderId, string? PaymentUrl, string Provider, decimal Amount);

public record CafeProductRow(int Id, string Name, string Category, decimal Price, bool IsAvailable, string? ImageUrl);

public record CafeOrderSummaryRow(
    int Id,
    string TableNumber,
    string? CustomerName,
    decimal TotalAmount,
    int Status,
    string? StaffId,
    string? PaymentOrderId,
    string CreatedAt
);

public record CafeOrderDetailRow(
    int Id,
    string TableNumber,
    string? CustomerName,
    string? Note,
    decimal TotalAmount,
    int Status,
    string? StaffId,
    string? PaymentOrderId,
    string CreatedAt,
    string? CompletedAt,
    List<CafeOrderItemRow> Items
);

public record CafeOrderItemRow(int Id, int ProductId, string ProductName, decimal UnitPrice, int Quantity, string? CustomNote);
