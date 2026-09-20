namespace GymAppApi.Application.Features.ContentLibrary.Commands.CreateContentItem;

public class CreateContentItemCommandResult
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public int MediaFileId { get; set; }
}
