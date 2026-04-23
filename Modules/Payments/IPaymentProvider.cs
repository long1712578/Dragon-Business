using Dragon.Business.Data;

namespace Dragon.Business.Modules.Payments;

public interface IPaymentProvider
{
    string ProviderName { get; }
    Task<string> CreatePaymentUrlAsync(Payment payment);
    Task<bool> VerifyWebhookAsync(string jsonContent, string signature);
}
