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
- **The fixture** in `Bastion.Core.Tests` is a copy of the sync format owned by `bastion_apple`.
<!-- coderabbit-ai-generated:v1 -->

<!-- coderabbit-ai-generated-section:v1:7039c90454929da2bd0818ae9ee212f02bd5e66360ca04bc6772d7823a47c613 -->

- Give extra care to host key verification, sync formats and encryption, and commands for remote systems. The [encrypted sync transition](docs/sync-format.md#switching-to-encrypted-sync) requires retiring the plaintext snapshot separately.

<!-- /coderabbit-ai-generated:v1 -->

## Review configuration

CodeRabbit inherits [pissmyran/coderabbit/.coderabbit.yaml](https://github.com/pissmyran/coderabbit/blob/main/.coderabbit.yaml); [.coderabbit.yaml](.coderabbit.yaml) adds Windows-specific review guidance and cross-platform context. Read the matching path instructions before changing those files. Use `@coderabbitai configuration` on the pull request and follow [docs/operations.md](docs/operations.md#coderabbit-configuration) to verify the resolved settings and source access.
