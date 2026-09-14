---
name: code-review
description: >-
  Review dotnes pull requests against established repository rules. Use this
  skill whenever the user asks for a code review, says "review this PR",
  provides a GitHub pull request URL or number, or asks whether a change is
  ready to merge. Checks transpiler correctness, 6502 assembly, NES and cc65
  conventions, MSBuild integration, snapshot tests, C# patterns, security,
  and AI-generated code pitfalls.
---

# dotnes Code Review

Review PRs against guidelines for the dotnes transpiler — a tool that converts .NET IL into 6502 machine code to produce NES ROMs.

## Review Mindset

Be polite but skeptical. Prioritize bugs, correctness regressions, and transpiler safety over style nitpicks. **3 important comments > 15 nitpicks.**

This is a compiler/transpiler project — correctness is paramount. A subtle bug in opcode emission can produce a ROM that silently does the wrong thing, and the only way to catch it may be running in an emulator. Treat every change to `Transpiler.cs`, `IL2NESWriter.cs`, `NESWriter.cs`, `BuiltInSubroutines.cs`, or `Program6502.cs` with extra scrutiny.

Flag severity clearly in every comment:
- ❌ **error** — Must fix before merge. Bugs, incorrect 6502 emission, broken snapshot tests, security issues.
- ⚠️ **warning** — Should fix. Performance issues, missing test coverage, inconsistency with patterns.
- 💡 **suggestion** — Consider changing. Style, readability, optional improvements.

For substantive PRs, prefer at least one useful inline comment over suggestions
buried in the summary. Do not invent a nit merely to produce a comment. Only
comment on a changed line where the observation is actionable and specific.

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

Verify project context before applying a rule. Check the target framework,
project references, build imports, and existing compatibility helpers so a rule
for modern .NET is not incorrectly applied to `netstandard2.0`, or vice versa.

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
- For Azure DevOps checks, use the `az devops` command to inspect the build and
  logs. For GitHub Actions, inspect the failed job logs.
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
- `references/native-rules.md` — When `.c`, `.h`, or cc65 reference source files
  changed. Covers reference parity, C safety, ownership, and compiler limits.
- `references/security-rules.md` — When any code files changed (C# or MSBuild).

### 6. Analyze the diff

For each changed file, check against the loaded review rules. Record issues as:

```json
{ "path": "src/Example.cs", "line": 42, "side": "RIGHT", "body": "..." }
```

**What to look for (in priority order):**
1. **Transpiler correctness** — Wrong opcodes, incorrect address modes, broken label resolution, ROM layout changes
2. **Safety and determinism** — Resource leaks, path/command vulnerabilities, non-deterministic ROM output
3. **Snapshot regressions** — Changes that would alter `.verified.bin` output for unchanged samples
4. **Bugs & correctness** — Logic errors, off-by-one, null dereferences
5. **Missing tests** — Transpiler changes without `RoslynTests`, new samples without snapshot data
6. **Performance** — Unnecessary allocations, O(n²) patterns in hot transpiler paths
7. **Code duplication** — Near-identical methods that should be consolidated
8. **Documentation** — Misleading comments, undocumented behavioral decisions, missing `docs/msbuild-properties.md` updates

Constraints:
- Only comment on added/modified lines in the diff — the API rejects out-of-range lines.
- `line` = line number in the NEW file (right side). Double-check against the diff.
- One issue per comment.
- **Don't pile on.** If the same issue appears many times, flag it once with a note listing all affected files.
- **Don't flag what CI catches.** Skip compiler errors, formatting the linter will catch, etc.
- **Avoid false positives.** Verify the concern actually applies given the full context. If unsure, phrase it as a question rather than a firm claim.
- **Verify downstream behavior.** Trace changed values to their final consumer;
  helper names and comments are not proof of semantics.

### 7. Post the review

Post your findings directly:

- **Inline comments** on specific lines of the diff with the severity, category, and explanation.
- **Review summary** with the overall verdict (✅ LGTM, ⚠️ Needs Changes, or ❌ Reject), issue counts by severity, and positive callouts.

If no issues are found **and CI is green**, submit a positive summary. Add at
most one or two 💡 suggestions only when they are concrete and worthwhile.
Truly trivial PRs (dependency bumps, 1-line typo fixes) may have no inline
comments.

**Copilot-authored PRs:** If the PR author is `Copilot` (the GitHub Copilot coding agent) and the verdict is ⚠️ Needs Changes or ❌ Reject, prefix the review summary with `@copilot ` so the comment automatically triggers Copilot to address the feedback. Do NOT add the prefix for ✅ LGTM verdicts.

## Comment format

```
🤖 {severity} **{Category}** — {What's wrong and what to do instead.}
```

Where `{severity}` is ❌, ⚠️, or 💡.

**Categories:** Transpiler correctness · 6502 emission · ROM layout · Snapshot integrity · NES program · neslib API · C reference · MSBuild · Nullable · Async pattern · Error handling · Resource management · Performance · Code organization · Testing · YAGNI · API design · Documentation · Security
