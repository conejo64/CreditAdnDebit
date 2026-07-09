# Security

## Secret scanning

This repository scans for committed secrets in two places:

1. **CI** — the `secret-scan` job in `.github/workflows/ci.yml` runs
   [`gitleaks`](https://github.com/gitleaks/gitleaks) against every push and
   pull request targeting `main`, using the rules and allowlist in
   [`.gitleaks.toml`](./.gitleaks.toml). The job fails the workflow run if a
   secret is detected.
2. **Pre-commit hook (local, recommended)** — a
   [`pre-commit`](https://pre-commit.com/) hook runs the same `gitleaks`
   scanner against staged changes before a commit is created, so a secret is
   caught before it ever enters git history.

### Setting up the pre-commit hook (fresh clone)

1. Install the `pre-commit` framework once (Python-based, works cross-platform):

   ```bash
   pip install pre-commit
   ```

2. From the repository root, install the git hook:

   ```bash
   pre-commit install
   ```

3. From then on, `git commit` automatically runs `gitleaks` against your
   staged changes. To run the hook against the entire repository on demand
   (for example, after pulling a large change):

   ```bash
   pre-commit run --all-files
   ```

If the hook blocks a commit, remove the flagged secret from the staged
change (use an environment variable / `backend/.env.example`-style
placeholder instead) and re-commit. Do not weaken `.gitleaks.toml`'s
allowlist to work around a real finding — file an issue if you believe a
detection is a genuine false positive.

## Reporting a vulnerability

If you discover a security vulnerability, please report it privately to the
maintainers rather than opening a public issue.
