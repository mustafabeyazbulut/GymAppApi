---
name: reference-real-auth-design-pointer
description: Pointer only — the real Auth feature (spans this backend + GymApp mobile) is being designed/tracked in GymApp's own memory, not duplicated here.
metadata:
  type: reference
---

A real (backend-integrated) Auth feature — register/login/JWT endpoints, an open-membership/tenant-less user model, and a minimal Assignment-creation endpoint — is being brainstormed as of 2026-09-15. Per this project's own cross-repo convention ([[project-backend-foundation-progress]]'s sibling rule in GymApp: mobile-facing facts live in GymApp's memory, backend-side facts only get duplicated here once they're actually implemented), the full design status lives in:

`C:\Users\MBEYAZBULUT\Documents\GitHub\GymApp\.claude\memory\project-real-auth-design.md`

Read that file for current status before assuming what's decided. Key facts relevant to THIS repo once implementation starts: no new domain entities needed (`User`/`Assignment`/`AssignmentRole.Member` already fit an open-membership model with zero changes — confirmed by reading `Core/GymAppApi.Domain/Entities/User.cs` and `Assignment.cs`); new work will be auth endpoints (register/login/refresh), a refresh-token store, and a minimal assign endpoint — not yet spec'd or planned as of this note.
