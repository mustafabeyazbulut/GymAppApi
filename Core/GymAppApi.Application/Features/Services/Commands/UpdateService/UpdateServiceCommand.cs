using FluentValidation;
using GymAppApi.Application.Features.Services.Commands.CreateService;
using MediatR;

namespace GymAppApi.Application.Features.Services.Commands.UpdateService;

public class UpdateServiceCommand : IRequest<ServiceDto>
{
    // Controller tarafından route'tan set edilir.
    public int ServiceId { get; set; }
    public string Name { get; set; } = null!;
}

public class UpdateServiceCommandValidator : AbstractValidator<UpdateServiceCommand>
{
    public UpdateServiceCommandValidator() => RuleFor(x => x.Name).SetValidator(new ServiceNameValidator());
}
