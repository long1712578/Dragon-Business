using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Dragon.Business.Data;

public record StaffMemberWithStats(int Id, string Name, string Role, decimal TotalTips);

public class StaffMember
{
    public int Id { get; set; }
    
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(50)]
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

    public string? PaymentUrl { get; set; }
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

// ─── Café Order Management Entities ───────────────────────────

public class CafeProduct
{
    public int Id { get; set; }
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Coffee"; // Coffee, Tea, Food, Other
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; } = true;
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CafeOrder
{
    public int Id { get; set; }
    public string TableNumber { get; set; } = "1";
    public string? CustomerName { get; set; }
    public string? Note { get; set; }
    public decimal TotalAmount { get; set; }
    public CafeOrderStatus Status { get; set; } = CafeOrderStatus.Pending;
    public string? StaffId { get; set; }
    public string? PaymentOrderId { get; set; } // Link đến Payment.OrderId sau khi checkout
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public class CafeOrderItem
{
    public int Id { get; set; }
    public int CafeOrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty; // Denormalized cho Native AOT
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public string? CustomNote { get; set; } // e.g., "ít đường", "không đá"
}

public enum CafeOrderStatus
{
    Pending   = 0, // Mới tạo, đang chờ
    Preparing = 1, // Đang pha chế
    Ready     = 2, // Xong, chờ khách lấy
    Completed = 3, // Đã thanh toán & hoàn tất
    Cancelled = 4  // Đã hủy
}

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<CafeProduct> CafeProducts => Set<CafeProduct>();
    public DbSet<CafeOrder> CafeOrders => Set<CafeOrder>();
    public DbSet<CafeOrderItem> CafeOrderItems => Set<CafeOrderItem>();
}

