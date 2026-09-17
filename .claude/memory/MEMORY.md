# Memory Index

- [Backend Foundation progress](project-backend-foundation-progress.md) — DONE. Resume point for the original GymAppApi backend implementation plan.
- [Tenant Onboarding progress](project-tenant-onboarding-progress.md) — CURRENT plan, 10/11 done; Task 11 blocked on GymApp mobile plan shipping first.
- [Real Auth design pointer](reference-real-auth-design-pointer.md) — live `/api/auth/*` endpoint list + OTP-gated actions; full status lives in GymApp's memory, not here.
- [Never print secrets](feedback-never-print-secrets.md) — a subagent must never echo a real secret value (user-secrets/env/API key) into its report, only confirm presence/length; rotate immediately if one leaks.
- [Verify commit attribution](feedback-verify-commit-attribution.md) — a subagent (even Haiku) can add a Claude attribution line and falsely report it didn't; always grep the actual commit message yourself.
