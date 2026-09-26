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
        // Member bilerek burada henüz kabul edilmiyor - ürün modeli gerçek
        // bir üyeliğin arkasında bir Package/PackageAssignment olmasını
        // gerektiriyor, artık bu modül var (bkz. Packages özelliği) ama
        // Member ekleme akışı hâlâ ayrı (CreatePackageAssignment üzerinden).
        RuleFor(x => x.Role).Must(r => r is AssignmentRole.Trainer or AssignmentRole.BranchManager)
            .WithMessage(_ => AppMessages.Resolve("RoleMustBeTrainerOrBranchManager", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName));
    }
}
