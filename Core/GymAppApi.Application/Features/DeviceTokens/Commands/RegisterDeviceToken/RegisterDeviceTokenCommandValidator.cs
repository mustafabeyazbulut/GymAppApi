using FluentValidation;

namespace GymAppApi.Application.Features.DeviceTokens.Commands.RegisterDeviceToken;

public class RegisterDeviceTokenCommandValidator : AbstractValidator<RegisterDeviceTokenCommand>
{
    public RegisterDeviceTokenCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Platform).IsInEnum();
    }
}
