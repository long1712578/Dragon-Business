using System.Text.Json.Serialization;
using Dragon.Business;
using Dragon.Business.Data;
using Dragon.Business.Infrastructure.Data;
using Dragon.Business.Modules.Orders;
using Dragon.Business.Modules.Payments;
using Dragon.Business.Modules.Staff;
using Dragon.Business.Modules.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Json;
using Scalar.AspNetCore;
using Serilog;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using RedisFlow.Extensions;
using RedisFlow.Abstractions;
using Dragon.Business.Hubs;
using FluentValidation;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        // 1. Serilog Enterprise Logging
        Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
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

                var metadataAddress = builder.Configuration["SSO:MetadataAddress"];
                if (!string.IsNullOrEmpty(metadataAddress))
                {
                    options.MetadataAddress = metadataAddress;
                    options.RequireHttpsMetadata = metadataAddress.StartsWith("https://");

                    if (Uri.TryCreate(options.Authority, UriKind.Absolute, out var publicUri) &&
                        Uri.TryCreate(metadataAddress, UriKind.Absolute, out var internalUri))
                    {
                        options.BackchannelHttpHandler = new InternalOidcRoutingHandler(publicUri.Host, internalUri.Host, internalUri.Port);
                    }
                }

                options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateAudience = false,
                    ValidIssuers = new[] { options.Authority, options.Authority.TrimEnd('/') + "/" }
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("ManagerOnly", policy => policy.RequireRole("admin"));
            options.AddPolicy("StaffOnly", policy => policy.RequireRole("admin", "employee"));
        });

        // 6. Enterprise Health Checks
        builder.Services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>()
            .AddRedis(builder.Configuration["Redis"] ?? "localhost:6379", name: "redis");

        // 7. Dependency Injection
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IPaymentProvider, ZaloPayAdapter>();
        builder.Services.AddScoped<IPaymentProvider, MockPaymentProvider>();
        builder.Services.AddScoped<PaymentService>();
        builder.Services.AddScoped<StaffService>();
        builder.Services.AddScoped<CafeOrderService>();
        
        // FluentValidation DI
        builder.Services.AddValidatorsFromAssemblyContaining<Program>();

        // 8. RedisFlow Event Streaming Configuration (Native AOT Compatible)
        builder.Services.AddRedisFlow(flow =>
        {
            flow.WithRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379")
                .AddProducer("payments")
                .AddConsumer("payments", "business-group", consumer =>
                {
                    consumer.AddHandler<PaymentSuccessEvent, PaymentNotificationHandler>();
                    consumer.AddHandler<PaymentCreatedEvent, PaymentNotificationHandler>();
                });

            flow.UseSerializer<AotRedisSerializer>();
        });

        builder.Services.AddTransient<PaymentNotificationHandler>();

        builder.Services.AddSignalR().AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, HubJsonContext.Default);
            options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(1, AppJsonContext.Default);
        });

        var app = builder.Build();

        // 8. Global Error Handling
        app.Use(async (context, next) =>
        {
            try { await next(); }
            catch (Exception ex)
            {
                Log.Error(ex, "Unhandled exception occurred");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new ErrorResponse("An internal server error occurred.", ex.Message), AppJsonContext.Default.ErrorResponse);
            }
        });

        app.UseAuthentication();
        app.UseAuthorization();

        // Serve Dashboard với Cache-Control
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                if (ctx.File.Name.EndsWith(".html") || ctx.File.Name.EndsWith(".js") || ctx.File.Name.EndsWith(".css"))
                {
                    ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
                    ctx.Context.Response.Headers.Append("Pragma", "no-cache");
                    ctx.Context.Response.Headers.Append("Expires", "0");
                }
                else
                {
                    ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=604800");
                }
            }
        });

        // 9. API Routing
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });
        app.MapHub<NotificationHub>("/hub/notifications");
        
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.WithTitle("Dragon Business API")
                   .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
        });

        // ═══════════════════════════════════════════════
        // MAP FEATURES / MODULES
        // ═══════════════════════════════════════════════
        var api = app.MapGroup("/api");
        api.MapPaymentEndpoints();
        api.MapCafeOrderEndpoints();
        api.MapStaffEndpoints();

        // Dev Helper Endpoint
        app.MapPost("/api/dev/webhook/sign", (SignRequest req, IConfiguration config) =>
        {
            var key2 = config["ZaloPay:Key2"] ?? "Iyz2LcUDt69876zY8v6968h76z6895pzed";
            using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(key2));
            var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(req.Data));
            var mac = Convert.ToHexString(hash).ToLower();
            return Results.Ok(new SignResponse(req.Data, mac));
        }).WithTags("Dev Helper");

        // 10. Database Initialization
        await DatabaseInitializer.InitializeAsync(app.Services);

        app.Run();
    }
}
