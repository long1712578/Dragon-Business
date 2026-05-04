using Dragon.Business.Data;
using Microsoft.EntityFrameworkCore;
using RedisFlow.Abstractions;
using Serilog;

namespace Dragon.Business.Modules.Payments;

public static class PaymentEndpoints
{
    public static RouteGroupBuilder MapPaymentEndpoints(this RouteGroupBuilder group)
    {
        var payments = group.MapGroup("/v1/payments").WithTags("Payments v1");

        payments.MapGet("/", async (AppDbContext db) =>
        {
            var result = new List<Payment>();
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT OrderId, Amount, Description, Status, Provider, StaffId, CreatedAt, PaymentUrl FROM Payments ORDER BY CreatedAt DESC LIMIT 50";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new Payment
                {
                    OrderId = reader.GetString(0),
                    Amount = reader.GetDecimal(1),
                    Description = reader.GetString(2),
                    Status = (PaymentStatus)reader.GetInt32(3),
                    Provider = reader.GetString(4),
                    StaffId = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CreatedAt = DateTime.SpecifyKind(DateTime.Parse(reader.GetString(6)), DateTimeKind.Utc),
                    PaymentUrl = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }
            return Results.Ok(result);
        }).RequireAuthorization("StaffOnly");

        payments.MapPost("/create", async (PaymentCreateRequest req, PaymentService paymentService) =>
        {
            var result = await paymentService.CreatePaymentRequestAsync(req.Amount, req.Desc, req.StaffId);
            return Results.Ok(result);
        }).RequireAuthorization("StaffOnly");

        payments.MapGet("/{orderId}", async (string orderId, AppDbContext db) =>
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT OrderId, Amount, Description, Status, Provider, StaffId, CreatedAt FROM Payments WHERE OrderId = @id";
            var pId = cmd.CreateParameter();
            pId.ParameterName = "@id";
            pId.Value = orderId;
            cmd.Parameters.Add(pId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return Results.Ok(new Payment
                {
                    OrderId = reader.GetString(0),
                    Amount = reader.GetDecimal(1),
                    Description = reader.GetString(2),
                    Status = (PaymentStatus)reader.GetInt32(3),
                    Provider = reader.GetString(4),
                    StaffId = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CreatedAt = DateTime.SpecifyKind(DateTime.Parse(reader.GetString(6)), DateTimeKind.Utc)
                });
            }
            return Results.NotFound(new ErrorResponse("Payment not found", orderId));
        }).RequireAuthorization("StaffOnly");

        payments.MapDelete("/{orderId}", async (string orderId, AppDbContext db) =>
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Payments WHERE OrderId = @id";
            var pId = cmd.CreateParameter();
            pId.ParameterName = "@id";
            pId.Value = orderId;
            cmd.Parameters.Add(pId);

            var affected = await cmd.ExecuteNonQueryAsync();
            return affected > 0
                ? Results.Ok(new DeleteResponse("Payment deleted", orderId))
                : Results.NotFound(new ErrorResponse("Payment not found", orderId));
        }).RequireAuthorization("ManagerOnly");

        payments.MapPost("/webhook/zalopay", async (HttpContext context, WebhookRequest req, PaymentService paymentService) =>
        {
            var signature = context.Request.Headers["x-zalopay-signature"].ToString();
            var success = await paymentService.ProcessWebhookAsync("ZaloPay", req.JsonContent, signature, req.OrderId);
            return success ? Results.Ok(new { return_code = 1, return_message = "success" }) : Results.BadRequest();
        }).AllowAnonymous();

        payments.MapPost("/webhook/momo", async (HttpContext context, WebhookRequest req, PaymentService paymentService) =>
        {
            var signature = context.Request.Headers["x-momo-signature"].ToString();
            var success = await paymentService.ProcessWebhookAsync("MoMo", req.JsonContent, signature, req.OrderId);
            return success ? Results.Ok(new { return_code = 1, return_message = "success" }) : Results.BadRequest();
        }).AllowAnonymous();

        payments.MapGet("/{orderId}/audit-logs", async (string orderId, AppDbContext db) =>
        {
            var list = new List<TransactionResponse>();
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, OrderId, Content, CreatedAt FROM Transactions WHERE OrderId = @id ORDER BY Id DESC";
            var pId = cmd.CreateParameter();
            pId.ParameterName = "@id";
            pId.Value = orderId;
            cmd.Parameters.Add(pId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new TransactionResponse(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3)
                ));
            }
            return Results.Ok(list);
        }).RequireAuthorization("ManagerOnly");

        // Mock Simulate
        payments.MapPost("/mock/{orderId}/simulate-paid", async (
            string orderId,
            AppDbContext db,
            IStreamProducer producer,
            IWebHostEnvironment env,
            IConfiguration config) =>
        {
            var allowMock = config.GetValue<bool>("AllowMockPayment");

            if (!env.IsDevelopment() && !env.IsEnvironment("Local") && !allowMock)
            {
                Log.Warning("[MOCK] Blocked simulate-paid request on Production for Order {OrderId}", orderId);
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            using var cmdCheck = conn.CreateCommand();
            cmdCheck.CommandText = "SELECT Status, Amount, StaffId, Provider FROM Payments WHERE OrderId = @id";
            var pCheck = cmdCheck.CreateParameter(); pCheck.ParameterName = "@id"; pCheck.Value = orderId; cmdCheck.Parameters.Add(pCheck);
            
            using var reader = await cmdCheck.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return Results.NotFound(new ErrorResponse("Payment not found", orderId));
            
            var currentStatus = reader.GetInt32(0);
            var amount = reader.GetDecimal(1);
            var staffId = reader.IsDBNull(2) ? null : reader.GetString(2);
            var provider = reader.GetString(3);
            
            if (currentStatus == (int)PaymentStatus.Paid)
                return Results.BadRequest(new ErrorResponse("Already paid", orderId));

            await reader.CloseAsync();

            using var cmdUpdate = conn.CreateCommand();
            cmdUpdate.CommandText = "UPDATE Payments SET Status = @status, PaidAt = @paidAt WHERE OrderId = @id";
            var pStat = cmdUpdate.CreateParameter(); pStat.ParameterName = "@status"; pStat.Value = (int)PaymentStatus.Paid; cmdUpdate.Parameters.Add(pStat);
            var pPaid = cmdUpdate.CreateParameter(); pPaid.ParameterName = "@paidAt"; pPaid.Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"); cmdUpdate.Parameters.Add(pPaid);
            var pIdUpdate = cmdUpdate.CreateParameter(); pIdUpdate.ParameterName = "@id"; pIdUpdate.Value = orderId; cmdUpdate.Parameters.Add(pIdUpdate);
            await cmdUpdate.ExecuteNonQueryAsync();

            using var cmdLog = conn.CreateCommand();
            cmdLog.CommandText = "INSERT INTO Transactions (OrderId, Content, CreatedAt) VALUES (@id, @content, @now)";
            var pIdL = cmdLog.CreateParameter(); pIdL.ParameterName = "@id"; pIdL.Value = orderId; cmdLog.Parameters.Add(pIdL);
            var pCnt = cmdLog.CreateParameter(); pCnt.ParameterName = "@content"; pCnt.Value = "{\"message\":\"Mock payment successful via simulate-paid API\"}"; cmdLog.Parameters.Add(pCnt);
            var pNow = cmdLog.CreateParameter(); pNow.ParameterName = "@now"; pNow.Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"); cmdLog.Parameters.Add(pNow);
            await cmdLog.ExecuteNonQueryAsync();

            using var cmdOrder = conn.CreateCommand();
            cmdOrder.CommandText = "UPDATE CafeOrders SET Status = 3, CompletedAt = @now2 WHERE PaymentOrderId = @pid AND Status != 3";
            var pNow2 = cmdOrder.CreateParameter(); pNow2.ParameterName = "@now2"; pNow2.Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"); cmdOrder.Parameters.Add(pNow2);
            var pPid = cmdOrder.CreateParameter(); pPid.ParameterName = "@pid"; pPid.Value = orderId; cmdOrder.Parameters.Add(pPid);
            await cmdOrder.ExecuteNonQueryAsync();

            await producer.ProduceAsync(new PaymentSuccessEvent(
                orderId,
                amount,
                staffId,
                DateTime.UtcNow,
                provider
            ));

            Log.Information("[MOCK] Payment {OrderId} marked as Paid via simulate-paid and event produced", orderId);
            return Results.Ok(new MockSimulateResponse(true, orderId, "Paid"));
        }).AllowAnonymous();

        payments.MapPut("/{orderId}/status", async (string orderId, StatusUpdateRequest req, AppDbContext db) =>
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            using var cmdCheck = conn.CreateCommand();
            cmdCheck.CommandText = "SELECT COUNT(1) FROM Payments WHERE OrderId = @id";
            var pCheckId = cmdCheck.CreateParameter(); pCheckId.ParameterName = "@id"; pCheckId.Value = orderId; cmdCheck.Parameters.Add(pCheckId);
            var count = Convert.ToInt64(await cmdCheck.ExecuteScalarAsync());
            if (count == 0) return Results.NotFound(new ErrorResponse("Payment not found", $"ID {orderId}"));

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Payments SET Status = @status WHERE OrderId = @id";
            var pStatus = cmd.CreateParameter(); pStatus.ParameterName = "@status"; pStatus.Value = req.Status; cmd.Parameters.Add(pStatus);
            var pId = cmd.CreateParameter(); pId.ParameterName = "@id"; pId.Value = orderId; cmd.Parameters.Add(pId);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok(new StatusUpdateResponse(orderId, req.Status));
        }).RequireAuthorization("ManagerOnly");

        return group;
    }
}
