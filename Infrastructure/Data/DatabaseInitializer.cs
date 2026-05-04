using Dragon.Business.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Dragon.Business.Infrastructure.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Tạo bảng và Migration (idempotent)
        var seedSql = @"
        CREATE TABLE IF NOT EXISTS ""StaffMembers"" (
            ""Id""        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""Name""      TEXT    NOT NULL,
            ""Role""      TEXT    NOT NULL,
            ""CreatedAt"" TEXT    NOT NULL DEFAULT (datetime('now'))
        );
        CREATE TABLE IF NOT EXISTS ""Payments"" (
            ""OrderId""     TEXT    NOT NULL PRIMARY KEY,
            ""TransId""     TEXT    NULL,
            ""Amount""      DECIMAL NOT NULL DEFAULT 0,
            ""Description"" TEXT    NULL,
            ""Status""      INTEGER NOT NULL DEFAULT 0,
            ""Provider""    TEXT    NOT NULL DEFAULT 'ZaloPay',
            ""StaffId""     TEXT    NULL,
            ""CreatedAt""   TEXT    NOT NULL DEFAULT (datetime('now')),
            ""PaidAt""      TEXT    NULL,
            ""PaymentUrl""  TEXT    NULL
        );
        CREATE INDEX IF NOT EXISTS ""idx_payments_staff"" ON ""Payments"" (""StaffId"");
        CREATE INDEX IF NOT EXISTS ""idx_payments_status"" ON ""Payments"" (""Status"");

        CREATE TABLE IF NOT EXISTS ""Transactions"" (
            ""Id""        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""OrderId""   TEXT    NOT NULL DEFAULT '',
            ""Content""   TEXT    NOT NULL DEFAULT '',
            ""CreatedAt"" TEXT    NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS ""CafeProducts"" (
            ""Id""          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""Name""        TEXT    NOT NULL,
            ""Category""    TEXT    NOT NULL DEFAULT 'Coffee',
            ""Price""       DECIMAL NOT NULL DEFAULT 0,
            ""IsAvailable"" INTEGER NOT NULL DEFAULT 1,
            ""ImageUrl""    TEXT    NULL,
            ""CreatedAt""   TEXT    NOT NULL DEFAULT (datetime('now'))
        );
        CREATE INDEX IF NOT EXISTS ""idx_cafeproducts_category"" ON ""CafeProducts"" (""Category"");

        CREATE TABLE IF NOT EXISTS ""CafeOrders"" (
            ""Id""             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""TableNumber""    TEXT    NOT NULL DEFAULT '1',
            ""CustomerName""   TEXT    NULL,
            ""Note""           TEXT    NULL,
            ""TotalAmount""    DECIMAL NOT NULL DEFAULT 0,
            ""Status""         INTEGER NOT NULL DEFAULT 0,
            ""StaffId""        TEXT    NULL,
            ""PaymentOrderId"" TEXT    NULL,
            ""CreatedAt""      TEXT    NOT NULL DEFAULT (datetime('now')),
            ""CompletedAt""    TEXT    NULL
        );
        CREATE INDEX IF NOT EXISTS ""idx_cafeorders_status""   ON ""CafeOrders"" (""Status"");
        CREATE INDEX IF NOT EXISTS ""idx_cafeorders_created""  ON ""CafeOrders"" (""CreatedAt"");

        CREATE TABLE IF NOT EXISTS ""CafeOrderItems"" (
            ""Id""          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""CafeOrderId"" INTEGER NOT NULL,
            ""ProductId""   INTEGER NOT NULL,
            ""ProductName"" TEXT    NOT NULL DEFAULT '',
            ""UnitPrice""   DECIMAL NOT NULL DEFAULT 0,
            ""Quantity""    INTEGER NOT NULL DEFAULT 1,
            ""CustomNote"" TEXT    NULL
        );
        CREATE INDEX IF NOT EXISTS ""idx_cafeorderitems_order"" ON ""CafeOrderItems"" (""CafeOrderId"");
        ";
        
        await db.Database.ExecuteSqlRawAsync(seedSql);

        // HACK: Tự động thêm cột PaymentUrl nếu chưa có
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Payments ADD COLUMN PaymentUrl TEXT NULL;");
        }
        catch { /* Đã tồn tại */ }

        // 2. Seed staff nếu chưa có
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "SELECT COUNT(*) FROM StaffMembers";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        if (count == 0)
        {
            cmd.CommandText = @"
            INSERT INTO StaffMembers (Name, Role, CreatedAt) VALUES
                ('Long Pham',        'TechLead', datetime('now')),
                ('Dragon Employee',  'Barista',  datetime('now'));
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // Seed Café Menu — Trung Nguyên Legend (batch INSERT, VPS-optimized)
        cmd.CommandText = "SELECT COUNT(*) FROM CafeProducts";
        var productCount = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        if (productCount == 0)
        {
            // 1 batch INSERT duy nhất → tối thiểu round-trip, tốt cho SQLite trên VPS giới hạn RAM
            cmd.CommandText = @"
            INSERT INTO CafeProducts (Name, Category, Price, IsAvailable, CreatedAt) VALUES
            -- ═══ NĂNG LƯỢNG SÁNG TẠO (Phin/Hot - Drip Coffee) ═══
            ('Năng Lượng Tư Duy - Sáng Tạo 1',     'Drip Coffee', 34000, 1, datetime('now')),
            ('Năng Lượng Khám Phá - Sáng Tạo 2',   'Drip Coffee', 42000, 1, datetime('now')),
            ('Năng Lượng Ý Tưởng - Sáng Tạo 3',    'Drip Coffee', 44000, 1, datetime('now')),
            ('Năng Lượng Sáng Tạo - Sáng Tạo 4',   'Drip Coffee', 45000, 1, datetime('now')),
            ('Năng Lượng Thành Công - Sáng Tạo 5', 'Drip Coffee', 48000, 1, datetime('now')),
            ('Năng Lượng Đột Phá - Sáng Tạo 8',    'Drip Coffee', 69000, 1, datetime('now')),
            -- ═══ NĂNG LƯỢNG KẾT NỐI (Espresso Based) ═══
            ('Success Đá Viên',         'Coffee', 40000, 1, datetime('now')),
            ('Success Sữa Đá',          'Coffee', 45000, 1, datetime('now')),
            ('Cà Phê Bọt Biển',         'Coffee', 44000, 1, datetime('now')),
            ('Bạc Xỉu',                 'Coffee', 44000, 1, datetime('now')),
            ('Cà Phê Mother Land',      'Coffee', 48000, 1, datetime('now')),
            ('Cà Phê L''amour',         'Coffee', 48000, 1, datetime('now')),
            ('Espresso',                'Coffee', 44000, 1, datetime('now')),
            ('Double Espresso',         'Coffee', 51000, 1, datetime('now')),
            ('Cà Phê Tonic Trái Cây',  'Coffee', 55000, 1, datetime('now')),
            ('Latte With Jelly',        'Coffee', 55000, 1, datetime('now')),
            ('Caramel Macchiato',       'Coffee', 59000, 1, datetime('now')),
            ('Cà Phê Affogato',         'Coffee', 62000, 1, datetime('now')),
            ('Cà Phê Đá Xay Socola',   'Coffee', 62000, 1, datetime('now')),
            ('Cà Phê Đá Xay Caramel',  'Coffee', 62000, 1, datetime('now')),
            ('Cà Phê Hoa Hồng',         'Coffee', 62000, 1, datetime('now')),
            ('Cà Phê Khoai Môn',        'Coffee', 69000, 1, datetime('now')),
            ('Americano',               'Coffee', 51000, 1, datetime('now')),
            ('Cappuccino',              'Coffee', 55000, 1, datetime('now')),
            ('Latte',                   'Coffee', 59000, 1, datetime('now')),
            -- ═══ NĂNG LƯỢNG GIÀU CÓ (Signature) ═══
            ('Legend Coffee Signature', 'Signature', 150000, 1, datetime('now')),
            ('Cà Phê Mè (Set)',         'Signature',  69000, 1, datetime('now')),
            ('Cà Phê Dừa (Set)',        'Signature',  69000, 1, datetime('now')),
            ('Cà Phê Trứng',            'Signature',  69000, 1, datetime('now')),
            -- ═══ NĂNG LƯỢNG THANH NHIỆT (Refreshing) ═══
            ('Nước Suối',               'Refreshing', 15000, 1, datetime('now')),
            ('Sữa Tươi Đá',             'Refreshing', 34000, 1, datetime('now')),
            ('Sữa Tươi Nóng',           'Refreshing', 41000, 1, datetime('now')),
            ('Sinh Tố Chanh Dây',       'Smoothie',   48000, 1, datetime('now')),
            ('Kim Quất Đá Xay',         'Smoothie',   48000, 1, datetime('now')),
            ('Sữa Chua Đá',             'Refreshing', 48000, 1, datetime('now')),
            ('Cacao Sữa',               'Refreshing', 48000, 1, datetime('now')),
            ('Trà Xanh Thạch Cà Phê',  'Tea',        55000, 1, datetime('now')),
            ('Trà Sữa Legend',          'Tea',        55000, 1, datetime('now')),
            ('Sinh Tố Bơ',              'Smoothie',   62000, 1, datetime('now')),
            ('Sinh Tố Dâu',             'Smoothie',   62000, 1, datetime('now')),
            ('Soda Táo Quế',            'Refreshing', 62000, 1, datetime('now')),
            ('Trà Vải Hoa Hồng',        'Tea',        62000, 1, datetime('now'));
            ";
            await cmd.ExecuteNonQueryAsync();
            Log.Information("[Seed] Seeded 43 Trung Nguyen Legend menu items");
        }
    }
}
