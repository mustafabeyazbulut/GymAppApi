using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Auth.Exceptions;

public class InvalidContactVerificationCodeException : UnauthorizedException
{
    public InvalidContactVerificationCodeException(bool phoneFailed, bool emailFailed)
        : base(SelectCode(phoneFailed, emailFailed))
    {
    }

    private static string SelectCode(bool phoneFailed, bool emailFailed)
    {
        if (phoneFailed && emailFailed)
        {
            return "PhoneAndEmailCodeInvalid";
        }

        return phoneFailed ? "PhoneCodeInvalid" : "EmailCodeInvalid";
    }
}
