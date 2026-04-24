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

        var appTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var appTransId = $"{DateTime.Now:yyMMdd}_{payment.OrderId}";
        
        var embedData = """{"redirecturl":"https://payhub.longdev.store"}""";
        var items = "[]";
        
        var data = $"{appId}|{appTransId}|DragonUser|{(long)payment.Amount}|{appTime}|{embedData}|{items}";
        var mac = ComputeHmacSha256(data, key1);

        using var client = new HttpClient();
        var payload = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "app_id", appId },
            { "app_user", "DragonUser" },
            { "app_time", appTime },
            { "amount", ((long)payment.Amount).ToString() },
            { "app_trans_id", appTransId },
            { "embed_data", embedData },
            { "item", items },
            { "description", payment.Description },
            { "mac", mac }
        });

        try {
            var response = await client.PostAsync(endpoint, payload);
            var content = await response.Content.ReadAsStringAsync();
            
            // Console log để debug trong pod K8s
            Console.WriteLine($"[ZaloPay Response] {content}");
            
            if (content.Contains("\"order_url\":\"")) {
                var start = content.IndexOf("\"order_url\":\"") + 13;
                var end = content.IndexOf("\"", start);
                var url = content.Substring(start, end - start);
                return url.Replace("\\/", "/");
            }

            // Nếu không có order_url, có thể là lỗi từ ZaloPay (Sai AppId/Key)
            if (content.Contains("\"return_message\":\"")) {
                 var start = content.IndexOf("\"return_message\":\"") + 18;
                 var end = content.IndexOf("\"", start);
                 var msg = content.Substring(start, end - start);
                 return $"https://longdev.store/error?msg={System.Net.WebUtility.UrlEncode(msg)}";
            }
            
            return "https://longdev.store/error?msg=ZaloPayAPIError";
        } catch (Exception ex) {
            return $"https://longdev.store/error?msg={System.Net.WebUtility.UrlEncode(ex.Message)}";
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
