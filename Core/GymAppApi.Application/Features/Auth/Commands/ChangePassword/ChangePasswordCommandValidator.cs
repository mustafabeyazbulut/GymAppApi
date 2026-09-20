using FluentValidation;

namespace GymAppApi.Application.Features.Auth.Commands.ChangePassword;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        // ResetPasswordCommandValidator ile aynı kural (bkz. o dosya) - tek
        // bir minimum uzunluk kontrolü, şifre karmaşıklığı politikası proje
        // genelinde bundan ibaret.
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}
