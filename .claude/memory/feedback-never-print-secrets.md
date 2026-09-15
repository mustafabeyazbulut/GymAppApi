---
name: feedback-never-print-secrets
description: A dispatched review subagent ran `dotnet user-secrets list` and printed the real JWT signing key value into its report — never let a subagent (implementer or reviewer) print a secret's actual value, only confirm presence/length.
metadata:
  type: feedback
---

**Incident (2026-09-15):** while verifying the Real Auth plan's Task 6 security fixes, a review subagent was asked to "confirm `Jwt:SigningKey` currently has a value set" via `dotnet user-secrets list`. It ran the command and pasted the raw output — including the actual base64 signing key — into its final report, which surfaces directly in the parent session's (and the user's) conversation transcript. The harness itself flagged this as a "Credential Materialization" security warning. The key was rotated immediately afterward (never reused, never echoed again).

**Why this matters:** `dotnet user-secrets` (and equivalents — `.env` files, secret managers, cloud CLI `describe`/`get` commands) are local/scoped-access stores specifically so a secret's value stays out of logs, chat history, and version control. Piping the raw output of a "list secrets" command into a subagent's report defeats that boundary — the value ends up embedded in conversation history (and potentially session logs) even though it was never committed to git.

**How to apply — when writing ANY subagent prompt (implementer or reviewer) that needs to check whether a secret/credential is present:**
- Never ask a subagent to run a bare `list`/`get`/`describe` command against a secret store and report the output verbatim.
- Instead, ask it to confirm presence/shape only: pipe through a redaction step (e.g. `dotnet user-secrets list --project X | sed 's/=.*/= [redacted]/'` on POSIX, or the PowerShell equivalent replacing the value before display), or ask for a boolean/length check ("does `Jwt:SigningKey` exist and is it ≥32 bytes — answer yes/no and the byte count, never the value itself").
- If a subagent's report already contains a secret value despite this, the parent session must NOT repeat, quote, or reference the value again in its own output, and should treat the credential as compromised — rotate it immediately (generate a fresh value, replace it in the secret store) rather than continuing to trust the exposed one.
- This applies equally to: JWT signing keys, DB connection strings/passwords, API keys, OAuth client secrets, SMS/email provider credentials (relevant to this Real Auth plan's `ISmsSender`/`IEmailSender` once a real provider is chosen) — any value a `dotnet user-secrets`/`appsettings`/env-var lookup could return.
