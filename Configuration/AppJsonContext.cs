using System.Text.Json.Serialization;
using Dragon.Business.Modules.Orders;
using Dragon.Business.Modules.Payments;
using Dragon.Business.Data;

namespace Dragon.Business
{
    // AOT-Friendly JSON Source Generation
    [JsonSerializable(typeof(PaymentSuccessEvent))]
    [JsonSerializable(typeof(ErrorResponse))]
    [JsonSerializable(typeof(Payment))]
    [JsonSerializable(typeof(List<Payment>))]
    [JsonSerializable(typeof(StaffMember))]
    [JsonSerializable(typeof(List<StaffMember>))]
    [JsonSerializable(typeof(StaffMemberWithStats))]
    [JsonSerializable(typeof(List<StaffMemberWithStats>))]
    [JsonSerializable(typeof(PaymentCreateRequest))]
    [JsonSerializable(typeof(PaymentRequestResponse))]
    [JsonSerializable(typeof(StatusUpdateRequest))]
    [JsonSerializable(typeof(StatusUpdateResponse))]
    [JsonSerializable(typeof(StaffCreateRequest))]
    [JsonSerializable(typeof(WebhookRequest))]
    [JsonSerializable(typeof(WebhookResponse))]
    [JsonSerializable(typeof(SignRequest))]
    [JsonSerializable(typeof(SignResponse))]
    [JsonSerializable(typeof(DeleteResponse))]
    [JsonSerializable(typeof(MockPaymentResponse))]
    [JsonSerializable(typeof(MockSimulateResponse))]
    [JsonSerializable(typeof(PaymentStatusUpdateEvent))]
    [JsonSerializable(typeof(PaymentCreatedEvent))]
    [JsonSerializable(typeof(PaymentStatus))]
    [JsonSerializable(typeof(object))]
    [JsonSerializable(typeof(List<TransactionResponse>))]
    // ── Café Order Management ──
    [JsonSerializable(typeof(CafeProductRow))]
    [JsonSerializable(typeof(List<CafeProductRow>))]
    [JsonSerializable(typeof(CafeOrderSummaryRow))]
    [JsonSerializable(typeof(List<CafeOrderSummaryRow>))]
    [JsonSerializable(typeof(CafeOrderDetailRow))]
    [JsonSerializable(typeof(CafeOrderItemRow))]
    [JsonSerializable(typeof(List<CafeOrderItemRow>))]
    [JsonSerializable(typeof(CreateCafeOrderRequest))]
    [JsonSerializable(typeof(CafeOrderItemRequest))]
    [JsonSerializable(typeof(List<CafeOrderItemRequest>))]
    [JsonSerializable(typeof(CreateProductRequest))]
    [JsonSerializable(typeof(CheckoutRequest))]
    [JsonSerializable(typeof(CheckoutResult))]
    [JsonSerializable(typeof(AvailabilityRequest))]
    [JsonSerializable(typeof(AvailabilityResponse))]
    [JsonSerializable(typeof(OrderStatusRequest))]
    [JsonSerializable(typeof(OrderStatusResponse))]
    [JsonSerializable(typeof(IdResponse))]
    public partial class AppJsonContext : JsonSerializerContext { }

    // Serializer tùy chỉnh cho RedisFlow để hỗ trợ Native AOT (100% không dùng reflection)
    public class AotRedisSerializer : RedisFlow.Abstractions.IMessageSerializer
    {
        public AotRedisSerializer() { } // Cần constructor public cho DI

        public byte[] Serialize<T>(T obj)
        {
            var typeInfo = AppJsonContext.Default.GetTypeInfo(typeof(T)) ?? throw new NotSupportedException($"Type {typeof(T)} not in AOT Context");
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(obj, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)typeInfo);
        }

        public T Deserialize<T>(byte[] data)
        {
            var typeInfo = AppJsonContext.Default.GetTypeInfo(typeof(T)) ?? throw new NotSupportedException($"Type {typeof(T)} not in AOT Context");
            return (T)System.Text.Json.JsonSerializer.Deserialize(data, typeInfo)!;
        }

        public object Deserialize(byte[] data, Type type)
        {
            var typeInfo = AppJsonContext.Default.GetTypeInfo(type) 
                ?? throw new InvalidOperationException($"Type {type} not found in AppJsonContext");
            return System.Text.Json.JsonSerializer.Deserialize(data, typeInfo)!;
        }
    }

    // Handler chặn các request gọi ra Authority (public internet) và bẻ lái vào mạng LAN
    public class InternalOidcRoutingHandler : DelegatingHandler
    {
        private readonly string _publicHost;
        private readonly string _internalHost;
        private readonly int _internalPort;

        public InternalOidcRoutingHandler(string publicHost, string internalHost, int internalPort)
        {
            _publicHost = publicHost;
            _internalHost = internalHost;
            _internalPort = internalPort;
            InnerHandler = new SocketsHttpHandler();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host == _publicHost)
            {
                var builder = new UriBuilder(request.RequestUri)
                {
                    Host = _internalHost,
                    Port = _internalPort,
                    Scheme = "http"
                };
                request.RequestUri = builder.Uri;
            }
            return base.SendAsync(request, cancellationToken);
        }
    }

    // DTOs
    public record ErrorResponse(string Message, string Error);
    public record DeleteResponse(string Message, string Id);
    public record AvailabilityRequest(bool IsAvailable);
    public record AvailabilityResponse(int Id, bool IsAvailable);
    public record OrderStatusRequest(int Status);
    public record OrderStatusResponse(int Id, int Status);
    public record IdResponse(int Id);
    public record PaymentCreateRequest(decimal Amount, string Desc, string StaffId);
    public record StatusUpdateRequest(int Status);
    public record StatusUpdateResponse(string OrderId, int Status);
    public record StaffCreateRequest(string Name, string Role);
    public record WebhookRequest(string JsonContent, string OrderId);
    public record WebhookResponse(int return_code, string return_message);
    public record SignRequest(string Data);
    public record SignResponse(string Data, string Mac);
    public record TransactionResponse(int Id, string OrderId, string Content, string CreatedAt);
    public record PaymentRequestResponse(string OrderId, string? PaymentUrl, string Provider);
    
    // Mock Payment Records
    public record MockPaymentResponse(string OrderId, string QrImageUrl, string Provider, string Message);
    public record MockSimulateResponse(bool Success, string OrderId, string Status);
    public record PaymentStatusUpdateEvent(string OrderId, int Status, string StatusText, string Provider, DateTimeOffset PaidAt);
}
