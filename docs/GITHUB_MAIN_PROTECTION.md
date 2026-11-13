# Minimum GitHub protection for `main`

The repository currently has no classic branch protection and no ruleset for
`main`. Apply the following repository setting before merging the authentication
and combined-hosting pull requests. This does not deploy the application.

## Recommended ruleset

In GitHub, open **Settings > Rules > Rulesets > New branch ruleset** and target
the default branch (`main`). Set the ruleset to **Active** only after reviewing
the settings below.

Require:

- changes to reach `main` through a pull request;
- all review conversations to be resolved;
- the branch to be up to date before merging;
- these status checks:
  - `Combined App Service publish`
  - `Combined App Service Windows publish`
  - `Frontend test and build`
  - `Backend build and PostgreSQL verification`
  - `Security audit`
  - `Code Quality Checks`
  - `Test Coverage`
  - `Dependency Review`
- deletion protection; and
- force-push protection.

Do not require the Vercel Preview check or Lighthouse as merge gates. Vercel is
a deployment integration rather than an application test, and Lighthouse runs
after a push to `main` rather than on pull requests.

If a second qualified reviewer is available, require one approval and dismiss
stale approvals when new commits are pushed. If the repository currently has
only one qualified maintainer, start with zero required approvals while still
requiring pull requests and all checks; never use a non-technical approval as a
substitute for code review. Increase the approval count when another qualified
reviewer is added.

Do not allow routine bypasses. Repository emergency administrators may retain a
documented break-glass path, but any bypass should be exceptional and audited.
Do not require linear history until the stacked PR #2/PR #3 sequence is complete,
because the recommended merge-commit order intentionally preserves ancestry.

## Safe stacked merge order

1. Confirm Vercel Production Branch Tracking no longer follows `main`.
2. Confirm Azure Deployment Center does not continuously deploy `main`.
3. Merge PR #2 into `main` with a merge commit.
4. Retarget PR #3 from `fix/instructor-role-authority` to `main`.
5. Confirm PR #3 now shows only its combined-hosting changes.
6. Rerun every required check, then merge PR #3 only after they all pass.

Changing this ruleset, merging either pull request, or changing a deployment
integration requires explicit owner approval.
