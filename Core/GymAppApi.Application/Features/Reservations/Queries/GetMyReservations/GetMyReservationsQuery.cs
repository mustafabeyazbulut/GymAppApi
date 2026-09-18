using MediatR;

namespace GymAppApi.Application.Features.Reservations.Queries.GetMyReservations;

public class GetMyReservationsQuery : IRequest<IReadOnlyList<MyReservationDto>>
{
    public GetMyReservationsQuery(int trainerUserId) => TrainerUserId = trainerUserId;

    public int TrainerUserId { get; }
}
