---
name: reference-real-auth-design-pointer
description: Pointer only — the real Auth feature and everything built on top of it (OTP register, OTP-gated freeze/reactivate/delete, language sync) is tracked in GymApp's own memory, not duplicated here.
metadata:
  type: reference
---

A real (backend-integrated) Auth feature — register/login/JWT endpoints, an open-membership/tenant-less user model, a minimal Assignment-creation endpoint, and (added after the original plan shipped) OTP-verified registration plus three OTP-gated self-service account actions. Per this project's own cross-repo convention ([[project-backend-foundation-progress]]'s sibling rule in GymApp: mobile-facing facts live in GymApp's memory, backend-side facts only get duplicated here once they're actually implemented), the full design status and current state live in:

- `C:\Users\MBEYAZBULUT\Documents\GitHub\GymApp\.claude\memory\project-otp-security-actions.md` — **read this first**, it's the current status.
- `C:\Users\MBEYAZBULUT\Documents\GitHub\GymApp\.claude\memory\project-real-auth-design.md` — the original plan's design rationale (historical; its "no SMS OTP" decision was later reversed, see the file above).

**Current live `/api/auth/*` endpoints (this repo, GymAppApi):**
- `POST register/request-otp`, `POST register/complete` — two-step OTP-verified registration (replaced the old single-step `POST register`).
- `POST login`, `POST refresh`, `POST forgot-password`, `POST reset-password`.
- `GET me`, `PATCH me/language`.
- `POST me/freeze/request-otp` → `POST me/freeze {code}`; `POST me/unfreeze/request-otp` → `POST me/unfreeze {code}` — self-service temporary account freeze/reactivate, does NOT block login.
- `POST me/delete/request-otp` → `DELETE me {code}` — account deletion.
- `POST /api/assignments` (GymAdmin/SuperAdmin-gated, company-scoped).

All of the above share one rate limiter (10 req/min) and the OTP-verified ones (`register/complete`, `me/freeze`, `me/unfreeze`, `DELETE me`) share one rate-limited/attempt-counted code service, `PendingVerificationCodeService` (`Core/GymAppApi.Application/Common/ContactVerification`). Three critical bugs were found and fixed during the original plan's own review (refresh-token concurrency shadow-property bug, cross-company privilege escalation on `/api/assignments`, JWT `sub` claim silently renamed by default inbound-claim-mapping) — full detail in the GymApp memory files above.
