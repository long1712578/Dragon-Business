using Dragon.Business.Data;

namespace Dragon.Business.Modules.Staff;

public static class StaffEndpoints
{
    public static RouteGroupBuilder MapStaffEndpoints(this RouteGroupBuilder group)
    {
        var staff = group.MapGroup("/staff").WithTags("Staff");

        staff.MapGet("/", async (StaffService staffService) =>
        {
            var result = await staffService.GetAllStaffWithStatsAsync();
            return Results.Ok(result);
        }).RequireAuthorization("StaffOnly");

        staff.MapPost("/", async (StaffCreateRequest req, StaffService staffService) =>
        {
            var result = await staffService.CreateStaffAsync(req.Name, req.Role);
            return Results.Ok(result);
        }).RequireAuthorization("ManagerOnly");

        staff.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var member = await db.StaffMembers.FindAsync(id);
            if (member == null) return Results.NotFound(new ErrorResponse("Staff not found", $"ID {id}"));
            db.StaffMembers.Remove(member);
            await db.SaveChangesAsync();
            return Results.Ok(new DeleteResponse("Staff deleted", id.ToString()));
        }).RequireAuthorization("ManagerOnly");

        return group;
    }
}
