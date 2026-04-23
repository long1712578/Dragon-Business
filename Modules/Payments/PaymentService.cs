using Dragon.Business.Data;
using Microsoft.EntityFrameworkCore;
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

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        var url = await provider.CreatePaymentUrlAsync(payment);
        
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

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId);
        if (payment != null && payment.Status != PaymentStatus.Paid)
        {
            payment.Status = PaymentStatus.Paid;
            payment.PaidAt = DateTime.UtcNow;
            
            _db.Transactions.Add(new Transaction { 
                OrderId = orderId, 
                Content = jsonContent 
            });
            
            await _db.SaveChangesAsync();
            
            // Bắn event vào RedisFlow
            await _producer.ProduceAsync(new PaymentSuccessEvent(
                payment.OrderId,
                payment.Amount,
                payment.StaffId,
                payment.PaidAt.Value,
                payment.Provider
            ));
            
            _logger.LogInformation("Payment {OrderId} marked as PAID and event produced", orderId);
        }

        return true;
    }
}

