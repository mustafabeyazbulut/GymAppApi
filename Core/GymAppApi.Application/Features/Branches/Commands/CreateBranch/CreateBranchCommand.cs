using MediatR;

namespace GymAppApi.Application.Features.Branches.Commands.CreateBranch;

public class CreateBranchCommand : IRequest<CreateBranchCommandResult>
{
    public int CompanyId { get; set; }
    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;
}
