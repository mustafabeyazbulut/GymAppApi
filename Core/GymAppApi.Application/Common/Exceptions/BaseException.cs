using System.Net;
using GymAppApi.Application.Common.Localization;

namespace GymAppApi.Application.Common.Exceptions;

public abstract class BaseException : Exception
{
    public abstract HttpStatusCode StatusCode { get; }

    // AppMessages katalogundaki anahtar - gerçek, dile göre çevrilmiş metin
    // ExceptionMiddleware tarafından isteğin diline göre YENİDEN çözülür;
    // buradaki .Message sadece loglama/varsayılan (İngilizce) bir yedektir,
    // istemciye asla doğrudan bu haliyle gitmez.
    public string Code { get; }
    public object[] Args { get; }

    protected BaseException(string code, params object[] args) : base(AppMessages.Resolve(code, "en", args))
    {
        Code = code;
        Args = args;
    }
}
