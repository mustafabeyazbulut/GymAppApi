using FluentValidation;

namespace GymAppApi.Application.Features.Assignments.Commands.CreateAssignment;

public class CreateAssignmentCommandValidator : AbstractValidator<CreateAssignmentCommand>
{
    public CreateAssignmentCommandValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.CompanyId).GreaterThan(0);
    }
}
