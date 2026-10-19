# Contributing

PasteDock is a native Windows clipboard workspace, written in C# and WPF on .NET 10. The app works offline and has no third-party runtime packages.

Use the SDK selected by `global.json`. On Windows, run `scripts/test.ps1`. On any supported .NET host, run `dotnet run --project tests/PasteDock.Core.Tests -c Release`. Source tooling uses Python 3.10+:

```sh
python -m unittest discover -s scripts/tests -v
python scripts/validate-source.py --report artifacts/reports/source.json
python scripts/audit-features.py --report artifacts/reports/features.json
```

Keep clipboard payloads out of logs, issues, screenshots and test fixtures. Use synthetic content. Test the failure path that motivated a fix: locked desktops, clipboard contention, changing targets, cancellation, damaged stores and full quotas matter here. Do not replace a complete payload with its display preview.

UI changes must work in light and dark themes, with keyboard focus, reduced motion and a narrow window. `--ui-smoke` renders the actual WPF views in a temporary workspace; it does not open the user's history or replace the clipboard. `--diagnostics` adds native checks and simulated paste failures. [Validation and remaining manual checks](docs/RELEASE-VALIDATION.md).

Keep commits free of generated tool-credit trailers. [ByeClaude](https://github.com/IamAngusU/ByeClaude) can audit and protect a local clone; configuring hooks is a contributor choice. No Git history rewrite is needed to contribute.

Open a focused issue or pull request with a reproducible trigger, expected and observed behavior, Windows version and the relevant content-free report. Use private reporting for sensitive vulnerabilities; see [SECURITY.md](SECURITY.md).
