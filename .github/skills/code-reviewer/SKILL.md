---
name: code-reviewer
description: >-
  Review dotnes PRs against established rules. Trigger on "review this PR",
  a GitHub PR URL, or code review requests. Checks transpiler correctness,
  6502 assembly, NES program conventions, MSBuild integration, snapshot tests,
  C# patterns, and AI-generated code pitfalls.
---

# dotnes PR Reviewer

Review PRs against guidelines for the dotnes transpiler — a tool that converts .NET IL into 6502 machine code to produce NES ROMs.

## Review Mindset

Be polite but skeptical. Prioritize bugs, correctness regressions, and transpiler safety over style nitpicks. **3 important comments > 15 nitpicks.**

This is a compiler/transpiler project — correctness is paramount. A subtle bug in opcode emission can produce a ROM that silently does the wrong thing, and the only way to catch it may be running in an emulator. Treat every change to `Transpiler.cs`, `IL2NESWriter.cs`, `NESWriter.cs`, `BuiltInSubroutines.cs`, or `Program6502.cs` with extra scrutiny.

Flag severity clearly in every comment:
- ❌ **error** — Must fix before merge. Bugs, incorrect 6502 emission, broken snapshot tests, security issues.
- ⚠️ **warning** — Should fix. Performance issues, missing test coverage, inconsistency with patterns.
- 💡 **suggestion** — Consider changing. Style, readability, optional improvements.

**Every review should produce at least one inline comment.** Even clean PRs have opportunities for improvement — missing edge-case tests, documentation gaps, or code consolidation. Use 💡 suggestions for these. Only omit inline comments for truly trivial PRs (1-line typo fix, dependency bump).

## Workflow

### 1. Identify the PR

If triggered from an agentic workflow (slash command on a PR), use the PR from the event context. Otherwise, extract `owner`, `repo`, `pr_number` from a URL or reference provided by the user.
Formats: `https://github.com/{owner}/{repo}/pull/{number}`, `{owner}/{repo}#{number}`, or bare number (defaults to `jonathanpeppers/dotnes`).

### 2. Gather context (before reading PR description)

```
gh pr diff {number} --repo {owner}/{repo}
gh pr view {number} --repo {owner}/{repo} --json files
```

For each changed file, read the **full source file** (not just the diff) to understand surrounding invariants, call patterns, and data flow. If the change modifies a public/internal API or utility, search for callers. Check whether sibling types need the same fix.

**Form an independent assessment** of what the change does and what problems it has *before* reading the PR description.

### 3. Incorporate PR narrative and reconcile

```
gh pr view {number} --repo {owner}/{repo} --json title,body
```

Now read the PR description and linked issues. Treat them as claims to verify, not facts to accept. Where your independent reading disagrees with the PR description, investigate further. If the PR claims a performance improvement, require evidence. If it claims a bug fix, verify the bug exists and the fix addresses root cause — not symptoms.

### 4. Check CI status

```
gh pr checks {number} --repo {owner}/{repo}
```

Review the CI results. **Never post ✅ LGTM if any required CI check is failing or if the code doesn't build.** If CI is failing:
- Investigate the failure.
- If the failure is caused by the PR's code changes, flag it as ❌ error.
- If the failure is a known infrastructure issue or pre-existing flake unrelated to the PR, note it in the summary but still use ⚠️ Needs Changes — the PR isn't mergeable until CI is green.

### 5. Load review rules

Based on the file types identified in step 2, read the appropriate rule files from this skill's `references/` directory.

**Always load:**
- `references/repo-conventions.md` — Formatting, style, and patterns specific to dotnes.
- `references/ai-pitfalls.md` — Common AI-generated code mistakes.

**Conditionally load based on changed file types:**
- `references/csharp-rules.md` — When any `.cs` files changed. Covers nullable, async, error handling, performance, and code organization.
- `references/transpiler-rules.md` — When files under `src/dotnes.tasks/` changed, especially `Transpiler.cs`, `IL2NESWriter.cs`, `NESWriter.cs`, `BuiltInSubroutines.cs`, or `Program6502.cs`. The core of this project.
- `references/nes-program-rules.md` — When files under `samples/` changed, or when `NESLib.cs` changed, or when the diff contains NES API calls (e.g., `pal_col`, `ppu_on_all`, `oam_spr`). Covers NES program constraints and neslib API usage.
- `references/testing-rules.md` — When test files changed (files under `src/dotnes.tests/`) or when transpiler changes lack corresponding test additions.
- `references/msbuild-rules.md` — When `.targets`, `.props`, or `.csproj` files changed, or when `TranspileToNES.cs` changed.
- `references/security-rules.md` — When any code files changed (C# or MSBuild).

### 6. Analyze the diff

For each changed file, check against the loaded review rules. Record issues as:

```json
{ "path": "src/Example.cs", "line": 42, "side": "RIGHT", "body": "..." }
```

**What to look for (in priority order):**
1. **Transpiler correctness** — Wrong opcodes, incorrect address modes, broken label resolution, ROM layout changes
2. **Snapshot regressions** — Changes that would alter `.verified.bin` output for unchanged samples
3. **Bugs & correctness** — Logic errors, off-by-one, null dereferences
4. **Missing tests** — Transpiler changes without `RoslynTests`, new samples without snapshot data
5. **Performance** — Unnecessary allocations, O(n²) patterns in hot transpiler paths
6. **Code duplication** — Near-identical methods that should be consolidated
7. **Documentation** — Misleading comments, undocumented behavioral decisions, missing `docs/msbuild-properties.md` updates

Constraints:
- Only comment on added/modified lines in the diff — the API rejects out-of-range lines.
- `line` = line number in the NEW file (right side). Double-check against the diff.
- One issue per comment.
- **Don't pile on.** If the same issue appears many times, flag it once with a note listing all affected files.
- **Don't flag what CI catches.** Skip compiler errors, formatting the linter will catch, etc.
- **Avoid false positives.** Verify the concern actually applies given the full context. If unsure, phrase it as a question rather than a firm claim.

### 7. Post the review

Post your findings directly:

- **Inline comments** on specific lines of the diff with the severity, category, and explanation.
- **Review summary** with the overall verdict (✅ LGTM, ⚠️ Needs Changes, or ❌ Reject), issue counts by severity, and positive callouts.

If no issues found **and CI is green**, submit with at most one or two 💡 suggestions and a positive summary. Truly trivial PRs (dependency bumps, 1-line typo fixes) may have no inline comments.

**Copilot-authored PRs:** If the PR author is `Copilot` (the GitHub Copilot coding agent) and the verdict is ⚠️ Needs Changes or ❌ Reject, prefix the review summary with `@copilot ` so the comment automatically triggers Copilot to address the feedback. Do NOT add the prefix for ✅ LGTM verdicts.

## Comment format

```
🤖 {severity} **{Category}** — {What's wrong and what to do instead.}
```

Where `{severity}` is ❌, ⚠️, or 💡.

**Categories:** Transpiler correctness · 6502 emission · ROM layout · Snapshot integrity · NES program · neslib API · MSBuild · Nullable · Async pattern · Error handling · Performance · Code organization · Testing · YAGNI · API design · Documentation · Security
