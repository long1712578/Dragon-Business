using Dragon.Business.Hubs;
using Dragon.Business.Modules.Payments;
using Microsoft.AspNetCore.SignalR;
using RedisFlow.Abstractions;
using RedisFlow.Messages;

namespace Dragon.Business.Modules.Notifications;

public class PaymentNotificationHandler : 
    IMessageHandler<PaymentSuccessEvent>,
    IMessageHandler<PaymentCreatedEvent>
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
        _logger.LogInformation("🔔 [Consumer] Nhận event thanh toán thành công: {OrderId} (MsgId: {MessageId})", message.OrderId, context.Message.Id);

        // Gửi thông báo realtime tới Dashboard qua SignalR
        // Lưu ý: Dùng mảng [] cho tham số để chuẩn AOT
        await _hubContext.Clients.All.SendCoreAsync("PaymentStatusUpdated", [new PaymentStatusUpdateEvent(
            message.OrderId,
            2, // Paid
            "Paid",
            message.Provider,
            message.PaidAt
        )]);
        
        _logger.LogInformation("✅ [Consumer] Đã gửi thông báo realtime cho {OrderId}", message.OrderId);
    }

    public async Task HandleAsync(PaymentCreatedEvent message, MessageContext context)
    {
        _logger.LogInformation("🔔 [Consumer] Nhận event thanh toán mới tạo: {OrderId} (MsgId: {MessageId})", message.OrderId, context.Message.Id);

        await _hubContext.Clients.All.SendCoreAsync("PaymentStatusUpdated", [new PaymentStatusUpdateEvent(
            message.OrderId,
            0, // Created/Pending
            "Pending",
            message.Provider,
            message.CreatedAt
        )]);
        
        _logger.LogInformation("✅ [Consumer] Đã gửi thông báo realtime Pending cho {OrderId}", message.OrderId);
    }
}
