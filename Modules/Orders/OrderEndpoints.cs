using Dragon.Business.Data;

namespace Dragon.Business.Modules.Orders;

public static class OrderEndpoints
{
    public static RouteGroupBuilder MapCafeOrderEndpoints(this RouteGroupBuilder group)
    {
        var orders = group.MapGroup("/orders").WithTags("Café Orders");
        var menu = group.MapGroup("/menu").WithTags("Café Menu");

        // ── Menu / Products ──────────────────────────
        menu.MapGet("/", async (CafeOrderService svc, string? category) =>
            Results.Ok(await svc.GetProductsAsync(category))
        ).RequireAuthorization("StaffOnly");

        menu.MapPost("/", async (CreateProductRequest req, CafeOrderService svc) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ErrorResponse("Name required", ""));
            if (req.Price <= 0) return Results.BadRequest(new ErrorResponse("Price must be > 0", ""));
            var id = await svc.CreateProductAsync(req);
            return Results.Created($"/api/menu/{id}", new IdResponse(id));
        }).RequireAuthorization("ManagerOnly");

        menu.MapPut("/{id:int}/availability", async (int id, AvailabilityRequest req, CafeOrderService svc) =>
        {
            var ok = await svc.UpdateProductAvailabilityAsync(id, req.IsAvailable);
            return ok ? Results.Ok(new AvailabilityResponse(id, req.IsAvailable)) : Results.NotFound(new ErrorResponse("Product not found", $"ID {id}"));
        }).RequireAuthorization("ManagerOnly");

        menu.MapDelete("/{id:int}", async (int id, CafeOrderService svc) =>
        {
            var ok = await svc.DeleteProductAsync(id);
            return ok ? Results.Ok(new DeleteResponse("Product deleted", id.ToString())) : Results.NotFound(new ErrorResponse("Product not found", $"ID {id}"));
        }).RequireAuthorization("ManagerOnly");

        // ── Orders ────────────────────────────────────
        orders.MapGet("/", async (CafeOrderService svc, int? status) =>
        {
            CafeOrderStatus? st = status.HasValue ? (CafeOrderStatus)status.Value : null;
            return Results.Ok(await svc.GetOrdersAsync(st));
        }).RequireAuthorization("StaffOnly");

        orders.MapGet("/{id:int}", async (int id, CafeOrderService svc) =>
        {
            var order = await svc.GetOrderByIdAsync(id);
            return order is null ? Results.NotFound(new ErrorResponse("Order not found", $"ID {id}")) : Results.Ok(order);
        }).RequireAuthorization("StaffOnly");

        orders.MapPost("/", async (CreateCafeOrderRequest req, CafeOrderService svc) =>
        {
            try
            {
                if (req.Items == null || req.Items.Count == 0) return Results.BadRequest(new ErrorResponse("Items required", ""));
                var id = await svc.CreateOrderAsync(req);
                return Results.Created($"/api/orders/{id}", new IdResponse(id));
            }
            catch (KeyNotFoundException ex) { return Results.NotFound(new ErrorResponse(ex.Message, "")); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new ErrorResponse(ex.Message, "")); }
        }).RequireAuthorization("StaffOnly");

        orders.MapPut("/{id:int}/status", async (int id, OrderStatusRequest req, CafeOrderService svc) =>
        {
            var ok = await svc.UpdateStatusAsync(id, (CafeOrderStatus)req.Status);
            return ok ? Results.Ok(new OrderStatusResponse(id, req.Status)) : Results.NotFound(new ErrorResponse("Order not found", $"ID {id}"));
        }).RequireAuthorization("StaffOnly");

        orders.MapPost("/{id:int}/checkout", async (int id, CheckoutRequest req, CafeOrderService svc) =>
        {
            try
            {
                var result = await svc.CheckoutOrderAsync(id, req.Provider ?? "Mock", req.StaffId);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex) { return Results.NotFound(new ErrorResponse(ex.Message, "")); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new ErrorResponse(ex.Message, "")); }
        }).RequireAuthorization("StaffOnly");

        orders.MapDelete("/{id:int}", async (int id, CafeOrderService svc) =>
        {
            var ok = await svc.DeleteOrderAsync(id);
            return ok ? Results.Ok(new DeleteResponse("Order deleted", id.ToString())) : Results.NotFound(new ErrorResponse("Order not found", $"ID {id}"));
        }).RequireAuthorization("ManagerOnly");

        return group;
    }
}
