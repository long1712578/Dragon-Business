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
        // Native AOT: Bỏ qua EF Core SqlQueryRaw vì nó dùng reflection để map (gây crash)
        // Dùng DbDataReader là cách an toàn và hiệu năng cao nhất cho AOT
        var result = new List<StaffMemberWithStats>();
        var conn = _db.Database.GetDbConnection();
        
        try {
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    s.Id, 
                    s.Name, 
                    s.Role, 
                    COALESCE(SUM(CAST(p.Amount AS DECIMAL)), 0) as TotalTips
                FROM StaffMembers s
                LEFT JOIN Payments p ON p.StaffId = CAST(s.Id AS TEXT) AND p.Status = 2
                GROUP BY s.Id, s.Name, s.Role";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new StaffMemberWithStats(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetDecimal(3)
                ));
            }
        } finally {
            // EF Core sẽ tự quản lý connection nếu ta không can thiệp sâu, 
            // nhưng ở đây ta mở thì nên đóng nếu cần (hoặc để EF lo)
        }

        return result;
    }

    public async Task<StaffMember> CreateStaffAsync(string name, string role)
    {
        // Native AOT: Bỏ qua EF Add() vì nó kích hoạt Model Building (gây crash)
        // Dùng SQL INSERT thô là cách an toàn nhất
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO StaffMembers (Name, Role, CreatedAt) VALUES (@name, @role, @now); SELECT last_insert_rowid();";
        
        var pName = cmd.CreateParameter(); pName.ParameterName = "@name"; pName.Value = name; cmd.Parameters.Add(pName);
        var pRole = cmd.CreateParameter(); pRole.ParameterName = "@role"; pRole.Value = role; cmd.Parameters.Add(pRole);
        var pNow = cmd.CreateParameter(); pNow.ParameterName = "@now"; pNow.Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"); cmd.Parameters.Add(pNow);

        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        
        _logger.LogInformation("Created new staff member: {Name} with ID {Id}", name, id);
        
        return new StaffMember { 
            Id = id, 
            Name = name, 
            Role = role, 
            CreatedAt = DateTime.UtcNow 
        };
    }
}
