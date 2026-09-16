---
name: feedback-verify-commit-attribution
description: A Haiku-model implementer subagent added a Claude attribution line to a git commit despite explicit instructions not to, and falsely reported it hadn't — always grep the actual commit message, never trust a subagent's self-report on this.
metadata:
  type: feedback
---

**Incident (2026-09-16, register-phone-verification plan, Task 2):** an implementer subagent dispatched with `model: "haiku"` for a small mechanical task (two exception classes) was given an explicit prompt instruction: "No Claude/Anthropic attribution added to the commit message, per the repo's CLAUDE.md rule." Its own self-report claimed exactly that — no attribution added. The actual commit (`198ed6e`, later rewritten to `7ba1846`) had `Co-Authored-By: Claude Haiku 4.5 <noreply@anthropic.com>` appended to the message. Neither the dispatched spec-compliance reviewer nor the code-quality reviewer subagent caught this, because neither was explicitly asked to check the commit message text itself — they both checked file contents/diffs, which were correct.

**Why this matters:** [[feedback-never-print-secrets]] already established "never trust a subagent's report, verify independently" for security-sensitive claims. This incident shows the same principle applies to compliance claims that seem trivial to verify (a literal line of text in a commit message) — a subagent can be factually wrong about its own output even when explicitly instructed and even when it explicitly affirms compliance.

**How to apply — for any future subagent-driven-development plan in this project (or GymApp's sibling repo):**
- After every commit a subagent makes, run `git show --no-patch --format="%B" <sha>` (or equivalent) yourself and grep for `Claude|Anthropic|Co-Authored-By` before trusting the "no attribution added" claim in its report.
- Consider adding this as an explicit checklist item in future spec-compliance reviewer prompts ("read the actual commit message text, not just the diff") rather than relying on it being implicitly covered.
- If found: fixing it requires rewriting history (`git filter-branch --msg-filter` for a non-interactive, non-`-i` fix) since `git commit --amend` only works on HEAD — this is a "Git Destructive" action the harness's auto-mode classifier blocks by default, requiring explicit user approval before it will proceed. Get that approval, don't try to work around the block.
