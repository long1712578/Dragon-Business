using Dragon.Business.Hubs;
using Dragon.Business.Modules.Payments;
using Microsoft.AspNetCore.SignalR;
using RedisFlow.Abstractions;

namespace Dragon.Business.Modules.Notifications;

/// <summary>
/// Consumer xử lý thông báo thanh toán.
/// Tách biệt hoàn toàn logic thông báo khỏi PaymentService (Event-Driven Architecture).
/// </summary>
public class PaymentNotificationHandler : IMessageHandler<PaymentSuccessEvent>
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<PaymentNotificationHandler> _logger;

    public PaymentNotificationHandler(IHubContext<NotificationHub> hubContext, ILogger<PaymentNotificationHandler> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task HandleAsync(PaymentSuccessEvent message, MessageContext context)
    {
        _logger.LogInformation("🔔 [Consumer] Nhận event thanh toán thành công: {OrderId} (MsgId: {MessageId})", message.OrderId, context.MessageId);

        // Gửi thông báo realtime tới Dashboard qua SignalR
        // Lưu ý: Dùng mảng [] cho tham số để chuẩn AOT
        await _hubContext.Clients.All.SendCoreAsync("PaymentStatusUpdated", [new PaymentStatusUpdateEvent(
            message.OrderId,
            2, // Paid
            "Paid",
            "ZaloPay",
            DateTimeOffset.UtcNow
        )]);
        
        _logger.LogInformation("✅ [Consumer] Đã gửi thông báo realtime cho {OrderId}", message.OrderId);
    }
}
