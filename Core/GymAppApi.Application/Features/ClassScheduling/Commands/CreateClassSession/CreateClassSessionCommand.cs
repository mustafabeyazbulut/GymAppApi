using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.ClassScheduling.Commands.CreateClassSession;

public class CreateClassSessionCommand : IRequest<CreateClassSessionCommandResult>
{
    public int BranchId { get; set; }
    public int TrainerUserId { get; set; }
    public ClassSessionCategory Category { get; set; }
    public string Name { get; set; } = null!;
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int Capacity { get; set; }
    public int CancellationCutoffHours { get; set; }

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
