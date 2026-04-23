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
        var staff = await _db.StaffMembers.ToListAsync();
        var result = new List<StaffMemberWithStats>();

        foreach (var s in staff)
        {
            var totalTips = await _db.Payments
                .Where(p => p.StaffId == s.Id.ToString() && p.Status == PaymentStatus.Paid)
                .SumAsync(p => p.Amount);

            result.Add(new StaffMemberWithStats(s.Id, s.Name, s.Role, totalTips));
        }

        return result;
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
