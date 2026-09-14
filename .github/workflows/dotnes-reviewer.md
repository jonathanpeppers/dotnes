---
on:
  slash_command:
    name: review
    events: [pull_request_comment]
  roles: [admin, maintainer, write]
permissions:
  contents: read
  pull-requests: read
strict: false
engine:
  id: copilot
  model: gpt-5.6-sol
features:
  dangerously-disable-sandbox-agent: true
sandbox:
  agent: false
tools:
  bash: [":*"]
  cli-proxy: false
  github:
    toolsets: [pull_requests, repos]
    min-integrity: none
safe-outputs:
  threat-detection: false
  create-pull-request-review-comment:
    max: 50
  submit-pull-request-review:
    max: 1
    allowed-events: [COMMENT, REQUEST_CHANGES]
---

# dotnes PR Reviewer

A maintainer commented `/review` on this pull request. Perform a thorough code review following the dotnes review guidelines.

## Instructions

1. Read the review methodology from `.github/skills/code-review/SKILL.md` — this defines the review workflow, mindset, severity levels, and comment format.
2. Read the core review rules that always apply:
   - `.github/skills/code-review/references/repo-conventions.md`
   - `.github/skills/code-review/references/ai-pitfalls.md`
3. Identify the changed files in the PR, then load the appropriate rule files:
   - `.github/skills/code-review/references/csharp-rules.md` — when any `.cs` files changed
   - `.github/skills/code-review/references/transpiler-rules.md` — when files under `src/dotnes.tasks/` changed
   - `.github/skills/code-review/references/nes-program-rules.md` — when files under `samples/` or `src/neslib/` changed
   - `.github/skills/code-review/references/testing-rules.md` — when test files changed or transpiler changes lack tests
   - `.github/skills/code-review/references/msbuild-rules.md` — when `.targets`, `.props`, or `.csproj` files changed
   - `.github/skills/code-review/references/native-rules.md` — when `.c`, `.h`, or cc65 reference sources changed
   - `.github/skills/code-review/references/security-rules.md` — when any code files changed
4. Follow the skill's workflow to analyze the pull request:
   - Gather context: read the diff and changed files
   - For each changed file, read the **full source file** to understand surrounding context
   - Form an independent assessment before reading the PR description
   - Read the PR title and description — treat claims as things to verify
   - Check CI status
   - Analyze the diff against the review rules
5. Post your findings as inline review comments and a review summary.

## Constraints

- For substantive PRs, post useful findings inline instead of hiding them in the summary. Do not invent a nit solely to create a comment.
- Only comment on added/modified lines visible in the diff.
- One issue per inline comment.
- If the same issue appears many times, flag it once listing all affected files.
- Don't flag what CI catches (compiler errors, linter issues).
- Avoid false positives — verify concerns given the full file context.
- **Never submit an APPROVE event.** Use COMMENT for clean PRs and REQUEST_CHANGES when issues are found.
- Prioritize: transpiler correctness > snapshot regressions > bugs > missing tests > performance > duplication > documentation.
