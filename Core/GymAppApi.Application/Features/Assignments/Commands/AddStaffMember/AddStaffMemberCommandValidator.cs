using System.Globalization;
using FluentValidation;
using GymAppApi.Application.Common.Localization;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;

public class AddStaffMemberCommandValidator : AbstractValidator<AddStaffMemberCommand>
{
    public AddStaffMemberCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().Matches(@"^\+[1-9]\d{7,14}$");
        RuleFor(x => x.BranchId).GreaterThan(0);
        // Member bilerek burada henüz kabul edilmiyor - ürün modeli gerçek
        // bir üyeliğin arkasında bir Package/PackageAssignment olmasını
        // gerektiriyor, artık bu modül var (bkz. Packages özelliği) ama
        // Member ekleme akışı hâlâ ayrı (CreatePackageAssignment üzerinden).
        RuleFor(x => x.Role).Must(r => r is AssignmentRole.Trainer or AssignmentRole.BranchManager)
            .WithMessage(_ => AppMessages.Resolve("RoleMustBeTrainerOrBranchManager", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName));
    }
}
