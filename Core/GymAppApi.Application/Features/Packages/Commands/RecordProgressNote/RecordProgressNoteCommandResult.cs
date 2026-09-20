namespace GymAppApi.Application.Features.Packages.Commands.RecordProgressNote;

public class RecordProgressNoteCommandResult
{
    public int Id { get; set; }
    public int TechniqueScore { get; set; }
    public int ConditionScore { get; set; }
    public string? NoteText { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? MediaFileId { get; set; }
}
