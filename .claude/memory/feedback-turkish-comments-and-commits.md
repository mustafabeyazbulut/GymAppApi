---
name: feedback-turkish-comments-and-commits
description: User wants ALL code comments, ALL git commit messages, AND all chat replies to the user written in Turkish only, in every repo. Read this before writing any comment, commit message, or chat response.
metadata:
  type: feedback
---

# Write code comments, commit messages, AND chat replies in Turkish only (2026-09-19, reinforced same day)

User's original instruction, mid-session: "kod açıklamalarını türkçeye çevir her zaman türkçe yazsın commit satırları" (translate code comments to Turkish, commit lines should always be written in Turkish).

**Reinforced later the same day, sharply, after Claude kept replying to the user in English in chat:** "bir daha uyarmayacağım seni bana sadece türkçe açıklama yaz. kod stırına da sadece türkçe açıklama yaz" (I won't warn you again - write explanations to me only in Turkish. Also write only Turkish comments in code lines). This is a correction to an earlier, WRONG assumption recorded in this same memory file - that chat replies could stay in whatever language fit the conversation. That assumption was false and caused a real, repeated annoyance. Corrected below.

**How to apply, current/final version:**
- Every new code comment (in any file, any repo touched from here on) goes in Turkish, not English - matches the existing standing convention already established in GymApp (see that repo's own "Translate code comments to Turkish" commit, `85b9483`) but now explicitly extended to GymAppApi too, which had been English throughout.
- Every git commit message body/subject goes in Turkish from now on, in both `GymAppApi` and `GymApp`. This does NOT change the existing rule that commits carry no Claude/AI attribution line (see this project's CLAUDE.md) - just the language of the human-readable message content.
- **Every chat reply to this user, in every repo/session, goes in Turkish only - no exceptions, no mixing in English.** This applies regardless of what language internal tool descriptions or other system text uses.
- This applies going forward to NEW commits/comments. Retroactively translating the entire existing English comment history in GymAppApi is a separate, large, not-yet-requested task - don't do a sweeping pass unprompted, but if editing a file that already has English comments nearby, it's fine (not required) to leave them as-is unless asked.
