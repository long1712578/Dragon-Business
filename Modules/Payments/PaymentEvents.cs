namespace Dragon.Business.Modules.Payments;

public record PaymentSuccessEvent(
    string OrderId, 
    decimal Amount, 
    string? StaffId, 
    DateTime PaidAt,
    string Provider
);

public record PaymentCreatedEvent(
    string OrderId,
    decimal Amount,
    string? StaffId,
    DateTime CreatedAt,
    string Provider
);
