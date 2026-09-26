using System.Globalization;
using FluentValidation;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Localization;
using GymAppApi.Application.Common.Validation;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;

public class AddStaffMemberCommandValidator : AbstractValidator<AddStaffMemberCommand>
{
    public AddStaffMemberCommandValidator(IPhoneNumberNormalizer phoneNumberNormalizer)
    {
        RuleFor(x => x.Phone).NotEmpty().ValidPhoneNumber(phoneNumberNormalizer);
        RuleFor(x => x.BranchId).GreaterThan(0);
        // Sadece şube personeli rolleri. Üyelik bir atama değil paket
        // tanımlamadır (CreatePackageAssignment) - senaryo §10.7.
        RuleFor(x => x.Role).Must(r => r is AssignmentRole.Trainer or AssignmentRole.BranchManager)
            .WithMessage(_ => AppMessages.Resolve("RoleMustBeTrainerOrBranchManager", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName));
    }
}
