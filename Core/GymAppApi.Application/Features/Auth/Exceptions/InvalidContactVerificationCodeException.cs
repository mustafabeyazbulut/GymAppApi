using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Auth.Exceptions;

public class InvalidContactVerificationCodeException : UnauthorizedException
{
    public InvalidContactVerificationCodeException(bool phoneFailed, bool emailFailed)
        : base(BuildMessage(phoneFailed, emailFailed))
    {
    }

    private static string BuildMessage(bool phoneFailed, bool emailFailed)
    {
        if (phoneFailed && emailFailed)
        {
            return "Telefon ve e-posta kodu hatalı, süresi dolmuş veya çok fazla deneme yapıldı.";
        }

        return phoneFailed
            ? "Telefon kodu hatalı, süresi dolmuş veya çok fazla deneme yapıldı."
            : "E-posta kodu hatalı, süresi dolmuş veya çok fazla deneme yapıldı.";
    }
}
