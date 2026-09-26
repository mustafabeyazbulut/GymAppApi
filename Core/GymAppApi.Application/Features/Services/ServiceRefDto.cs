using GymAppApi.Domain.Entities;

namespace GymAppApi.Application.Features.Services;

// Paket/ders DTO'larında kullanılan kısa hizmet referansı: { id, name }.
public class ServiceRefDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public static List<ServiceRefDto> ListFrom(IEnumerable<Service>? services) =>
        (services ?? Enumerable.Empty<Service>())
            .OrderBy(s => s.Name)
            .Select(s => new ServiceRefDto { Id = s.Id, Name = s.Name })
            .ToList();
}
