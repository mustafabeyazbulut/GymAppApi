using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Features.Invitations.Common;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Invitations.Commands.AcceptInvitation;

public class AcceptInvitationCommandHandler : IRequestHandler<AcceptInvitationCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public AcceptInvitationCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(AcceptInvitationCommand request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // Kodsuz kabul, SMS koduyla kanıtlanan "telefonun sahibi benim"
        // güvencesinin yerine hesabın doğrulanmış telefonuna dayanıyor.
        var user = await _unitOfWork.GetReadRepository<User>().GetAsync(u => u.Id == request.UserId, cancellationToken: cancellationToken);
        if (user is null || !user.PhoneVerified)
        {
            throw new ForbiddenException("PhoneNotVerified");
        }

        // İş kuralları (duplicate, rol çakışması, geçerli paket engeli) SMS
        // kodlu confirm uçlarıyla ortak servislerde.
        if (InvitationLookup.IsPackage(request.Type))
        {
            var invitation = await InvitationLookup.FindPackageInvitationAsync(_unitOfWork, request.InvitationId, request.UserId, now, cancellationToken);
            await PackageInvitationAcceptance.AcceptAsync(_unitOfWork, invitation, now, cancellationToken);
            return;
        }

        if (InvitationLookup.IsAssignment(request.Type))
        {
            var invitation = await InvitationLookup.FindAssignmentInvitationAsync(_unitOfWork, request.Type, request.InvitationId, request.UserId, now, cancellationToken);
            await AssignmentInvitationAcceptance.AcceptAsync(_unitOfWork, invitation, cancellationToken);
            return;
        }

        throw new NotFoundException("InvitationNotFound", request.InvitationId);
    }
}
