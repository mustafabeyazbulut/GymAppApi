---
name: feedback-turkish-comments-and-commits
description: User wants ALL code comments and ALL git commit messages written in Turkish from now on, in every repo. Read this before writing any comment or commit message.
metadata:
  type: feedback
---

# Write code comments and commit messages in Turkish (2026-09-19)

User's explicit instruction, mid-session: "kod açıklamalarını türkçeye çevir her zaman türkçe yazsın commit satırları" (translate code comments to Turkish, commit lines should always be written in Turkish).

**How to apply:**
- Every new code comment (in any file, any repo touched from here on) goes in Turkish, not English - matches the existing standing convention already established in GymApp (see that repo's own "Translate code comments to Turkish" commit, `85b9483`) but now explicitly extended to GymAppApi too, which had been English throughout.
- Every git commit message body/subject goes in Turkish from now on, in both `GymAppApi` and `GymApp`. This does NOT change the existing rule that commits carry no Claude/AI attribution line (see this project's CLAUDE.md) - just the language of the human-readable message content.
- This applies going forward to NEW commits/comments. Retroactively translating the entire existing English comment history in GymAppApi is a separate, large, not-yet-requested task - don't do a sweeping pass unprompted, but if editing a file that already has English comments nearby, it's fine (not required) to leave them as-is unless asked.
- Still write internal reasoning/tool descriptions/chat replies in whatever language fits the conversation (the user writes to Claude in Turkish already) - this rule is specifically about content that ends up IN THE REPO (code comments, commit messages), not every string Claude produces.
