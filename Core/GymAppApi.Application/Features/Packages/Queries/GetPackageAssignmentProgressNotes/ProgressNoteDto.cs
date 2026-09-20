namespace GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentProgressNotes;

public class ProgressNoteDto
{
    public int Id { get; set; }
    public int TechniqueScore { get; set; }
    public int ConditionScore { get; set; }
    public string? NoteText { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? MediaFileId { get; set; }
    public string? MediaContentType { get; set; }
}
