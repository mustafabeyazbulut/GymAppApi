using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Reservations.Exceptions;

// Paket PackageAssignmentValidity'ye göre kullanılamaz: süresi dolmuş,
// dondurulmuş, iptal edilmiş ya da henüz onaylanmamış.
public class PackageAssignmentNotUsableException : ConflictException
{
    public PackageAssignmentNotUsableException() : base("PackageAssignmentNotUsable") { }
}
