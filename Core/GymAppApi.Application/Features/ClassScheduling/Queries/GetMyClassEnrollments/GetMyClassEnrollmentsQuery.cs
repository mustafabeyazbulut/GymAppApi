using MediatR;

namespace GymAppApi.Application.Features.ClassScheduling.Queries.GetMyClassEnrollments;

public class GetMyClassEnrollmentsQuery : IRequest<IReadOnlyList<MyClassEnrollmentDto>>
{
    public GetMyClassEnrollmentsQuery(int memberUserId) => MemberUserId = memberUserId;

    public int MemberUserId { get; }
}
