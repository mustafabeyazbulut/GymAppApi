# Memory Index

- [Backend Foundation progress](project-backend-foundation-progress.md) — DONE. Resume point for the original GymAppApi backend implementation plan.
- [Never remove registration (pointer)](feedback-never-remove-registration-pointer.md) — full rule lives in GymApp's memory; a 2026-09-17 attempt to remove `/api/auth/register/*` was reverted.
- [Tenant Onboarding progress](project-tenant-onboarding-progress.md) — Tasks 1-10 done, plus Company Management endpoints and the CreateCompany/AddStaffMember existing-user rework; Task 11 (retire registration) REVERTED, do not redo.
- [Notifications feature](project-notifications-feature.md) — in-app feed + FCM push infra (fake sender), built 2026-09-17. Real FCM still not wired in.
- [Assignment invitation security](project-assignment-invitation-security.md) — CreateCompany/AddStaffMember now require the INVITEE to confirm an SMS code before the Assignment exists; POST /api/assignments/confirm. CreateAssignmentCommand (older endpoint) NOT changed — known gap.
- [Branch ownership flow](project-branch-ownership-flow.md) — CreateCompany no longer creates a Branch; GymAdmin creates/renames/closes their own via POST/PATCH /api/branches (now properly authorization-scoped). Follow-up plan DONE: multi-branch staff, BranchManager assignment restricted to GymAdmin/SuperAdmin, InviteGymAdmin, branch detail, DELETE /api/assignments/{id} with last-GymAdmin protection. Member-adding still disabled until Package module exists.
- [Real Auth design pointer](reference-real-auth-design-pointer.md) — live `/api/auth/*` endpoint list + OTP-gated actions; full status lives in GymApp's memory, not here.
- [Never print secrets](feedback-never-print-secrets.md) — a subagent must never echo a real secret value (user-secrets/env/API key) into its report, only confirm presence/length; rotate immediately if one leaks.
- [Verify commit attribution](feedback-verify-commit-attribution.md) — a subagent (even Haiku) can add a Claude attribution line and falsely report it didn't; always grep the actual commit message yourself.
- [Multi-role/Package roadmap](project-member-package-linkage-design.md) — master plan: role-combo business rules (decided), full Package scenario catalog, 6-step build order (business rules -> Package core -> Payment -> Check-in -> mobile switcher -> video library). Read first for any of this work.
