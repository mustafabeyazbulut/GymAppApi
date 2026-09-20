using MediatR;

namespace GymAppApi.Application.Features.Media.Queries.GetMediaFile;

public class GetMediaFileQuery : IRequest<GetMediaFileResult>
{
    // Route segmentinden controller tarafından set edilir.
    public int MediaFileId { get; set; }

    // Çağıranın kendi JWT sub claim'inden controller tarafından set edilir.
    public int RequestedByUserId { get; set; }
}
