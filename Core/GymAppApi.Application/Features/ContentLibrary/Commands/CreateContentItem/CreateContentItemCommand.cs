using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.ContentLibrary.Commands.CreateContentItem;

public class CreateContentItemCommand : IRequest<CreateContentItemCommandResult>
{
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public PackageAccessTier RequiredAccessTier { get; set; }
    public int? BranchId { get; set; }

    // Controller, IFormFile'dan bu üç alanı doldurur - Application katmanı
    // kasıtlı olarak ASP.NET'e özel IFormFile tipini hiç görmez (testability
    // ve katman ayrımı için).
    public Stream FileContent { get; set; } = null!;
    public string FileContentType { get; set; } = null!;

    // Çağıranın kendi JWT sub claim'inden controller tarafından set edilir.
    public int RequestedByUserId { get; set; }
}
