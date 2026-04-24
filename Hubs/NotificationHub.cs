using Microsoft.AspNetCore.SignalR;
using System.Text.Json.Serialization;

namespace Dragon.Business.Hubs;

public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}

public record NotificationMessage(string Title, string Message, string OrderId);

[JsonSerializable(typeof(NotificationMessage))]
internal partial class HubJsonContext : JsonSerializerContext { }
