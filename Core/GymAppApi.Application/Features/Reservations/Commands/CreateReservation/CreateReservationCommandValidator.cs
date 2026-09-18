using FluentValidation;

namespace GymAppApi.Application.Features.Reservations.Commands.CreateReservation;

public class CreateReservationCommandValidator : AbstractValidator<CreateReservationCommand>
{
    public CreateReservationCommandValidator()
    {
        RuleFor(x => x.PackageAssignmentId).GreaterThan(0);
        RuleFor(x => x.TrainerId).GreaterThan(0);
        RuleFor(x => x.ScheduledAt).NotEmpty();
    }
}
