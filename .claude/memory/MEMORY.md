# Memory Index

- [Backend Foundation progress](project-backend-foundation-progress.md) — DONE. Resume point for the original GymAppApi backend implementation plan.
- [Never remove registration (pointer)](feedback-never-remove-registration-pointer.md) — full rule lives in GymApp's memory; a 2026-09-17 attempt to remove `/api/auth/register/*` was reverted.
- [Tenant Onboarding progress](project-tenant-onboarding-progress.md) — Tasks 1-10 done, plus Company Management endpoints and the CreateCompany/AddStaffMember existing-user rework; Task 11 (retire registration) REVERTED, do not redo.
- [Notifications feature](project-notifications-feature.md) — in-app feed + FCM push infra (fake sender), built 2026-09-17. Real FCM still not wired in.
- [Real Auth design pointer](reference-real-auth-design-pointer.md) — live `/api/auth/*` endpoint list + OTP-gated actions; full status lives in GymApp's memory, not here.
- [Never print secrets](feedback-never-print-secrets.md) — a subagent must never echo a real secret value (user-secrets/env/API key) into its report, only confirm presence/length; rotate immediately if one leaks.
- [Verify commit attribution](feedback-verify-commit-attribution.md) — a subagent (even Haiku) can add a Claude attribution line and falsely report it didn't; always grep the actual commit message yourself.
