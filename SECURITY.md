# Security policy

Do not put clipboard contents, passwords, tokens, private files or decrypted backups in a public issue. Use GitHub private vulnerability reporting when available, or the private contact options at [angusu.de](https://angusu.de).

Supported preview: **0.6.x**. The current-user Windows DPAPI store protects data at rest. It does not protect against malware running as the same Windows user, screen capture, or a compromised desktop. Recognizable-secret filtering is best effort, not a password detector.

The app has no network client, telemetry or automatic updater. Repository badges are documentation assets; they do not run inside the application. Exports and temporary paste files are ordinary local files. Encrypted portable backups use a separate passphrase.

See the detailed [storage and privacy model](docs/SECURITY.md), [release validation](docs/RELEASE-VALIDATION.md), and [architecture](docs/ARCHITECTURE.md).
