namespace GymAppApi.Domain.Common;

public abstract class EntityBase : IEntityBase
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
