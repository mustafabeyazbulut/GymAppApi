using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.CreateAssignment;

public class CreateAssignmentCommand : IRequest<CreateAssignmentCommandResult>
{
    public int UserId { get; set; }
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }
}
