using MediatR;

namespace GymAppApi.Application.Features.Companies.Commands.CreateCompany;

// Deliberately does NOT implement ITransactionalRequest - the handler needs
// multiple sequential SaveChangesAsync calls mid-transaction (to obtain the
// generated Company.Id and, on the new-admin path, User.Id before the
// dependent Branch/Assignment rows can be inserted), which is naturally
// handled by the handler's own manual transaction rather than the pipeline's
// TransactionBehavior.
public class CreateCompanyCommand : IRequest<CreateCompanyCommandResult>
{
    public string CompanyName { get; set; } = null!;
    public string BranchName { get; set; } = null!;
    public string BranchAddress { get; set; } = null!;

    // Looks up an already-registered user by phone and makes them this
    // company's GymAdmin — this never creates a new User. See
    // .claude/memory/feedback-never-remove-registration-pointer.md for why.
    public string GymAdminPhone { get; set; } = null!;
}
