using System.Globalization;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Localization;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.ForgotPassword;

public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, ForgotPasswordCommandResult>
{
    // CurrentUICulture: Handle() bu handler'ı çağıran isteğin pipeline'ı
    // İÇİNDE çalışıyor (RequestLocalizationMiddleware'in nested'ı), bu
    // yüzden ExceptionMiddleware'in aksine ambient kültürü doğrudan
    // okuyabiliyor - bkz. ExceptionMiddleware'in kendi yorumundaki
    // ExecutionContext açıklaması.
    private static string GenericMessage =>
        AppMessages.Resolve("PasswordResetCodeSentIfAccountExists", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISmsSender _smsSender;
    private readonly IEmailSender _emailSender;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;

    public ForgotPasswordCommandHandler(
        IUnitOfWork unitOfWork, ISmsSender smsSender, IEmailSender emailSender,
        IPhoneNumberNormalizer phoneNumberNormalizer)
    {
        _unitOfWork = unitOfWork;
        _smsSender = smsSender;
        _emailSender = emailSender;
        _phoneNumberNormalizer = phoneNumberNormalizer;
    }

    public async Task<ForgotPasswordCommandResult> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var identifier = _phoneNumberNormalizer.NormalizeIfPhone(request.Identifier);
        var user = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == identifier || u.Email == identifier, cancellationToken: cancellationToken);

        // Never reveal whether the identifier matched an account.
        if (user is null)
        {
            return new ForgotPasswordCommandResult { Message = GenericMessage };
        }

        var otpWriteRepo = _unitOfWork.GetWriteRepository<OtpVerification>();

        // Invalidate any still-live reset codes for this user first, so at
        // most one PasswordReset OtpVerification row is ever valid at once.
        // Without this, requesting a new code twice (e.g. "didn't get the
        // SMS, send again") leaves multiple valid rows and ResetPassword's
        // single-row lookup (no ORDER BY) could match the wrong one,
        // rejecting a genuinely correct code and burning an attempt against
        // a row the user can never satisfy.
        var priorLiveOtps = await _unitOfWork.GetReadRepository<OtpVerification>().GetAllAsync(
            o => o.UserId == user.Id && o.Purpose == OtpPurpose.PasswordReset && !o.IsUsed && o.ExpiresAt > DateTime.UtcNow,
            cancellationToken: cancellationToken);
        foreach (var prior in priorLiveOtps)
        {
            prior.IsUsed = true;
            otpWriteRepo.Update(prior);
        }

        var code = Random.Shared.Next(100000, 999999).ToString();

        await otpWriteRepo.AddAsync(new OtpVerification
        {
            UserId = user.Id,
            Code = code,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            Purpose = OtpPurpose.PasswordReset,
            IsUsed = false,
            AttemptCount = 0,
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Kod, kullanıcının GİRDİĞİ tanımlayıcının kanalına gider: e-posta
        // girildiyse e-postaya, telefon girildiyse SMS'e. (Eskiden hesapta
        // e-posta varsa telefonla istense bile e-postaya gidiyordu - telefonla
        // isteyen kişi e-postasına erişemiyor olabilir.) Metin alıcının kendi
        // dilinde (PreferredLanguage), isteği yapan cihazın dilinde değil.
        var language = user.PreferredLanguage;
        var message = AppMessages.Resolve("PasswordResetCodeMessage", language, code);
        var identifierIsEmail = request.Identifier.Contains('@');
        if (identifierIsEmail && !string.IsNullOrWhiteSpace(user.Email))
        {
            await _emailSender.SendAsync(user.Email, AppMessages.Resolve("PasswordResetEmailSubject", language), message, cancellationToken);
        }
        else
        {
            await _smsSender.SendAsync(user.Phone, message, cancellationToken);
        }

        return new ForgotPasswordCommandResult { Message = GenericMessage };
    }
}
