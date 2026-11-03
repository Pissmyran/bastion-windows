# Getting started

## Prerequisites

.NET SDK (latest version) for the core. Windows with the Windows App SDK for the app.

## Build and test

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

## Contents

| Path | Contents |
| --- | --- |
| `WindowsApp/Bastion.Core/` | The core: sessions, host list, sync, encryption and tools |
| `WindowsApp/Bastion.Core.Tests/` | Tests for the core, with its own copy of the sync fixture |
| `WindowsApp/` (root) | The WinUI app: `App.xaml`, `MainWindow.xaml` and `WindowsApp.csproj` |
