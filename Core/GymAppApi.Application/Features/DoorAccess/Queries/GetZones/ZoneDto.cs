namespace GymAppApi.Application.Features.DoorAccess.Queries.GetZones;

public class ZoneDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string Name { get; set; } = null!;
}
