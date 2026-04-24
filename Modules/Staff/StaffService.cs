using Dragon.Business.Data;
using Microsoft.EntityFrameworkCore;

namespace Dragon.Business.Modules.Staff;

public class StaffService
{
    private readonly AppDbContext _db;
    private readonly ILogger<StaffService> _logger;

    public StaffService(AppDbContext db, ILogger<StaffService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<StaffMemberWithStats>> GetAllStaffWithStatsAsync()
    {
        // Native AOT: Sử dụng SQL thô để tránh dynamic LINQ compilation
        // Đồng thời tối ưu hiệu năng: Join và GroupBy ngay trong DB thay vì N+1 query
        var sql = @"
            SELECT 
                s.Id, 
                s.Name, 
                s.Role, 
                COALESCE(SUM(CAST(p.Amount AS DECIMAL)), 0) as TotalTips
            FROM StaffMembers s
            LEFT JOIN Payments p ON p.StaffId = CAST(s.Id AS TEXT) AND p.Status = 2
            GROUP BY s.Id, s.Name, s.Role";

        return await _db.Database.SqlQueryRaw<StaffMemberWithStats>(sql).ToListAsync();
    }

    public async Task<StaffMember> CreateStaffAsync(string name, string role)
    {
        var staff = new StaffMember { Name = name, Role = role };
        _db.StaffMembers.Add(staff);
        await _db.SaveChangesAsync();
        
        _logger.LogInformation("Created new staff member: {Name}", name);
        return staff;
    }
}
