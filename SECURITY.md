# Security Policy

## Supported versions

| Version | Supported |
|---|---|
| 1.1.x | Yes |
| 1.0.x | No — please update |

Only the latest released minor version receives fixes.

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

1. Open a private report through GitHub:
   <https://github.com/taozhihaoo/file-flow/security/advisories/new>
2. If that is not possible, open a regular issue that only asks for a private contact
   channel — without technical details.

Please include:

- affected version (the version is shown in the sidebar and in `%LOCALAPPDATA%\FileFlow\logs`),
- a description of the issue and its impact,
- reproduction steps, and a proof of concept if you have one,
- any suggested mitigation.

**What to expect:** acknowledgement within 5 working days, an assessment within 10 working days, and a
fix or a documented decision in the next release. Reporters are credited in the release notes unless
they ask otherwise.

## Scope

In scope:

- the application (`FileFlow.App`), the domain layer (`FileFlow.Core`) and the infrastructure layer
  (`FileFlow.Infrastructure`),
- data handling: the SQLite database, CSV import/export, log files,
- the build and CI configuration in this repository.

Out of scope:

- vulnerabilities in Windows, the .NET runtime or third-party NuGet packages — report those upstream
  (we still want to know if FileFlow triggers them),
- findings that require an attacker who already has code execution or administrator rights on the
  machine,
- "the application can delete files" — that is its documented purpose.

## Security model

FileFlow is a **single-user, local-only desktop application**. It assumes the Windows user account and
its file-system permissions are the trust boundary.

- **No network access.** No HTTP client, no telemetry, no update check, no cloud sync, no crash
  reporting. File contents and metadata never leave the machine.
- **No elevation.** The application never requests administrator rights and never bypasses Windows
  ACLs. Operations that the current user is not permitted to perform fail with a classified error.
- **No file content access.** Scans read file metadata only (name, size, timestamps). Import/export
  handles metadata CSV; file contents are never read, hashed or uploaded.
- **Data location.** Everything lives in `%LOCALAPPDATA%\FileFlow` (or the `--datadir` folder):
  `fileflow.db` (WAL mode) and `logs/*.log`. There is no encryption at rest; confidentiality relies on
  the Windows user profile ACLs, and the database is readable by the user account that created it.
- **Logs** contain file paths, operation counts and classified error kinds. They never contain file
  contents, secrets or credentials, but they do contain user paths — consider that before sharing a
  log file.

## Safeguards for destructive operations

- Every high-risk operation (rename, move, copy, delete, extension change) shows a full
  before→after preview with per-file validation before anything touches the disk.
- Delete goes to the Windows Recycle Bin by default (`FOF_ALLOWUNDO`); permanent delete must be
  enabled explicitly in Settings.
- `ProtectedPathGuard` refuses to plan operations inside critical system directories
  (`C:\Windows`, `C:\Program Files*`, `C:\ProgramData`) without an additional confirmation warning.
- Operations are cancellable, progress is persisted per item, and a crash mid-batch is detected on
  next start (`Interrupted`) instead of silently continuing.
- File operations never overwrite an existing target: a collision is reported and the item is skipped.

## Implementation notes

- All SQL is parameterized; no query string is built from user input. `LIKE` patterns are escaped.
- Paths are validated before use (`PathValidator`, `FileNameValidator`); UNC/network paths and
  reserved device names are rejected.
- The UI layer translates classified errors into readable messages and writes technical details to the
  log, so error text never leaks internals to the screen.
- The task queue and file operations are covered by automated tests, including concurrency and crash
  recovery scenarios.
- CSV import is treated as untrusted input: headers are validated, malformed rows are skipped and
  reported with their line numbers, and imported paths must pass the same path validation as scanned
  ones.

## Dependencies

Runtime dependencies are limited to Microsoft.Extensions.* and `Microsoft.Data.Sqlite`; there is no
web framework, no scripting engine and no plugin system. NuGet packages are restored from nuget.org in
CI and pinned by version in the project files. Dependency updates are applied through pull requests
that must pass the full build and test suite.

## Disclosure policy

We follow coordinated disclosure: a fix is prepared and released before details become public. Once a
fix is available, the advisory is published on GitHub with credit to the reporter.
