---
name: feedback-never-remove-registration-pointer
description: Pointer only — self-service registration must never be removed from this backend either; the full incident/rule lives in GymApp's memory since it's a cross-repo product-model correction, not a mobile-only fact.
metadata:
  type: feedback
---

**Never remove or gate `/api/auth/register/*`** (`RegisterRequestOtpCommand`/`RegisterCompleteCommand`), even if a plan document (including this repo's own `docs/superpowers/plans/2026-09-17-tenant-onboarding.md` Task 11) says to retire it. It is a permanent feature. On 2026-09-17 this repo's Task 11 did exactly that (commit `345c6f5`) and had to be reverted (commit `967fc43`) after the user corrected the actual product model — companies assign *already-registered* users to roles, they do not create brand-new accounts by phone.

Full incident writeup and the corrected product model live in `C:\Users\MBEYAZBULUT\Documents\GitHub\GymApp\.claude\memory\feedback-never-remove-registration.md` — **read that file first**, not just this pointer.

**Follow-up done (2026-09-17, commit `d4ce7e9`, confirmed with the user first):** `CreateCompanyCommand`/`AddStaffMemberCommand` reworked to look up an existing registered user by phone (404 if not found) instead of creating a new `User`. See [[project-tenant-onboarding-progress]] for the diff details.

**How to apply:** if any plan or memory in this repo suggests removing register endpoints, or making CreateCompany/AddStaffMember create new users again instead of attaching existing ones, stop and confirm with the user first.
