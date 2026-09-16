using FluentValidation;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;

public class RegisterRequestOtpCommandValidator : AbstractValidator<RegisterRequestOtpCommand>
{
    public RegisterRequestOtpCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().Matches(@"^\+[1-9]\d{7,14}$");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
