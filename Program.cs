using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;
using RedisFlow.Extensions;
using Dragon.Business.Data;
using Dragon.Business.Data.CompiledModels;
using Dragon.Business.Modules.Payments;

var builder = WebApplication.CreateSlimBuilder(args);

// 1. Cấu hình JSON tối ưu cho AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// 2. Database (SQLite) — UseModel() để tương thích Native AOT
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=Data/dragon_business.db")
           .UseModel(AppDbContextModel.Instance));

// 3. RedisFlow
builder.Services.AddRedisFlow(redisFlow =>
{
    redisFlow.WithRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
    redisFlow.UseJsonSerialization();
    
    redisFlow.AddProducer("payments", producer => {
        producer.WithMaxLength(1000).WithApproximateTrimming(true);
    });
});


// 4. OpenAPI & Scalar (tương thích CreateSlimBuilder)
builder.Services.AddOpenApi();

// 5. Dependency Injection
builder.Services.AddScoped<IPaymentProvider, ZaloPayAdapter>();
builder.Services.AddScoped<PaymentService>();

var app = builder.Build();

// Migrate Database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
#pragma warning disable IL3050 // EnsureCreated: known EF Core AOT limitation, safe for SQLite schema creation
    db.Database.EnsureCreated();
#pragma warning restore IL3050
    
    if (!db.StaffMembers.Any())
    {
        db.StaffMembers.Add(new StaffMember { Name = "Long Pham", Role = "TechLead" });
        db.StaffMembers.Add(new StaffMember { Name = "Dragon Employee", Role = "Barista" });
        db.SaveChanges();
    }
}


if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Dragon Business API")
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });

    // Helper: tự tính mac để test webhook không cần tính tay
    app.MapPost("/api/dev/webhook/sign", (WebhookSignRequest req, IConfiguration config) =>
    {
        var key2 = config["ZaloPay:Key2"] ?? "Iyz2LcUDt69876zY8v6968h76z6895pzed";
        using var hmac = new HMACSHA256(System.Text.Encoding.UTF8.GetBytes(key2));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(req.Data));
        var mac = BitConverter.ToString(hash).Replace("-", "").ToLower();
        return Results.Ok(new { data = req.Data, mac });
    }).WithTags("Dev").WithSummary("[DEV ONLY] Tính mac cho webhook test");
}

// Minimal APIs
app.MapGet("/", () => new AppInfoResponse("Dragon Business API is running!", "1.0.0-AOT"));

string? TryResolveOrderIdFromWebhook(string jsonContent)
{
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(jsonContent);
        var root = doc.RootElement;

        if (root.TryGetProperty("orderId", out var orderIdProp))
        {
            return orderIdProp.GetString();
        }

        if (root.TryGetProperty("app_trans_id", out var appTransIdProp))
        {
            var appTransId = appTransIdProp.GetString();
            if (!string.IsNullOrWhiteSpace(appTransId))
            {
                var idx = appTransId.IndexOf('_');
                return idx >= 0 && idx < appTransId.Length - 1
                    ? appTransId[(idx + 1)..]
                    : appTransId;
            }
        }
    }
    catch
    {
        return null;
    }

    return null;
}

// Module: Payments
var payments = app.MapGroup("/api/payments").WithTags("Payments");

payments.MapPost("/create", async (CreatePaymentRequest request, PaymentService paymentService) => {
    if (request.Amount <= 0)
    {
        return Results.BadRequest(new ErrorResponse("Amount must be greater than 0"));
    }

    if (string.IsNullOrWhiteSpace(request.Desc))
    {
        return Results.BadRequest(new ErrorResponse("Description is required"));
    }

    var result = await paymentService.CreatePaymentRequestAsync(request.Amount, request.Desc, request.StaffId);
    return Results.Ok(result);
})
.WithName("CreatePayment")
.WithSummary("Create new payment request")
.Produces<PaymentRequestResponse>(StatusCodes.Status200OK)
.Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

payments.MapGet("/", async (AppDbContext db) =>
        await db.Payments.OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync())
    .WithName("GetRecentPayments")
    .WithSummary("Get recent payments")
    .Produces<List<Payment>>(StatusCodes.Status200OK);

payments.MapGet("/{orderId}", async (string orderId, AppDbContext db) =>
    {
        var payment = await db.Payments.FirstOrDefaultAsync(x => x.OrderId == orderId);
        return payment is null
            ? Results.NotFound(new ErrorResponse($"Payment {orderId} not found"))
            : Results.Ok(payment);
    })
    .WithName("GetPaymentByOrderId")
    .WithSummary("Get payment by order id")
    .Produces<Payment>(StatusCodes.Status200OK)
    .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

payments.MapPost("/webhook/zalopay", async (
    ZaloWebhookRequest request,
    PaymentService paymentService) =>
{
    // ZaloPay: body = { "data": "<json string>", "mac": "<hmac-sha256>" }
    if (string.IsNullOrWhiteSpace(request.Data))
        return Results.BadRequest(new ErrorResponse("data field is required"));

    if (string.IsNullOrWhiteSpace(request.Mac))
        return Results.BadRequest(new ErrorResponse("mac field is required"));

    var orderId = TryResolveOrderIdFromWebhook(request.Data);
    if (string.IsNullOrWhiteSpace(orderId))
        return Results.BadRequest(new ErrorResponse("Cannot resolve orderId from webhook data"));

    var ok = await paymentService.ProcessWebhookAsync("ZaloPay", request.Data, request.Mac, orderId);
    return ok
        ? Results.Ok(new WebhookAck(1, "success"))
        : Results.BadRequest(new WebhookAck(-1, "invalid signature or payload"));
})
.WithName("ZaloPayWebhook")
.WithSummary("Receive payment status callback from ZaloPay")
.Produces<WebhookAck>(StatusCodes.Status200OK)
.Produces<WebhookAck>(StatusCodes.Status400BadRequest)
.Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

// Module: Staff
var staff = app.MapGroup("/api/staff").WithTags("Staff");
staff.MapGet("/", async (AppDbContext db) => await db.StaffMembers.ToListAsync());

app.Run();

// -------------------------------------------------------------------------
// Boilerplate cho Native AOT & EF Core
// -------------------------------------------------------------------------

[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(AppInfoResponse))]
[JsonSerializable(typeof(StaffMember))]
[JsonSerializable(typeof(StaffMember[]))]
[JsonSerializable(typeof(List<StaffMember>))]
[JsonSerializable(typeof(Payment))]
[JsonSerializable(typeof(Payment[]))]
[JsonSerializable(typeof(List<Payment>))]
[JsonSerializable(typeof(PaymentStatus))]
[JsonSerializable(typeof(Transaction))]
[JsonSerializable(typeof(List<Transaction>))]
[JsonSerializable(typeof(PaymentRequestResponse))]
[JsonSerializable(typeof(PaymentSuccessEvent))]
[JsonSerializable(typeof(CreatePaymentRequest))]
[JsonSerializable(typeof(WebhookAck))]
[JsonSerializable(typeof(ZaloWebhookRequest))]
[JsonSerializable(typeof(WebhookSignRequest))]
[JsonSerializable(typeof(ErrorResponse))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }

#pragma warning disable IL2026, IL3050 // EF Core: safe with compiled model (UseModel)
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
}
#pragma warning restore IL2026, IL3050

public record PaymentRequestResponse(string OrderId, string PaymentUrl, string Provider);
public record AppInfoResponse(string Message, string Version);
public record CreatePaymentRequest(decimal Amount, string Desc, string? StaffId);
// ZaloPay gửi: { "data": "<json string>", "mac": "<hmac>" }
// "data" là chuỗi JSON đã encode (không phải object), "mac" là chữ ký HMAC-SHA256
public record ZaloWebhookRequest(string Data, string Mac);
public record WebhookSignRequest(string Data); // [DEV] input: data string, output: mac
public record WebhookAck(int ReturnCode, string ReturnMessage);
public record ErrorResponse(string Message);

