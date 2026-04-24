using System.Text.Json.Serialization;
using Dragon.Business.Data;
using Dragon.Business.Modules.Payments;
using Dragon.Business.Modules.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Json;
using Scalar.AspNetCore;
using Serilog;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

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

var app = builder.Build();

// 8. Global Error Handling
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

// Admin: xóa payment
payments.MapDelete("/{orderId}", async (string orderId, AppDbContext db) => {
    var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId);
    if (payment == null) return Results.NotFound(new { Message = $"Payment {orderId} not found" });
    db.Payments.Remove(payment);
    await db.SaveChangesAsync();
    return Results.Ok(new { Message = $"Payment {orderId} deleted" });
}).RequireAuthorization("ManagerOnly");

// Admin: cập nhật trạng thái payment thủ công
payments.MapPut("/{orderId}/status", async (string orderId, StatusUpdateRequest req, AppDbContext db) => {
    var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId);
    if (payment == null) return Results.NotFound(new { Message = $"Payment {orderId} not found" });
    payment.Status = (PaymentStatus)req.Status;
    await db.SaveChangesAsync();
    return Results.Ok(payment);
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
    if (member == null) return Results.NotFound(new { Message = $"Staff {id} not found" });
    db.StaffMembers.Remove(member);
    await db.SaveChangesAsync();
    return Results.Ok(new { Message = $"Staff {id} deleted" });
}).RequireAuthorization("ManagerOnly");

// Module: Dev Helper
app.MapPost("/api/dev/webhook/sign", (SignRequest req, IConfiguration config) => {
    var key2 = config["ZaloPay:Key2"] ?? "Iyz2LcUDt69876zY8v6968h76z6895pzed";
    using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(key2));
    var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(req.Data));
    var mac = BitConverter.ToString(hash).Replace("-", "").ToLower();
    return Results.Ok(new { data = req.Data, mac });
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
            "Amount"      TEXT    NOT NULL DEFAULT '0',
            "Description" TEXT    NOT NULL DEFAULT '',
            "Status"      INTEGER NOT NULL DEFAULT 0,
            "Provider"    TEXT    NOT NULL DEFAULT 'ZaloPay',
            "StaffId"     TEXT    NULL,
            "CreatedAt"   TEXT    NOT NULL DEFAULT (datetime('now')),
            "PaidAt"      TEXT    NULL
        );
        CREATE TABLE IF NOT EXISTS "Transactions" (
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

// AOT-Friendly JSON Source Generation
[JsonSerializable(typeof(Payment))]
[JsonSerializable(typeof(List<Payment>))]
[JsonSerializable(typeof(StaffMember))]
[JsonSerializable(typeof(List<StaffMember>))]
[JsonSerializable(typeof(StaffMemberWithStats))]
[JsonSerializable(typeof(List<StaffMemberWithStats>))]
[JsonSerializable(typeof(PaymentCreateRequest))]
[JsonSerializable(typeof(StatusUpdateRequest))]
[JsonSerializable(typeof(StaffCreateRequest))]
[JsonSerializable(typeof(WebhookRequest))]
[JsonSerializable(typeof(SignRequest))]
[JsonSerializable(typeof(PaymentStatus))]
[JsonSerializable(typeof(object))]
internal partial class AppJsonContext : JsonSerializerContext { }

public record PaymentCreateRequest(decimal Amount, string Desc, string StaffId);
public record StatusUpdateRequest(int Status);
public record StaffCreateRequest(string Name, string Role);
public record WebhookRequest(string JsonContent, string OrderId);
public record SignRequest(string Data);
public record PaymentRequestResponse(string OrderId, string PaymentUrl, string Provider);

