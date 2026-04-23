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
        
        var embedData = """{"redirecturl":"https://dragon.vn/payment-success"}""";
        var items = "[]";
        
        // data = app_id + "|" + app_trans_id + "|" + app_user + "|" + amount + "|" + app_time + "|" + embed_data + "|" + item
        var data = $"{appId}|{appTransId}|DragonUser|{(long)payment.Amount}|{appTime}|{embedData}|{items}";
        var mac = ComputeHmacSha256(data, key1);

        var payload = new Dictionary<string, string>
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
        };

        // Gửi request tới ZaloPay (trong thực tế dùng HttpClient)
        // Ở đây tôi trả về URL giả lập hoặc log ra
        return $"https://sb-openapi.zalopay.vn/pay?order={appTransId}&mac={mac}";
    }

    public Task<bool> VerifyWebhookAsync(string jsonContent, string signature)
    {
        var key2 = _config["ZaloPay:Key2"] ?? "Iyz2LcUDt69876zY8v6968h76z6895pzed";
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
