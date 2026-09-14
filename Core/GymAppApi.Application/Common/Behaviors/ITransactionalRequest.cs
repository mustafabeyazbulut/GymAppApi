namespace GymAppApi.Application.Common.Behaviors;

// Marker interface: implement on a Command whose Handler must run inside a
// single DB transaction (e.g. "read current total, assert under a limit,
// write" sequences that would otherwise race under concurrent requests).
public interface ITransactionalRequest
{
}
