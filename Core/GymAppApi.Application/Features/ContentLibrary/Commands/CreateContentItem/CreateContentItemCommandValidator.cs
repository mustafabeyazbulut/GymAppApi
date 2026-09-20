using FluentValidation;

namespace GymAppApi.Application.Features.ContentLibrary.Commands.CreateContentItem;

public class CreateContentItemCommandValidator : AbstractValidator<CreateContentItemCommand>
{
    public CreateContentItemCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.FileContentType).NotEmpty();
        RuleFor(x => x.FileContent).NotNull();
    }
}
