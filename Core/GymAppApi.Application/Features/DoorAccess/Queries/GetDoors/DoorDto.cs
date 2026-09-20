namespace GymAppApi.Application.Features.DoorAccess.Queries.GetDoors;

public class DoorDto
{
    public int Id { get; set; }
    public int ZoneId { get; set; }
    public string Name { get; set; } = null!;
}
