using MediatR;

namespace GymAppApi.Application.Features.ContentLibrary.Queries.GetContentItems;

public class GetContentItemsQuery : IRequest<IReadOnlyList<ContentItemDto>>
{
    // Çağıranın kendi JWT sub claim'inden controller tarafından set edilir.
    public int RequestedByUserId { get; set; }
}
