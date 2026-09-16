using GymAppApi.Application.Common.ContactVerification;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.UnfreezeAccountRequestOtp;

public class UnfreezeAccountRequestOtpCommandHandler : IRequestHandler<UnfreezeAccountRequestOtpCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISmsSender _smsSender;

    public UnfreezeAccountRequestOtpCommandHandler(IUnitOfWork unitOfWork, ISmsSender smsSender)
    {
        _unitOfWork = unitOfWork;
        _smsSender = smsSender;
    }

    public async Task Handle(UnfreezeAccountRequestOtpCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Id == request.UserId, cancellationToken: cancellationToken);
        if (user is null)
        {
            throw new NotFoundException($"Kullanıcı {request.UserId} bulunamadı.");
        }

        var code = await PendingVerificationCodeService.IssueAsync(_unitOfWork, ContactChannel.Phone, user.Phone, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _smsSender.SendAsync(user.Phone, $"GymApp hesap aktifleştirme doğrulama kodunuz: {code}. Kod 10 dakika geçerlidir.", cancellationToken);
    }
}
