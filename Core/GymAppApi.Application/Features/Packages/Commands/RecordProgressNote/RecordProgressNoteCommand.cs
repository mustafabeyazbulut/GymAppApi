using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.RecordProgressNote;

public class RecordProgressNoteCommand : IRequest<RecordProgressNoteCommandResult>
{
    // Route segmentinden controller tarafından set edilir.
    public int PackageAssignmentId { get; set; }
    public int TechniqueScore { get; set; }
    public int ConditionScore { get; set; }
    public string? NoteText { get; set; }

    // Çağıranın kendi JWT sub claim'inden controller tarafından set edilir.
    public int RequestedByUserId { get; set; }
}
