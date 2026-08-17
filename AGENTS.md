# Repository rules

## Public repository boundary

- This repository is public at `https://github.com/nihil-string/CodexQuotaWidget`.
- Treat every commit, branch, tag, release, GitHub Actions log, issue, pull request,
  comment, uploaded artifact, and screenshot as permanently public. Public data may
  be forked, cached, indexed, or mirrored even after deletion.
- Fail closed: when it is unclear whether content is sensitive or licensed for
  publication, do not commit, push, upload, or quote it until the uncertainty is
  resolved.

## Mandatory redaction before publication

Before any commit, push, tag, release, issue, pull request, comment, or artifact
upload:

1. Inspect the exact scope with `git status -sb`, `git diff`, and
   `git diff --cached`. Stage only intended files; do not use broad staging in a
   mixed worktree.
2. Scan the current files and, when a value may have been committed previously,
   every reachable Git object. Checking only the current tree is insufficient.
3. Never publish passwords, access or refresh tokens, API keys, cookies,
   authorization headers, private keys, certificates, connection strings,
   credential-bearing URLs, `.env` files, `auth.json`, credential stores, or
   real authentication fixtures.
4. Redact personal and machine-specific data: real Windows usernames, home or
   workspace paths, hostnames, private addresses, email addresses, account IDs,
   session/thread/report identifiers, process dumps, and unrelated local project
   names. Prefer relative repository paths.
5. Use generic placeholders such as `%USERPROFILE%`, `%CODEX_HOME%`,
   `C:\Users\user`, `<redacted>`, and `example.com`. Test credentials and IDs
   must be synthetic, obviously fake, and unusable.
6. Upload only the minimum log excerpt needed to reproduce a problem. Redact
   paths, identifiers, request headers, and payloads. Never upload complete Codex
   sessions, databases, application logs, WER bundles, crash dumps, or local
   settings without a field-by-field review.
7. Inspect every image visually and inspect its metadata. Crop unrelated desktop
   areas, taskbars, notifications, other applications, account names, and local
   paths. Do not assume a screenshot is safe because its filename is generic.
8. Ensure commit messages, shell output, test reports, CI annotations, and GitHub
   Actions logs do not echo sensitive values. Do not print a secret merely to
   prove that a scanner found it.
9. Audit release archives from their final file list, not only from source paths.
   Exclude `bin/`, `obj/`, `artifacts/`, credentials, local settings, dumps, and
   temporary files unless a specific reviewed release artifact requires them.
   Record SHA-256 for distributed binaries and archives.
10. Keep any pre-redaction history backup outside the repository in a private
    directory. Never push, attach, or publish such a backup.

`.gitignore` is defense in depth, not proof that sensitive data was never
committed or uploaded.

## If sensitive data enters Git history

- Stop publication immediately. Do not try to fix the incident by deleting only
  the current file or adding a later redaction commit.
- Revoke or rotate exposed credentials first.
- Rewrite every affected public ref, push with an exact `--force-with-lease`, and
  rescan the rewritten history before resuming publication.
- Verify affected old commit URLs without authentication. If GitHub still serves
  an unreachable sensitive object, use GitHub's sensitive-data removal process
  or contact GitHub Support before considering the incident closed.
- Report what was exposed, which refs and artifacts were affected, what was
  rotated or rewritten, and what residual copies or private backups remain.

## Public project and licensing rules

- The project is released under the MIT License. Preserve `LICENSE` and required
  copyright/license notices in source and distributed archives.
- Preserve the statement that CodexQuotaWidget is an independent, unofficial
  community project and is not affiliated with, sponsored by, or endorsed by
  OpenAI.
- Do not copy third-party source, binaries, images, fonts, or documentation into
  the repository unless their license and attribution requirements have been
  reviewed and satisfied. Update `ACKNOWLEDGEMENTS.md` when attribution changes.
- Security reports containing non-public details belong in GitHub private
  vulnerability reporting, not public issues.
