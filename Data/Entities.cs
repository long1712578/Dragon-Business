using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Dragon.Business.Data;

public class StaffMember
{
    public int Id { get; set; }
    
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(50)]
    public string Role { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Payment
{
    [Key]
    public string OrderId { get; set; } = string.Empty; // Mã đơn hàng hệ thống
    
    public string? TransId { get; set; } // Mã giao dịch của ví điện tử (MoMo/ZaloPay)
    
    public decimal Amount { get; set; }
    
    public string Description { get; set; } = string.Empty;
    
    public PaymentStatus Status { get; set; } = PaymentStatus.Created;
    
    public string Provider { get; set; } = "ZaloPay"; // ZaloPay, MoMo
    
    public string? StaffId { get; set; } // Nếu thanh toán/tip cho nhân viên cụ thể
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? PaidAt { get; set; }
}

public enum PaymentStatus
{
    Created = 0,
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Expired = 4
}

public class Transaction
{
    public int Id { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty; // Log thô từ webhook
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
