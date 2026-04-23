namespace Dragon.Business.Modules.Payments;

public record PaymentSuccessEvent(
    string OrderId, 
    decimal Amount, 
    string? StaffId, 
    DateTime PaidAt,
    string Provider
);
