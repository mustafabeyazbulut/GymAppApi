# Memory Index

- [Backend Foundation progress](project-backend-foundation-progress.md) — DONE. Resume point for the original GymAppApi backend implementation plan.
- [Never remove registration (pointer)](feedback-never-remove-registration-pointer.md) — full rule lives in GymApp's memory; a 2026-09-17 attempt to remove `/api/auth/register/*` was reverted.
- [Tenant Onboarding progress](project-tenant-onboarding-progress.md) — Tasks 1-10 done, plus an unplanned Company Management endpoint addition; Task 11 (retire registration) REVERTED, do not redo.
- [Real Auth design pointer](reference-real-auth-design-pointer.md) — live `/api/auth/*` endpoint list + OTP-gated actions; full status lives in GymApp's memory, not here.
- [Never print secrets](feedback-never-print-secrets.md) — a subagent must never echo a real secret value (user-secrets/env/API key) into its report, only confirm presence/length; rotate immediately if one leaks.
- [Verify commit attribution](feedback-verify-commit-attribution.md) — a subagent (even Haiku) can add a Claude attribution line and falsely report it didn't; always grep the actual commit message yourself.
