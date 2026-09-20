using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.RecordProgressNote;

public class RecordProgressNoteCommand : IRequest<RecordProgressNoteCommandResult>
{
    // Route segmentinden controller tarafından set edilir.
    public int PackageAssignmentId { get; set; }
    public int TechniqueScore { get; set; }
    public int ConditionScore { get; set; }
    public string? NoteText { get; set; }

    // Opsiyonel "önce/sonra" fotoğraf/video eki - controller, IFormFile'dan bu
    // iki alanı doldurur (bkz. CreateContentItemCommand'in aynı deseni).
    public Stream? MediaFileContent { get; set; }
    public string? MediaFileContentType { get; set; }

    // Çağıranın kendi JWT sub claim'inden controller tarafından set edilir.
    public int RequestedByUserId { get; set; }
}
