using System.Net;

namespace GymAppApi.Application.Common.Exceptions;

// Gövde doğrulaması (FluentValidation) projede 422 döner; bu istisna sorgu
// parametresi gibi isteğin kendisi geçersiz olduğunda 400 döndürmek için
// (ör. izin verilen aralığı aşan tarih aralığı, desteklenmeyen dönem).
public class BadRequestException : BaseException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.BadRequest;

    public BadRequestException(string code, params object[] args) : base(code, args) { }
}
