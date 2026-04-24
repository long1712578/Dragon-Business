using System.Security.Cryptography;
using System.Text;
using Dragon.Business.Data;

namespace Dragon.Business.Modules.Payments;

public class ZaloPayAdapter : IPaymentProvider
{
    private readonly IConfiguration _config;
    public string ProviderName => "ZaloPay";

    public ZaloPayAdapter(IConfiguration config)
    {
        _config = config;
    }

    public async Task<string> CreatePaymentUrlAsync(Payment payment)
    {
        var appId = _config["ZaloPay:AppId"] ?? "2553";
        var key1 = _config["ZaloPay:Key1"] ?? "9ph3199y9th6883v69h761b7z6n83pzed";
        var endpoint = _config["ZaloPay:Endpoint"] ?? "https://sb-openapi.zalopay.vn/v2/create";

        var vnNow = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var appTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        // app_trans_id phải dùng ngày Việt Nam (UTC+7), không dùng UTC
        // Server K3s chạy UTC, nếu dùng DateTime.Now/UtcNow sẽ sai ngày → ZaloPay reject
        var appTransId = $"{vnNow:yyMMdd}_{payment.OrderId}";
        var appUser = "DragonPayUser";
        var amount = ((long)payment.Amount).ToString();
        var embedData = "{}";
        var items = "[]";
        
        // CHUẨN MAC V2: app_id|app_trans_id|app_user|amount|app_time|embed_data|item
        var data = $"{appId}|{appTransId}|{appUser}|{amount}|{appTime}|{embedData}|{items}";
        var mac = ComputeHmacSha256(data, key1);

        using var client = new HttpClient();
        var payload = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "app_id", appId },
            { "app_user", appUser },
            { "app_time", appTime },
            { "amount", amount },
            { "app_trans_id", appTransId },
            { "embed_data", embedData },
            { "item", items },
            { "description", payment.Description },
            { "mac", mac }
        });

        try {
            var response = await client.PostAsync(endpoint, payload);
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[ZaloPay API] Request Data: {data}");
            Console.WriteLine($"[ZaloPay API] Response: {content}");
            
            if (content.Contains("\"order_url\":\"")) {
                var start = content.IndexOf("\"order_url\":\"") + 13;
                var end = content.IndexOf("\"", start);
                return content.Substring(start, end - start).Replace("\\/", "/");
            }

            // Nếu lỗi, log lại và trả về chuỗi trống để Frontend biết mà xử lý
            return string.Empty;
        } catch (Exception ex) {
            Console.WriteLine($"[ZaloPay API] Exception: {ex.Message}");
            return string.Empty;
        }
    }

    public Task<bool> VerifyWebhookAsync(string jsonContent, string signature)
    {
        var key2 = _config["ZaloPay:Key2"] ?? "Iyz2unPNSW0S1ge9df9uPaLqy0S89S6O";
        var expectedMac = ComputeHmacSha256(jsonContent, key2);
        return Task.FromResult(expectedMac == signature);
    }

    private string ComputeHmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }
}
