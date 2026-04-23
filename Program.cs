using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using RedisFlow.Extensions;
using Dragon.Business.Data;
using Dragon.Business.Modules.Payments;
using Dragon.Business.Modules.Staff;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateSlimBuilder(args);

// 1. Cấu hình JSON tối ưu cho AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// 2. Database (SQLite)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=Data/dragon_business.db"));

// 3. RedisFlow
builder.Services.AddRedisFlow(redisFlow =>
{
    redisFlow.WithRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
    redisFlow.UseJsonSerialization();
    
    redisFlow.AddProducer("payments", producer => {
        producer.WithMaxLength(1000).WithApproximateTrimming(true);
    });
});

// 4. OpenAPI & Scalar
builder.Services.AddOpenApi();

// 5. Dependency Injection
builder.Services.AddScoped<IPaymentProvider, ZaloPayAdapter>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<StaffService>();

var app = builder.Build();

// Seed & Migrate Database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    
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
    app.MapScalarApiReference();
}

// Minimal APIs
app.MapGet("/", () => new { Message = "Dragon Business API is running!", Version = "1.0.0-AOT" });

// Module: Payments
var payments = app.MapGroup("/api/payments").WithTags("Payments");

payments.MapPost("/create", async (PaymentCreateRequest req, PaymentService paymentService) => {
    var result = await paymentService.CreatePaymentRequestAsync(req.Amount, req.Desc, req.StaffId);
    return Results.Ok(result);
});

payments.MapGet("/{orderId}", async (string orderId, AppDbContext db) => {
    var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId);
    return payment != null ? Results.Ok(payment) : Results.NotFound(new { Message = $"Payment {orderId} not found" });
});

payments.MapPost("/webhook/zalopay", async (HttpContext context, WebhookRequest req, PaymentService paymentService) => {
    var signature = context.Request.Headers["x-zalopay-signature"].ToString();
    var success = await paymentService.ProcessWebhookAsync("ZaloPay", req.JsonContent, signature, req.OrderId);
    
    return success 
        ? Results.Ok(new { return_code = 1, return_message = "success" })
        : Results.BadRequest(new { return_code = -1, return_message = "invalid signature or payload" });
});

// Module: Staff
var staff = app.MapGroup("/api/staff").WithTags("Staff");

staff.MapGet("/", async (StaffService staffService) => {
    var result = await staffService.GetAllStaffWithStatsAsync();
    return Results.Ok(result);
});

staff.MapPost("/", async (StaffCreateRequest req, StaffService staffService) => {
    var result = await staffService.CreateStaffAsync(req.Name, req.Role);
    return Results.Ok(result);
});

// Module: Dev Helper
var dev = app.MapGroup("/api/dev").WithTags("Dev Helper");

dev.MapPost("/webhook/sign", (WebhookSignRequest req, IConfiguration config) => {
    var key2 = config["ZaloPay:Key2"] ?? "Iyz2LcUDt69876zY8v6968h76z6895pzed";
    // ZaloPay Webhook data format: {"orderId":"xxx","result":"paid"}
    var jsonContent = $"{{\"orderId\":\"{req.OrderId}\",\"result\":\"{req.Result}\"}}";
    
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key2));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(jsonContent));
    var mac = BitConverter.ToString(hash).Replace("-", "").ToLower();
    
    return Results.Ok(new { data = jsonContent, mac = mac });
});

app.Run();

// -------------------------------------------------------------------------
// Boilerplate cho Native AOT & DTOs
// -------------------------------------------------------------------------

[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(StaffMember))]
[JsonSerializable(typeof(StaffMember[]))]
[JsonSerializable(typeof(StaffMemberDto))]
[JsonSerializable(typeof(List<StaffMemberDto>))]
[JsonSerializable(typeof(Payment))]
[JsonSerializable(typeof(PaymentStatus))]
[JsonSerializable(typeof(Transaction))]
[JsonSerializable(typeof(PaymentRequestResponse))]
[JsonSerializable(typeof(PaymentCreateRequest))]
[JsonSerializable(typeof(StaffCreateRequest))]
[JsonSerializable(typeof(WebhookRequest))]
[JsonSerializable(typeof(WebhookSignRequest))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
}

public record PaymentRequestResponse(string OrderId, string PaymentUrl, string Provider);
public record PaymentCreateRequest(decimal Amount, string Desc, string? StaffId);
public record StaffCreateRequest(string Name, string Role);
public record WebhookRequest(string JsonContent, string OrderId);
public record WebhookSignRequest(string OrderId, string Result);
