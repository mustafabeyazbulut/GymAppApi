using System.Globalization;
using GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;
using GymAppApi.Application.Features.Assignments.Commands.InviteGymAdmin;
using GymAppApi.Application.Features.Auth.Commands.RegisterComplete;
using GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;
using GymAppApi.Application.Features.Companies.Commands.CreateCompany;
using GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Security;

namespace GymAppApi.UnitTests.Common.Validation;

// Telefon alan tüm validator'ların aynı ValidPhoneNumber kuralını (ve
// dolayısıyla handler'ların kullandığı IPhoneNumberNormalizer'ı) kullandığını
// doğrular - eski "^\+[1-9]\d{7,14}$" regex'i boşluklu/yerel girişi
// reddediyor, "+900001234567" gibi var olamayacak numaraları kabul ediyordu.
public class PhoneNumberValidatorsTests
{
    private static readonly PhoneNumberNormalizer Normalizer = new();

    public static TheoryData<string> ValidPhones => new()
    {
        "+905551234567",
        "05551234567",
        "+90 555 123 45 67",
        "+4915123456789",
        "+1 (201) 555-0123",
        "+447400123456",
    };

    public static TheoryData<string> InvalidPhones => new()
    {
        "+90555123",
        "+90555ABC4567",
        "+900001234567",
        "not-a-phone",
    };

    // Her validator için: telefon dışındaki alanları geçerli doldurup sadece
    // telefon kuralının sonucunu döndürür.
    private static IEnumerable<(string Name, Func<string, bool> PhoneIsValid)> AllPhoneValidators()
    {
        yield return (nameof(RegisterRequestOtpCommandValidator), phone =>
            !new RegisterRequestOtpCommandValidator(Normalizer).Validate(new RegisterRequestOtpCommand { Phone = phone })
                .Errors.Any(e => e.PropertyName == nameof(RegisterRequestOtpCommand.Phone)));
        yield return (nameof(RegisterCompleteCommandValidator), phone =>
            !new RegisterCompleteCommandValidator(Normalizer).Validate(new RegisterCompleteCommand { Phone = phone, FullName = "x", PhoneCode = "123456", Password = "Sifre123!" })
                .Errors.Any(e => e.PropertyName == nameof(RegisterCompleteCommand.Phone)));
        yield return (nameof(CreateCompanyCommandValidator), phone =>
            !new CreateCompanyCommandValidator(Normalizer).Validate(new CreateCompanyCommand { GymAdminPhone = phone, CompanyName = "x" })
                .Errors.Any(e => e.PropertyName == nameof(CreateCompanyCommand.GymAdminPhone)));
        yield return (nameof(InviteGymAdminCommandValidator), phone =>
            !new InviteGymAdminCommandValidator(Normalizer).Validate(new InviteGymAdminCommand { Phone = phone, CompanyId = 1 })
                .Errors.Any(e => e.PropertyName == nameof(InviteGymAdminCommand.Phone)));
        yield return (nameof(AddStaffMemberCommandValidator), phone =>
            !new AddStaffMemberCommandValidator(Normalizer).Validate(new AddStaffMemberCommand { Phone = phone, BranchId = 1, Role = AssignmentRole.Trainer })
                .Errors.Any(e => e.PropertyName == nameof(AddStaffMemberCommand.Phone)));
        yield return (nameof(CreatePackageAssignmentCommandValidator), phone =>
            !new CreatePackageAssignmentCommandValidator(Normalizer).Validate(new CreatePackageAssignmentCommand { MemberPhone = phone, PackageId = 1 })
                .Errors.Any(e => e.PropertyName == nameof(CreatePackageAssignmentCommand.MemberPhone)));
    }

    [Theory]
    [MemberData(nameof(ValidPhones))]
    public void EveryPhoneValidator_AcceptsValidNumbersInAnyCommonWriting(string phone)
    {
        foreach (var (name, phoneIsValid) in AllPhoneValidators())
        {
            Assert.True(phoneIsValid(phone), $"{name} '{phone}' numarasını reddetti.");
        }
    }

    [Theory]
    [MemberData(nameof(InvalidPhones))]
    public void EveryPhoneValidator_RejectsInvalidNumbers(string phone)
    {
        foreach (var (name, phoneIsValid) in AllPhoneValidators())
        {
            Assert.False(phoneIsValid(phone), $"{name} '{phone}' numarasını kabul etti.");
        }
    }

    [Fact]
    public void InvalidPhone_ErrorMessage_IsLocalizedByCurrentUICulture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("tr");
            var turkish = new RegisterRequestOtpCommandValidator(Normalizer).Validate(new RegisterRequestOtpCommand { Phone = "+90555123" });
            Assert.Contains(turkish.Errors, e => e.ErrorMessage.Contains("geçerli bir telefon numarası"));

            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var english = new RegisterRequestOtpCommandValidator(Normalizer).Validate(new RegisterRequestOtpCommand { Phone = "+90555123" });
            Assert.Contains(english.Errors, e => e.ErrorMessage.Contains("valid phone number"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
