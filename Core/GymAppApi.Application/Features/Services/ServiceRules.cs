using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Services;

// Hizmet yazma kuralları tek yerde: aktif bağlama göre yetki (GymAdmin firma
// genelinde, BranchManager sadece kendi şubesinde) ve şube içinde tekil ad.
public static class ServiceRules
{
    public static bool CanManage(ITenantContext tenantContext, int companyId, int branchId) => tenantContext.Role switch
    {
        AssignmentRole.GymAdmin => tenantContext.CompanyId == companyId,
        AssignmentRole.BranchManager => tenantContext.CompanyId == companyId && tenantContext.BranchId == branchId,
        _ => false,
    };

    // Filtresiz okuma (pasif hizmet de yönetilebilsin) + açık kapsam: başka
    // firmanın / (şube kapsamlı personel için) başka şubenin hizmeti "yok".
    public static async Task<Service> LoadManageableAsync(IUnitOfWork unitOfWork, ITenantContext tenantContext, int serviceId, CancellationToken cancellationToken)
    {
        var service = await unitOfWork.GetReadRepository<Service>().GetAsync(
            s => s.Id == serviceId,
            include: q => q.IgnoreQueryFilters().Include(s => s.Branch),
            enableTracking: true,
            cancellationToken: cancellationToken);
        if (service is null || tenantContext.CompanyId is null || service.CompanyId != tenantContext.CompanyId ||
            (tenantContext.BranchId != null && service.BranchId != tenantContext.BranchId))
        {
            throw new NotFoundException("ServiceNotFound", serviceId);
        }

        if (!CanManage(tenantContext, service.CompanyId, service.BranchId))
        {
            throw new ForbiddenException("ForbiddenManageService");
        }

        return service;
    }

    // Büyük/küçük harf ve baş/son boşluk duyarsız; pasif hizmetler de sayılır
    // (DB'deki tekil indeks onları da kapsar).
    public static async Task EnsureNameAvailableAsync(IUnitOfWork unitOfWork, int branchId, string name, int? exceptServiceId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();
        var clashes = await unitOfWork.GetReadRepository<Service>().GetAllAsync(
            s => s.BranchId == branchId && s.Name.ToLower() == lowered && (exceptServiceId == null || s.Id != exceptServiceId),
            include: q => q.IgnoreQueryFilters().Include(s => s.Branch),
            cancellationToken: cancellationToken);
        if (clashes.Count > 0)
        {
            throw new ConflictException("ServiceNameTaken");
        }
    }
}
