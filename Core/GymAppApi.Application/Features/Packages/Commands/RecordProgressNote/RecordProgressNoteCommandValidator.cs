using FluentValidation;

namespace GymAppApi.Application.Features.Packages.Commands.RecordProgressNote;

public class RecordProgressNoteCommandValidator : AbstractValidator<RecordProgressNoteCommand>
{
    public RecordProgressNoteCommandValidator()
    {
        RuleFor(x => x.TechniqueScore).InclusiveBetween(0, 100);
        RuleFor(x => x.ConditionScore).InclusiveBetween(0, 100);
        RuleFor(x => x.NoteText).MaximumLength(1000);
    }
}
