namespace GymAppApi.Domain.Common;

public interface IDeactivatable
{
    bool IsActive { get; set; }
}
