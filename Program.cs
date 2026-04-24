using System.Text.Json.Serialization;
using Dragon.Business;
using Dragon.Business.Data;
using Dragon.Business.Modules.Payments;
using Dragon.Business.Modules.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Json;
using Scalar.AspNetCore;
using Serilog;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using RedisFlow.Extensions;

var builder = WebApplication.CreateSlimBuilder(args);

// 1. Serilog Enterprise Logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

// 2. AOT JSON Context Configuration
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

// 3. Infrastructure & DB
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=/app/Data/dragon.db")
           .UseModel(Dragon.Business.Data.CompiledModels.AppDbContextModel.Instance));

// 4. OpenAPI & Scalar
builder.Services.AddOpenApi();

// 5. Authentication & Authorization (SSO)
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = builder.Configuration["SSO:Authority"] ?? "https://sso.longdev.store";
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateAudience = false
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Roles khớp với SSO seed: 'admin' và 'employee'
    options.AddPolicy("ManagerOnly", policy => policy.RequireRole("admin"));
    options.AddPolicy("StaffOnly",   policy => policy.RequireRole("admin", "employee"));
});

// 6. Enterprise Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>()
    .AddRedis(builder.Configuration["Redis"] ?? "localhost:6379", name: "redis");

// 7. Dependency Injection
builder.Services.AddScoped<IPaymentProvider, ZaloPayAdapter>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<StaffService>();

// 8. RedisFlow Event Streaming Configuration (Native AOT Compatible)
builder.Services.AddRedisFlow(flow =>
{
    flow.WithRedis(builder.Configuration["Redis"] ?? "localhost:6379")
        .AddProducer("payments");
    
    // Ép RedisFlow dùng AppJsonContext để không bị crash AOT
    flow.UseSerializer<AotRedisSerializer>();
});

builder.Services.AddSignalR().AddJsonProtocol(options => {
    options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, Dragon.Business.Hubs.HubJsonContext.Default);
    options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(1, Dragon.Business.AppJsonContext.Default);
});

var app = builder.Build();

// 8. Global Error Handling
app.Use(async (context, next) => {
    try { await next(); }
    catch (Exception ex) {
        Log.Error(ex, "Unhandled exception occurred");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new ErrorResponse("An internal server error occurred.", ex.Message), AppJsonContext.Default.ErrorResponse);
    }
});

app.UseAuthentication();
app.UseAuthorization();

// Serve Dashboard với Cache-Control để tự động update code mới (Cache Busting)
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Với index.html và các file .js, .css -> Luôn yêu cầu trình duyệt check bản mới (revalidate)
        if (ctx.File.Name.EndsWith(".html") || ctx.File.Name.EndsWith(".js") || ctx.File.Name.EndsWith(".css"))
        {
            ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            ctx.Context.Response.Headers.Append("Pragma", "no-cache");
            ctx.Context.Response.Headers.Append("Expires", "0");
        }
        else
        {
            // Các file static khác (ảnh, font) có thể cache lâu hơn (7 ngày)
            ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=604800");
        }
    }
});

// Mapping Health Checks
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHub<Dragon.Business.Hubs.NotificationHub>("/hub/notifications");
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Dragon Business API")
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

// Module: Payments
var payments = app.MapGroup("/api/payments").WithTags("Payments");

payments.MapGet("/", async (AppDbContext db) => {
    // Native AOT: Bỏ qua hoàn toàn EF Query Compiler để tránh lỗi 'Query wasn't precompiled'
    var result = new List<Payment>();
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT OrderId, Amount, Description, Status, Provider, StaffId, CreatedAt, PaymentUrl FROM Payments ORDER BY CreatedAt DESC LIMIT 50";
    using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        result.Add(new Payment {
            OrderId = reader.GetString(0),
            Amount = reader.GetDecimal(1),
            Description = reader.GetString(2),
            Status = (PaymentStatus)reader.GetInt32(3),
            Provider = reader.GetString(4),
            StaffId = reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedAt = DateTime.Parse(reader.GetString(6)),
            PaymentUrl = reader.IsDBNull(7) ? null : reader.GetString(7)
        });
    }
    return Results.Ok(result);
}).RequireAuthorization("StaffOnly");

payments.MapPost("/create", async (PaymentCreateRequest req, PaymentService paymentService) => {
    var result = await paymentService.CreatePaymentRequestAsync(req.Amount, req.Desc, req.StaffId);
    return Results.Ok(result);
}).RequireAuthorization("StaffOnly");

payments.MapGet("/{orderId}", async (string orderId, AppDbContext db) => {
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT OrderId, Amount, Description, Status, Provider, StaffId, CreatedAt FROM Payments WHERE OrderId = @id";
    var pId = cmd.CreateParameter();
    pId.ParameterName = "@id";
    pId.Value = orderId;
    cmd.Parameters.Add(pId);
    
    using var reader = await cmd.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        return Results.Ok(new Payment {
            OrderId = reader.GetString(0),
            Amount = reader.GetDecimal(1),
            Description = reader.GetString(2),
            Status = (PaymentStatus)reader.GetInt32(3),
            Provider = reader.GetString(4),
            StaffId = reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedAt = DateTime.Parse(reader.GetString(6))
        });
    }
    return Results.NotFound(new ErrorResponse("Payment not found", orderId));
}).RequireAuthorization("StaffOnly");

payments.MapDelete("/{orderId}", async (string orderId, AppDbContext db) => {
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
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

payments.MapPost("/webhook/zalopay", async (HttpContext context, WebhookRequest req, PaymentService paymentService) => {
    var signature = context.Request.Headers["x-zalopay-signature"].ToString();
    var success = await paymentService.ProcessWebhookAsync("ZaloPay", req.JsonContent, signature, req.OrderId);
    return success ? Results.Ok(new { return_code = 1, return_message = "success" }) : Results.BadRequest();
}).AllowAnonymous();

// Admin: cập nhật trạng thái payment thủ công
payments.MapPut("/{orderId}/status", async (string orderId, StatusUpdateRequest req, AppDbContext db) => {
    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

    // Check exists first
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

    return Results.Ok(new { OrderId = orderId, Status = req.Status });
}).RequireAuthorization("ManagerOnly");

// Module: Staff
var staff = app.MapGroup("/api/staff").WithTags("Staff");

staff.MapGet("/", async (StaffService staffService) => {
    var result = await staffService.GetAllStaffWithStatsAsync();
    return Results.Ok(result);
}).RequireAuthorization("StaffOnly");

staff.MapPost("/", async (StaffCreateRequest req, StaffService staffService) => {
    var result = await staffService.CreateStaffAsync(req.Name, req.Role);
    return Results.Ok(result);
}).RequireAuthorization("ManagerOnly");

// Admin: xóa staff
staff.MapDelete("/{id:int}", async (int id, AppDbContext db) => {
    var member = await db.StaffMembers.FindAsync(id);
    if (member == null) return Results.NotFound(new ErrorResponse("Staff not found", $"ID {id}"));
    db.StaffMembers.Remove(member);
    await db.SaveChangesAsync();
    return Results.Ok(new DeleteResponse("Staff deleted", id.ToString()));
}).RequireAuthorization("ManagerOnly");

// Module: Dev Helper
app.MapPost("/api/dev/webhook/sign", (SignRequest req, IConfiguration config) => {
    var key2 = config["ZaloPay:Key2"] ?? "Iyz2LcUDt69876zY8v6968h76z6895pzed";
    using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(key2));
    var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(req.Data));
    var mac = Convert.ToHexString(hash).ToLower();
    return Results.Ok(new SignResponse(req.Data, mac));
}).WithTags("Dev Helper");

// Seed Database — 100% raw SQL, không dùng LINQ (không tương thích Native AOT)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var conn = db.Database.GetDbConnection();
    conn.Open();

    using var cmd = conn.CreateCommand();

    // 1. Tạo bảng (idempotent)
    cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS "StaffMembers" (
            "Id"        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            "Name"      TEXT    NOT NULL DEFAULT '',
            "Role"      TEXT    NOT NULL DEFAULT '',
            "CreatedAt" TEXT    NOT NULL DEFAULT (datetime('now'))
        );
        CREATE TABLE IF NOT EXISTS "Payments" (
            "OrderId"     TEXT    NOT NULL PRIMARY KEY,
            "TransId"     TEXT    NULL,
            "Amount"      DECIMAL NOT NULL DEFAULT 0,
            "Description" TEXT    NOT NULL DEFAULT '',
            "Status"      INTEGER NOT NULL DEFAULT 0,
            "Provider"    TEXT    NOT NULL DEFAULT 'ZaloPay',
            "StaffId"     TEXT    NULL,
            "CreatedAt"   TEXT    NOT NULL DEFAULT (datetime('now')),
            "PaidAt"      TEXT    NULL,
            "PaymentUrl"  TEXT    NULL
        );
        CREATE INDEX IF NOT EXISTS "idx_payments_staff" ON "Payments" ("StaffId");
        CREATE INDEX IF NOT EXISTS "idx_payments_status" ON "Payments" ("Status");
    ";
    await db.Database.ExecuteSqlRawAsync(seedSql);

    // HACK: Tự động thêm cột PaymentUrl nếu chưa có (Dành cho các DB cũ)
    try {
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Payments ADD COLUMN PaymentUrl TEXT NULL;");
    } catch { /* Bỏ qua nếu cột đã tồn tại */ }
    
    var seedTransactionsSql = @"
        CREATE TABLE IF NOT EXISTS ""Transactions"" (
            "Id"        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            "OrderId"   TEXT    NOT NULL DEFAULT '',
            "Content"   TEXT    NOT NULL DEFAULT '',
            "CreatedAt" TEXT    NOT NULL DEFAULT ''
        );
        """;
    cmd.ExecuteNonQuery();

    // 2. Seed staff nếu chưa có (raw SQL, không dùng LINQ)
    cmd.CommandText = "SELECT COUNT(*) FROM StaffMembers";
    var count = (long)(cmd.ExecuteScalar() ?? 0L);
    if (count == 0)
    {
        cmd.CommandText = """
            INSERT INTO StaffMembers (Name, Role, CreatedAt) VALUES
                ('Long Pham',        'TechLead', datetime('now')),
                ('Dragon Employee',  'Barista',  datetime('now'));
            """;
        cmd.ExecuteNonQuery();
    }

    conn.Close();
}

app.Run();

namespace Dragon.Business
{
    // AOT-Friendly JSON Source Generation
    [JsonSerializable(typeof(Dragon.Business.Modules.Payments.PaymentSuccessEvent))]
    [JsonSerializable(typeof(ErrorResponse))]
    [JsonSerializable(typeof(Payment))]
    [JsonSerializable(typeof(List<Payment>))]
    [JsonSerializable(typeof(StaffMember))]
    [JsonSerializable(typeof(List<StaffMember>))]
    [JsonSerializable(typeof(StaffMemberWithStats))]
    [JsonSerializable(typeof(List<StaffMemberWithStats>))]
    [JsonSerializable(typeof(PaymentCreateRequest))]
    [JsonSerializable(typeof(PaymentRequestResponse))]
    [JsonSerializable(typeof(StatusUpdateRequest))]
    [JsonSerializable(typeof(StaffCreateRequest))]
    [JsonSerializable(typeof(WebhookRequest))]
    [JsonSerializable(typeof(SignRequest))]
    [JsonSerializable(typeof(SignResponse))]
    [JsonSerializable(typeof(DeleteResponse))]
    [JsonSerializable(typeof(PaymentStatus))]
    [JsonSerializable(typeof(object))]
    internal partial class AppJsonContext : JsonSerializerContext { }

    // Serializer tùy chỉnh cho RedisFlow để hỗ trợ Native AOT (100% không dùng reflection)
    public class AotRedisSerializer : RedisFlow.Abstractions.IMessageSerializer
    {
        public AotRedisSerializer() { } // Cần constructor public cho DI

        public byte[] Serialize<T>(T obj)
        {
            var typeInfo = AppJsonContext.Default.GetTypeInfo(typeof(T)) ?? throw new NotSupportedException($"Type {typeof(T)} not in AOT Context");
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(obj, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)typeInfo);
        }

        public T Deserialize<T>(byte[] data)
        {
            var typeInfo = AppJsonContext.Default.GetTypeInfo(typeof(T)) ?? throw new NotSupportedException($"Type {typeof(T)} not in AOT Context");
            return (T)System.Text.Json.JsonSerializer.Deserialize(data, typeInfo)!;
        }

        public object Deserialize(byte[] data, Type type)
        {
            var typeInfo = AppJsonContext.Default.GetTypeInfo(type) ?? throw new NotSupportedException($"Type {type} not in AOT Context");
            return System.Text.Json.JsonSerializer.Deserialize(data, typeInfo)!;
        }
    }

    public record ErrorResponse(string Message, string Error);
    public record DeleteResponse(string Message, string Id);
    public record PaymentCreateRequest(decimal Amount, string Desc, string StaffId);
    public record StatusUpdateRequest(int Status);
    public record StaffCreateRequest(string Name, string Role);
    public record WebhookRequest(string JsonContent, string OrderId);
    public record SignRequest(string Data);
    public record SignResponse(string Data, string Mac);
    public record PaymentRequestResponse(string OrderId, string PaymentUrl, string Provider);
}
