---
name: project-notifications-feature
description: GymAppApi in-app notification feed + FCM push infrastructure — what was built 2026-09-17, how it's wired, and what's still a stub
metadata:
  type: project
---

# Notifications (in-app feed + push infra) — built 2026-09-17

User asked to "set up notifications" after the Tenant Onboarding rework. The
original product design doc (`2026-09-09-gym-yonetim-sistemi-design.md`)
already specified Faz 1 push infra (FCM + `DeviceToken` registration) but it
had never been built — `DeviceToken` existed as an unused entity since the
backend-foundation plan. User chose **both** an in-app feed and push (not
just one) when asked.

## What was built

- **Domain:** `Notification` entity (`Core/GymAppApi.Domain/Entities/Notification.cs`)
  — user-owned like `DeviceToken`, deliberately NOT `ITenantScoped`/`ICompanyScoped`.
  Added to `GymAppApiDbContext.IntentionallyUnscopedEntityTypes`. Migration `20260917171854_AddNotifications`.
- **`IPushNotificationSender`** (Application interface) + **`LoggingPushNotificationSender`**
  (Infrastructure fake, logs `[FAKE PUSH]`) — same placeholder pattern as `ISmsSender`/`LoggingSmsSender`.
  **Real FCM was never wired in** — swapping it in only requires replacing this one file + its DI registration
  (`Infrastructure/GymAppApi.Infrastructure/Registration.cs`).
- **`NotificationDispatcher`** (`Core/GymAppApi.Application/Common/Notifications/NotificationDispatcher.cs`)
  — static helper (same style as `PendingVerificationCodeService`), takes `IUnitOfWork` + `IPushNotificationSender`
  as params rather than being its own DI-registered service. `NotifyUserAsync(uow, pushSender, userId, title, body, ct)`
  writes a `Notification` row AND pushes to every one of that user's registered `DeviceToken`s.
- **Trigger points (only two so far):** `CreateCompanyCommandHandler` (notifies the new Gym Admin) and
  `AddStaffMemberCommandHandler` (notifies the newly attached Member/Trainer) — called right after the existing SMS
  send, same spot, in addition to it (SMS was NOT removed).
- **`POST /api/device-tokens`** (`DeviceTokensController`, `[Authorize]`) — upserts by `Token` (reassigns `UserId`
  if the same physical device token re-registers under a different account; `DeviceTokenConfiguration` has a unique
  index on `Token` so this can't insert a duplicate).
- **`GET /api/notifications`** and **`PATCH /api/notifications/{id}/read`** (`NotificationsController`, `[Authorize]`)
  — caller's own notifications only, scoped by JWT `sub`, newest-first, no pagination (matches this codebase's
  existing simplicity level — `GetCompaniesQuery` etc. don't paginate either).

## Deliberately not built (kept in scope, ask before adding)

- No "unread count" endpoint — client can derive it from the full list since there's no pagination yet.
- No `DELETE /api/device-tokens/{token}` (unregister on logout) — low priority while push is still a fake logger.
- `docs/mobile-api/README.md` was NOT updated — it was already stale before this change (missing Companies/Assignments/Auth
  entirely, still says "auth not yet added"). Catching it up is a separate, larger task, not done here.
