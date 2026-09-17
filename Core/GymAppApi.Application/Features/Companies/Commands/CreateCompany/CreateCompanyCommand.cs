using MediatR;

namespace GymAppApi.Application.Features.Companies.Commands.CreateCompany;

// Deliberately does NOT implement ITransactionalRequest - the handler needs
// a mid-transaction SaveChangesAsync to obtain the generated Company.Id
// before the dependent invitation row can reference it, which is naturally
// handled by the handler's own manual transaction rather than the pipeline's
// TransactionBehavior.
// Deliberately does NOT create a Branch - that's the new GymAdmin's own call
// once they've confirmed the invitation (POST /api/branches), not something
// SuperAdmin decides on their behalf. See
// .claude/memory/project-branch-ownership-flow.md for the full reasoning.
public class CreateCompanyCommand : IRequest<CreateCompanyCommandResult>
{
    public string CompanyName { get; set; } = null!;

    // Looks up an already-registered user by phone and invites them to be
    // this company's GymAdmin — this never creates a new User. See
    // .claude/memory/feedback-never-remove-registration-pointer.md for why.
    public string GymAdminPhone { get; set; } = null!;

    // Set by the controller from the caller's own JWT sub claim - recorded
    // on the PendingAssignmentInvitation for auditability.
    public int RequestedByUserId { get; set; }
}
