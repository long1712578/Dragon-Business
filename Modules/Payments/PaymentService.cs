using Dragon.Business.Data;
using Microsoft.EntityFrameworkCore;

namespace Dragon.Business.Modules.Payments;

public class PaymentService
{
    private readonly AppDbContext _db;
    private readonly IEnumerable<IPaymentProvider> _providers;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(AppDbContext db, IEnumerable<IPaymentProvider> providers, ILogger<PaymentService> logger)
    {
        _db = db;
        _providers = providers;
        _logger = logger;
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

    public async Task<bool> ProcessWebhookAsync(string providerName, string jsonContent, string signature)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderName == providerName);
        if (provider == null || !await provider.VerifyWebhookAsync(jsonContent, signature))
        {
            _logger.LogWarning("Invalid webhook signature from {Provider}", providerName);
            return false;
        }

        // Logic parse jsonContent tùy theo provider để lấy app_trans_id/order_id
        // Giả lập lấy được orderId
        var orderId = "extracted_from_json"; 
        
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
            
            // TODO: Bắn event vào RedisFlow ở đây
            _logger.LogInformation("Payment {OrderId} marked as PAID", orderId);
        }

        return true;
    }
}
