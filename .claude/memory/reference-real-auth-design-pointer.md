---
name: reference-real-auth-design-pointer
description: Pointer only — the real Auth feature (spans this backend + GymApp mobile) is tracked in GymApp's own memory, not duplicated here. Backend side is FULLY DONE (14/14); mobile side in progress.
metadata:
  type: reference
---

A real (backend-integrated) Auth feature — register/login/JWT endpoints, an open-membership/tenant-less user model, and a minimal Assignment-creation endpoint. Per this project's own cross-repo convention ([[project-backend-foundation-progress]]'s sibling rule in GymApp: mobile-facing facts live in GymApp's memory, backend-side facts only get duplicated here once they're actually implemented), the full design status and task-by-task progress live in:

`C:\Users\MBEYAZBULUT\Documents\GitHub\GymApp\.claude\memory\project-real-auth-design.md`

Read that file for current status before assuming what's decided or done.

**Status as of last update: THIS repo's (GymAppApi) backend plan is FULLY COMPLETE — 14/14 tasks implemented, reviewed, and verified end-to-end against a real running Postgres instance.** No further backend work is expected unless new issues surface during mobile integration. Live endpoints: `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/forgot-password`, `POST /api/auth/reset-password`, `GET/DELETE /api/auth/me`, `POST /api/assignments` (GymAdmin/SuperAdmin-gated, company-scoped). All `/api/auth/*` + `/api/assignments` share one rate limiter (10 req/min). Three critical bugs were found and fixed during the backend plan's own review (refresh-token concurrency shadow-property bug, cross-company privilege escalation on `/api/assignments`, JWT `sub` claim silently renamed by default inbound-claim-mapping) — full detail in the GymApp memory file above.

The GymApp mobile plan (15 tasks, consuming these endpoints) is being executed via `superpowers:subagent-driven-development` and is IN PROGRESS as of this update — check the GymApp memory file's own status line for the current task count, don't assume it's finished just because the backend is.
