# AGENTS.md – bastion_windows

Bastion for Windows: the SSH client for Windows with WinUI, built with C#, .NET, WinUI and SSH.NET.

Read the shared rules at the start of every session: [GOVERNANCE.md](https://raw.githubusercontent.com/pissmyran/.github/main/GOVERNANCE.md) and [BASTION.md](https://raw.githubusercontent.com/pissmyran/.github/main/BASTION.md).

## Read first

1. [docs/architecture.md](docs/architecture.md): components and security boundaries.
2. [docs/operations.md](docs/operations.md): verification before pushing.
3. [docs/sync-format.md](docs/sync-format.md): when the host list or sync is involved.

## Commands

Test the core (works on Linux, macOS and Windows):

```bash
dotnet test WindowsApp/Bastion.Core.Tests
```

Build the app (Windows only):

```bash
dotnet build WindowsApp/WindowsApp.csproj
```

## Rules

- **The WinUI app cannot be built on Linux.** Test the core.

<!-- coderabbit-ai-generated:v1 -->

<!-- coderabbit-ai-generated-section:v1:3b5f96739b73ad194614d7253288a250867d8109e015e57f210ea468305246a7 -->

- **SFTP editor:** downloads are limited to 1 MiB while reading, including files whose reported size is inaccurate.

<!-- /coderabbit-ai-generated:v1 -->
- **The fixture** in `Bastion.Core.Tests` is a copy of the sync format owned by `bastion_apple`.
- **Extra care:** host key verification, sync format and encryption, commands built for remote systems.

## Review configuration

CodeRabbit inherits [pissmyran/coderabbit/.coderabbit.yaml](https://github.com/pissmyran/coderabbit/blob/main/.coderabbit.yaml); [.coderabbit.yaml](.coderabbit.yaml) adds Windows-specific review guidance and cross-platform context. Read the matching path instructions before changing those files. Use `@coderabbitai configuration` on the pull request and follow [docs/operations.md](docs/operations.md#coderabbit-configuration) to verify the resolved settings and source access.
