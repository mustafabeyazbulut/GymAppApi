using FluentValidation;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Validation;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;

public class RegisterRequestOtpCommandValidator : AbstractValidator<RegisterRequestOtpCommand>
{
    public RegisterRequestOtpCommandValidator(IPhoneNumberNormalizer phoneNumberNormalizer)
    {
        RuleFor(x => x.Phone).NotEmpty().ValidPhoneNumber(phoneNumberNormalizer);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
