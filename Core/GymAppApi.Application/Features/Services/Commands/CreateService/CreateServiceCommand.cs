using System.Globalization;
using FluentValidation;
using GymAppApi.Application.Common.Localization;
using MediatR;

namespace GymAppApi.Application.Features.Services.Commands.CreateService;

public class CreateServiceCommand : IRequest<ServiceDto>
{
    // Controller tarafından route'tan set edilir.
    public int BranchId { get; set; }
    public string Name { get; set; } = null!;
}

public class ServiceNameValidator : AbstractValidator<string?>
{
    public ServiceNameValidator()
    {
        RuleFor(name => name).NotEmpty().WithMessage(_ => Localized("ServiceNameRequired"));
        RuleFor(name => name!.Trim()).MaximumLength(60).When(name => name is not null).WithName("Name")
            .WithMessage(_ => Localized("ServiceNameTooLong"));
    }

    private static string Localized(string code) =>
        AppMessages.Resolve(code, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
}

public class CreateServiceCommandValidator : AbstractValidator<CreateServiceCommand>
{
    public CreateServiceCommandValidator() => RuleFor(x => x.Name).SetValidator(new ServiceNameValidator());
}
