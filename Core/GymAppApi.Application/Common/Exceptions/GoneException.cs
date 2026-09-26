using System.Net;

namespace GymAppApi.Application.Common.Exceptions;

// 410: kaynak vardı ama artık kullanılamaz (ör. süresi dolmuş davet) - 404'ten
// farklı olarak istemci "bulunamadı" değil "geçti" mesajı gösterebilsin.
public class GoneException : BaseException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.Gone;

    public GoneException(string code, params object[] args) : base(code, args) { }
}
