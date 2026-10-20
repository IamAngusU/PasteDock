PasteDock 0.6.0 protects unfinished edits and keeps intentional snippet variants independent.

- Restore an encrypted local editor draft after restarting. A visible status distinguishes successful recovery storage from pending changes, privacy rejection, size limits and storage failure.
- Keep the original with Save as new or Ctrl+Shift+Enter. A stale editor cannot overwrite a clip that changed or was removed.
- Preserve equal-content snippets as independent items through encrypted backup import and deletion undo. Repeated imports keep the current version of an existing snippet.
- Save new snippets without evicting other history. Failed saves and discard keep the editor available; failed recovery cleanup accurately reports an already saved clip.
- Use wrapping, character counts and recovery status in both themes and compact windows. Disabled history lists now retain the correct dark background.

Recovery is optional: one draft, at most 1,048,576 characters, expiring seven days after its last saved revision. Current privacy rules apply. Drafts are excluded from portable backups. Changes before the one-second save completes, or after a reported storage failure, may not survive a crash.

Executed local validation: **197 core tests, 16 Python tooling tests, 38 WPF scenario groups and 14 simulated paste scenarios**, plus native Windows checks. [Reports and remaining limits](https://github.com/IamAngusU/PasteDock/blob/main/docs/RELEASE-VALIDATION.md).

Extract the whole ZIP for your CPU and open PasteDock.exe. The runtime is included. Ctrl+Alt+V opens the workspace. No administrator account or sign-in is required. The runtime stays offline.

These are **unsigned preview builds**. x64 has been executed locally; ARM64 is cross-compiled with native acceptance outstanding. Full manual target-app, RDP and multi-monitor acceptance remains open. Hosted CI results are separate; no hosted pass is claimed when account execution prevents jobs from starting. SHA-256 checks download integrity, not publisher identity.
