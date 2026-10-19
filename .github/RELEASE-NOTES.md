PasteDock 0.5.0 makes the local clipboard workspace easier to use every day.

- Review and reorder the actual paste queue; remove one occurrence or use Copy & advance for manual paste. Failures retain the entry, and repeated clips have distinct completion tickets.
- Give clips durable names with F2. Names survive editing, repeat capture and encrypted backup.
- Keep your own notes in My snippets. Saved snippets survive automatic retention and remain snippets after re-copying from another app.
- Exclude words, phrases, apps, tags and content types. Deep search also checks negative terms beyond the instant index.
- Choose among five sort orders. Filter controls stay reachable in a narrow window.
- Read errors in a wrapping recovery banner, review draft discard, inspect CSS colors, wrap text and navigate the command menu with arrows. Theme-aware scrollbars work in both directions.
- Save sort/wrap preferences with window settings.

Executed local validation: **167 core tests, 16 Python tooling tests, 25 WPF scenario groups and 14 simulated paste scenarios**, plus native Windows checks. Reports and remaining limits are documented in [release validation](https://github.com/IamAngusU/PasteDock/blob/main/docs/RELEASE-VALIDATION.md).

Extract the entire ZIP for your CPU and open PasteDock.exe. The runtime is included. Ctrl+Alt+V opens the workspace. No administrator account or sign-in is required.

These are **unsigned preview builds**. x64 has been executed locally; ARM64 is cross-compiled and native acceptance remains outstanding. Full manual target-app, remote-desktop and multi-monitor acceptance is still open. Hosted CI results must be read separately; no hosted pass is claimed when account execution prevents jobs from starting. SHA-256 files check download integrity, not publisher identity.
