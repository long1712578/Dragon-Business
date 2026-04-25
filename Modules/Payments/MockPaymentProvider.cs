using Dragon.Business.Data;

namespace Dragon.Business.Modules.Payments;

/// <summary>
/// Mock Payment Provider — dùng cho dev/staging, không cần Sandbox key thật.
/// QR code thật sự được tạo ra (qua api.qrserver.com miễn phí),
/// khi scan → mở trang mobile đẹp → user tap "Thanh toán" → simulate-paid API.
/// </summary>
public class MockPaymentProvider : IPaymentProvider
{
    public string ProviderName => "Mock";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public MockPaymentProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<string> CreatePaymentUrlAsync(Payment payment)
    {
        // Lấy base URL của app để tạo link scan
        var request = _httpContextAccessor.HttpContext?.Request;
        var baseUrl = request != null
            ? $"{request.Scheme}://{request.Host}"
            : "https://payhub.longdev.store";

        // URL mà QR code sẽ encode
        // Khi phone scan → mở trang này → hiển thị thông tin đơn hàng → tap Thanh toán
        var scanUrl = $"{baseUrl}/mock-pay.html?orderId={payment.OrderId}&amount={payment.Amount}&desc={Uri.EscapeDataString(payment.Description)}";

        // Dùng api.qrserver.com (miễn phí, không cần API key, không cần cài thư viện)
        // Trả về URL của ảnh QR — Frontend dùng trực tiếp trong <img src="...">
        var qrImageUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=300x300&margin=10&data={Uri.EscapeDataString(scanUrl)}";

        return Task.FromResult(qrImageUrl);
    }

    public Task<bool> VerifyWebhookAsync(string jsonContent, string signature)
    {
        // Mock provider không cần xác thực chữ ký
        return Task.FromResult(true);
    }
}
