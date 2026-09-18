using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Packages.Exceptions;

public class PaymentExceedsRemainingBalanceException : ConflictException
{
    public PaymentExceedsRemainingBalanceException() : base("Bu ödeme, paketin kalan bakiyesinden fazla.") { }
}
