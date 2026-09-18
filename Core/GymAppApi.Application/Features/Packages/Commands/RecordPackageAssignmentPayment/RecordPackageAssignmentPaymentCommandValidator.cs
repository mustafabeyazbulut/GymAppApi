using FluentValidation;

namespace GymAppApi.Application.Features.Packages.Commands.RecordPackageAssignmentPayment;

public class RecordPackageAssignmentPaymentCommandValidator : AbstractValidator<RecordPackageAssignmentPaymentCommand>
{
    public RecordPackageAssignmentPaymentCommandValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}
