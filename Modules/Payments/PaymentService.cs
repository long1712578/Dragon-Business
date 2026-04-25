using Dragon.Business.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using RedisFlow.Abstractions;

namespace Dragon.Business.Modules.Payments;

public class PaymentService
{
    private readonly AppDbContext _db;
    private readonly IEnumerable<IPaymentProvider> _providers;
    private readonly ILogger<PaymentService> _logger;
    private readonly IStreamProducer _producer;

    public PaymentService(
        AppDbContext db, 
        IEnumerable<IPaymentProvider> providers, 
        ILogger<PaymentService> logger,
        IStreamProducer producer)
    {
        _db = db;
        _providers = providers;
        _logger = logger;
        _producer = producer;
    }

    public async Task<PaymentRequestResponse> CreatePaymentRequestAsync(decimal amount, string description, string? staffId = null, string providerName = "ZaloPay")
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderName == providerName) 
            ?? throw new ArgumentException($"Provider {providerName} not found");

        var orderId = Guid.NewGuid().ToString("N").Substring(0, 10);
        
        var payment = new Payment
        {
            OrderId = orderId,
            Amount = amount,
            Description = description,
            StaffId = staffId,
            Provider = providerName,
            Status = PaymentStatus.Created
        };

        var url = await provider.CreatePaymentUrlAsync(payment);
        payment.PaymentUrl = url;

        // Native AOT: Tránh Model Building crash bằng cách dùng Raw SQL INSERT
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO Payments (OrderId, Amount, Description, Status, Provider, StaffId, CreatedAt, PaymentUrl) 
                            VALUES (@id, @amt, @desc, @status, @prov, @staff, @now, @url)";
        
        var pId = cmd.CreateParameter(); pId.ParameterName = "@id"; pId.Value = payment.OrderId; cmd.Parameters.Add(pId);
        var pAmt = cmd.CreateParameter(); pAmt.ParameterName = "@amt"; pAmt.Value = payment.Amount; cmd.Parameters.Add(pAmt);
        var pDesc = cmd.CreateParameter(); pDesc.ParameterName = "@desc"; pDesc.Value = payment.Description; cmd.Parameters.Add(pDesc);
        var pStat = cmd.CreateParameter(); pStat.ParameterName = "@status"; pStat.Value = (int)payment.Status; cmd.Parameters.Add(pStat);
        var pProv = cmd.CreateParameter(); pProv.ParameterName = "@prov"; pProv.Value = payment.Provider; cmd.Parameters.Add(pProv);
        var pStaff = cmd.CreateParameter(); pStaff.ParameterName = "@staff"; pStaff.Value = (object?)payment.StaffId ?? DBNull.Value; cmd.Parameters.Add(pStaff);
        var pNow = cmd.CreateParameter(); pNow.ParameterName = "@now"; pNow.Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"); cmd.Parameters.Add(pNow);
        var pUrl = cmd.CreateParameter(); pUrl.ParameterName = "@url"; pUrl.Value = (object?)payment.PaymentUrl ?? DBNull.Value; cmd.Parameters.Add(pUrl);

        await cmd.ExecuteNonQueryAsync();
        
        _logger.LogInformation("Created payment request {OrderId} via {Provider}", orderId, providerName);

        return new PaymentRequestResponse(orderId, url, providerName);
    }

    public async Task<bool> ProcessWebhookAsync(string providerName, string jsonContent, string signature, string orderId)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderName == providerName);
        if (provider == null || !await provider.VerifyWebhookAsync(jsonContent, signature))
        {
            _logger.LogWarning("Invalid webhook signature from {Provider}", providerName);
            return false;
        }

        // Native AOT: FindAsync an toàn hơn dynamic LINQ
        var payment = await _db.Payments.FindAsync(orderId);
        if (payment != null && payment.Status != PaymentStatus.Paid)
        {
            // Native AOT: Dùng SQL UPDATE thô để tránh Model Building crash
            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Payments SET Status = @status, PaidAt = @paidAt WHERE OrderId = @id";
            
            var pStat = cmd.CreateParameter(); pStat.ParameterName = "@status"; pStat.Value = (int)PaymentStatus.Paid; cmd.Parameters.Add(pStat);
            var pPaid = cmd.CreateParameter(); pPaid.ParameterName = "@paidAt"; pPaid.Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"); cmd.Parameters.Add(pPaid);
            var pId = cmd.CreateParameter(); pId.ParameterName = "@id"; pId.Value = orderId; cmd.Parameters.Add(pId);

            await cmd.ExecuteNonQueryAsync();

            // INSERT transaction log
            using var cmdLog = conn.CreateCommand();
            cmdLog.CommandText = "INSERT INTO Transactions (OrderId, Content, CreatedAt) VALUES (@id, @content, @now)";
            var pIdL = cmdLog.CreateParameter(); pIdL.ParameterName = "@id"; pIdL.Value = orderId; cmdLog.Parameters.Add(pIdL);
            var pCnt = cmdLog.CreateParameter(); pCnt.ParameterName = "@content"; pCnt.Value = jsonContent; cmdLog.Parameters.Add(pCnt);
            var pNow = cmdLog.CreateParameter(); pNow.ParameterName = "@now"; pNow.Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"); cmdLog.Parameters.Add(pNow);
            await cmdLog.ExecuteNonQueryAsync();
            
            // Cập nhật local object để dùng cho notification bên dưới
            payment.Status = PaymentStatus.Paid;
            payment.PaidAt = DateTime.UtcNow;
            
            // Bắn event vào RedisFlow
            await _producer.ProduceAsync(new PaymentSuccessEvent(
                payment.OrderId,
                payment.Amount,
                payment.StaffId,
                payment.PaidAt.Value,
                payment.Provider
            ));
            
            _logger.LogInformation("Payment {OrderId} marked as PAID and event produced to Redis", orderId);
        }

        return true;
    }
}

