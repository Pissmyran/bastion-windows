# Bastion for Windows

**Documentation:** [pissmyran.github.io/bastion_windows](https://pissmyran.github.io/bastion_windows/)

Bastion is an SSH client. This repo holds the version for Windows with WinUI, built with C#, .NET, WinUI and SSH.NET. The other platforms have their own repos: [Bastion for Apple](https://github.com/pissmyran/bastion_apple), [Bastion for Linux](https://github.com/pissmyran/bastion_linux), [Bastion for Android](https://github.com/pissmyran/bastion_android). The [Bastion hub](https://github.com/pissmyran/bastion) ties them together.

## Contents

| Path | Contents |
| --- | --- |
| `WindowsApp/Bastion.Core/` | The core: sessions, host list, sync, encryption and tools |
| `WindowsApp/Bastion.Core.Tests/` | Tests for the core, with its own copy of the sync fixture |
| `WindowsApp/` (root) | The WinUI app: `App.xaml`, `MainWindow.xaml` and `WindowsApp.csproj` |

## Getting started

Test the core (works on Linux, macOS and Windows):

```bash
dotnet test WindowsApp/Bastion.Core.Tests
```

Build the app (Windows only):

```bash
dotnet build WindowsApp/WindowsApp.csproj
```

<!-- coderabbit-ai-generated:v1 -->

<!-- coderabbit-ai-generated-section:v1:4d09ba8730e3415736cb32143eb877bff93aaa05d777b3be0707d0483bb1ddea -->

The core and its tests run on cross-platform .NET; the WinUI app requires Windows. Remote archive actions require a POSIX `sh` and the relevant `tar` or `unzip` utility, with SFTP paths accessible to that shell.

<!-- /coderabbit-ai-generated:v1 -->

## Review configuration

CodeRabbit inherits shared defaults from [pissmyran/coderabbit](https://github.com/pissmyran/coderabbit). Repository-specific guidance lives in [.coderabbit.yaml](.coderabbit.yaml). Comment `@coderabbitai configuration` on the pull request to inspect the resolved settings; see [verification steps](docs/operations.md#coderabbit-configuration).

## More

- [The documentation](https://pissmyran.github.io/bastion_windows/): getting started, architecture, operations, sync format and releases
- [SECURITY.md](SECURITY.md): report a vulnerability
- [CONTRIBUTING.md](CONTRIBUTING.md): contribute
