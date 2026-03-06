---
name: pr-iterate
description: >-
  Iterate on a GitHub pull request from a coding agent (like Copilot coding agent)
  until it's ready for human review. Use this skill whenever the user asks to
  complete a PR, finish a PR, iterate on a PR, review and fix a PR, ship a PR,
  get a PR green, land a PR, or monitor a coding agent's PR. Also triggers when
  the user mentions PR numbers (e.g. "#108"), links to github.com pull requests,
  or says things like "when copilot is done", "clean up that PR", "make it green",
  or "iterate until CI passes". This skill handles the full lifecycle: code review,
  CI approval, test fixing, and repeated iteration until the PR is green and ready
  for human merge.
---

# PR Iterate

Take over a pull request from a coding agent and iterate on it until it's green
and ready for human review. The human will review and merge — never merge yourself.

## Bot Prefix Rule

**Every** comment, review body, or PR description edit you post must start with
the 🤖 emoji so humans can instantly tell it came from automation. This applies to:

- PR review bodies (`gh pr review --body "🤖 ..."`)
- PR comments (`gh pr comment --body "🤖 ..."`)
- Commit messages do NOT need the emoji (they already have the Co-authored-by trailer)

## Workflow

### 1. Assess the PR

```bash
gh pr view <number> --repo <owner>/<repo>
gh pr diff <number> --repo <owner>/<repo>
gh pr checks <number> --repo <owner>/<repo>
```

- Read the PR description to understand the intent and checklist
- Read the full diff to understand what changed
- Check mergeability: `gh api repos/<owner>/<repo>/pulls/<number> --jq '{mergeable, mergeable_state}'`
- Check if the coding agent is still working (look for recent commits, pending
  workflow runs from the Copilot bot). If it's still active, wait before touching
  anything.

### 2. Review the Code

Checkout the branch locally and do a thorough review:

- **Correctness**: Does the algorithm/logic work? Trace through edge cases.
- **Consistency**: Does it follow existing patterns in the codebase? Check similar
  handlers, tests, and naming conventions.
- **State management**: For this codebase specifically, check that flags like
  `_runtimeValueInA`, `_savedRuntimeToTemp`, `_ushortInAX` are properly set/cleared.
- **Tests**: Are the new tests meaningful? Do they follow the existing test style?

Run the full test suite locally to verify:

```powershell
$p = Start-Process dotnet -ArgumentList 'test','--nologo','-v','q' `
  -NoNewWindow -PassThru -RedirectStandardOutput test_out.txt -RedirectStandardError test_err.txt
$p.WaitForExit(300000)
Get-Content test_out.txt -Tail 30
```

### 3. Leave a Review

Submit a review on the PR. Always prefix the body with 🤖:

```bash
gh pr review <number> --repo <owner>/<repo> --comment --body "🤖 <your review>"
```

Use `--comment` for feedback that needs changes, `--approve` when it looks good.
Be specific about what you checked and any concerns.

### 4. Ship It (Mark Ready)

Once the code review passes:

1. **Remove draft status**: `gh pr ready <number> --repo <owner>/<repo>`
2. **Update title** (drop `[WIP]`): `gh pr edit <number> --repo <owner>/<repo> --title "<clean title>"`

### 5. Resolve Merge Conflicts

Before monitoring CI, check if the PR has merge conflicts:

```bash
gh api repos/<owner>/<repo>/pulls/<number> --jq '{mergeable, mergeable_state}'
```

If `mergeable` is `false` and `mergeable_state` is `"dirty"`:

1. **Fetch and merge main locally**:
   ```bash
   git fetch origin main
   git merge origin/main
   ```
2. **Resolve conflicts** — understand both sides of each conflict. If `main`
   implemented a feature that the PR was throwing an error for, keep main's
   implementation. If the PR improved code that main also touched, merge the
   intent of both changes.
3. **Run tests** to verify the resolution is correct
4. **Commit the merge** (use `git commit --no-edit` to keep the default merge message)
5. **Push** and continue to step 6

### 6. Get CI Green

Monitor the workflow run:

```bash
gh run list --repo <owner>/<repo> --branch <branch> --limit 3
gh run watch <run-id> --repo <owner>/<repo> --exit-status
```

If CI needs approval (shows "action_required"):

```bash
gh api repos/<owner>/<repo>/actions/runs/<run-id>/approve -X POST
```

If approval via API fails (403), the run may have auto-started on a subsequent
attempt. Check with `gh run list` for a newer in-progress run.

### 7. Fix Failures

If CI fails:

1. **Read the logs**: `gh run view <run-id> --repo <owner>/<repo> --log-failed`
2. **Reproduce locally**: Run `dotnet test` and verify the failure
3. **Fix the code**: Make the minimal fix needed
4. **Commit and push**:
   ```bash
   git add -A
   git commit -m "Fix: <description>

   Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
   git push origin <branch>
   ```
5. **Monitor the new CI run**: Go back to step 6

### 8. Address Review Feedback

After posting your review or after CI goes green, check for review comments from
humans or other bots:

```bash
gh pr view <number> --repo <owner>/<repo> --comments
gh api repos/<owner>/<repo>/pulls/<number>/reviews
gh api repos/<owner>/<repo>/pulls/<number>/comments
```

For each unresolved review comment:

1. **Understand the feedback** — read the comment in context of the code it references
2. **Fix the code** if the feedback is actionable and in scope for this PR
3. **Reply to the comment** explaining what you did (always prefix with 🤖):
   ```bash
   gh api repos/<owner>/<repo>/pulls/<number>/comments \
     -f body="🤖 Fixed — <what you changed and why>" \
     -F in_reply_to=<comment-id>
   ```
4. **Commit and push** the fix (with Co-authored-by trailer)
5. **File follow-up issues** for valid feedback that is out of scope for this PR.
   Reply to the comment acknowledging the point and linking the new issue:
   ```bash
   gh issue create --repo <owner>/<repo> \
     --title "<concise title>" \
     --body "Follow-up from PR #<number> review. <description>"
   gh api repos/<owner>/<repo>/pulls/<number>/comments \
     -f body="🤖 Valid point — filed #<issue> as a follow-up." \
     -F in_reply_to=<comment-id>
   ```

If a review requests changes, address all comments before re-requesting review.
After pushing fixes, go back to step 6 to monitor CI.

### 9. Iterate

Repeat steps 5-8 until CI is fully green, merge conflicts are resolved, and all
review comments are addressed (either fixed or filed as follow-ups).
Then verify:

```bash
gh pr checks <number> --repo <owner>/<repo>
```

Once all checks pass, post a final comment:

```bash
gh pr comment <number> --repo <owner>/<repo> --body "🤖 CI is green. Ready for human review."
```

## Important Rules

- **Never merge** — the human reviews and merges
- **Never force-push** — always push incremental commits
- **Always use `dotnet test` without `--no-build`** — the test project requires fresh builds
- **Use `Start-Process` with redirected output** for `dotnet test` — it hangs in
  interactive PowerShell due to MSBuild terminal output
- **Prefix all GitHub comments/reviews with 🤖**
- **Include the Co-authored-by trailer** in all commits
