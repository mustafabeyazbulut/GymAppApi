namespace GymAppApi.Application.Features.Media.Queries.GetMediaFile;

public class GetMediaFileResult
{
    public Stream Content { get; set; } = null!;
    public string ContentType { get; set; } = null!;
}
