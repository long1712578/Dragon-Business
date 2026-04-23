using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using RedisFlow.Extensions;
using Dragon.Business.Data;
using Dragon.Business.Modules.Payments;
using Dragon.Business.Modules.Staff;
using Microsoft.AspNetCore.Http.Json;
using Serilog;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Security.Cryptography;
using System.Text;

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
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=Data/dragon.db"));

// 4. RedisFlow
builder.Services.AddRedisFlow(redisFlow =>
{
    redisFlow.WithRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
    redisFlow.UseJsonSerialization();
    
    redisFlow.AddProducer("payments", producer => {
        producer.WithMaxLength(1000).WithApproximateTrimming(true);
    });
});

// 5. OpenAPI & Scalar
builder.Services.AddOpenApi();

// 6. Authentication & Authorization (SSO)
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
    options.AddPolicy("ManagerOnly", policy => policy.RequireRole("admin", "Manager"));
    options.AddPolicy("StaffOnly", policy => policy.RequireRole("admin", "Manager", "staff", "Staff"));
});

// 7. Enterprise Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>()
    .AddRedis(builder.Configuration["Redis"] ?? "localhost:6379", name: "redis");

// 8. Dependency Injection
builder.Services.AddScoped<IPaymentProvider, ZaloPayAdapter>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<StaffService>();

var app = builder.Build();

// 9. Global Error Handling
app.Use(async (context, next) => {
    try { await next(); }
    catch (Exception ex) {
        Log.Error(ex, "Unhandled exception occurred");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { Message = "An internal server error occurred.", Error = ex.Message });
    }
});

app.UseAuthentication();
app.UseAuthorization();

// Serve Dashboard
app.UseDefaultFiles();
app.UseStaticFiles();

// Mapping Health Checks
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
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
    return await db.Payments.OrderByDescending(p => p.CreatedAt).Take(50).ToListAsync();
}).RequireAuthorization("StaffOnly");

payments.MapPost("/create", async (PaymentCreateRequest req, PaymentService paymentService) => {
    var result = await paymentService.CreatePaymentRequestAsync(req.Amount, req.Desc, req.StaffId);
    return Results.Ok(result);
}).RequireAuthorization("StaffOnly");

payments.MapGet("/{orderId}", async (string orderId, AppDbContext db) => {
    var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId);
    return payment != null ? Results.Ok(payment) : Results.NotFound(new { Message = $"Payment {orderId} not found" });
}).RequireAuthorization("StaffOnly");

payments.MapPost("/webhook/zalopay", async (HttpContext context, WebhookRequest req, PaymentService paymentService) => {
    var signature = context.Request.Headers["x-zalopay-signature"].ToString();
    var success = await paymentService.ProcessWebhookAsync("ZaloPay", req.JsonContent, signature, req.OrderId);
    return success ? Results.Ok(new { return_code = 1, return_message = "success" }) : Results.BadRequest();
}).AllowAnonymous();

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

// Module: Dev Helper
    
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
