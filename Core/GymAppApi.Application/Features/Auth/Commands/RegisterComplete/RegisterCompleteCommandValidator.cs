using FluentValidation;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterComplete;

public class RegisterCompleteCommandValidator : AbstractValidator<RegisterCompleteCommand>
{
    public RegisterCompleteCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty().Matches(@"^\+[1-9]\d{7,14}$");
        RuleFor(x => x.PhoneCode).NotEmpty().Matches(@"^\d{6}$");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.EmailCode).NotEmpty().Matches(@"^\d{6}$").When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}
