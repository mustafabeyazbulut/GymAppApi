# Memory Index

- [Backend Foundation progress](project-backend-foundation-progress.md) — resume point for GymAppApi backend implementation; read first if starting fresh.
- [Real Auth design pointer](reference-real-auth-design-pointer.md) — real Auth feature is being designed; full status lives in GymApp's memory, not here.
- [Never print secrets](feedback-never-print-secrets.md) — a subagent must never echo a real secret value (user-secrets/env/API key) into its report, only confirm presence/length; rotate immediately if one leaks.
- [Verify commit attribution](feedback-verify-commit-attribution.md) — a subagent (even Haiku) can add a Claude attribution line and falsely report it didn't; always grep the actual commit message yourself.
