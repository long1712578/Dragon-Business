using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
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


// 4. OpenAPI & Scalar
builder.Services.AddOpenApi();

// 5. Dependency Injection
builder.Services.AddScoped<IPaymentProvider, ZaloPayAdapter>();
builder.Services.AddScoped<PaymentService>();

var app = builder.Build();

// Migrate Database
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
var payments = app.MapGroup("/api/payments");

payments.MapPost("/create", async (decimal amount, string desc, string? staffId, PaymentService paymentService) => {
    var result = await paymentService.CreatePaymentRequestAsync(amount, desc, staffId);
    return Results.Ok(result);
});

payments.MapPost("/webhook/zalopay", async (HttpRequest request, PaymentService paymentService) => {
    // Logic xử lý webhook ZaloPay
    return Results.Ok(new { return_code = 1, return_message = "success" });
});

// Module: Staff
var staff = app.MapGroup("/api/staff");
staff.MapGet("/", async (AppDbContext db) => await db.StaffMembers.ToListAsync());

app.Run();

// -------------------------------------------------------------------------
// Boilerplate cho Native AOT & EF Core
// -------------------------------------------------------------------------

[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(DateTime))]
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
internal partial class AppJsonSerializerContext : JsonSerializerContext { }

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
}

public record PaymentRequestResponse(string OrderId, string PaymentUrl, string Provider);

