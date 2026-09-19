using FluentValidation;

namespace GymAppApi.Application.Features.ClassScheduling.Commands.EnrollInClassSession;

public class EnrollInClassSessionCommandValidator : AbstractValidator<EnrollInClassSessionCommand>
{
    public EnrollInClassSessionCommandValidator()
    {
        RuleFor(x => x.ClassSessionId).GreaterThan(0);
        RuleFor(x => x.PackageAssignmentId).GreaterThan(0);
    }
}
