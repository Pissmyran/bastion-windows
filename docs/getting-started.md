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

The core and its tests target cross-platform .NET and can be tested anywhere. The WinUI app needs Windows.

<!-- coderabbit-ai-generated:v1 -->

<!-- coderabbit-ai-generated-section:v1:67b7461e63094781f0e169504071388221505b3d6a22335b29e4c6ced88e37c6 -->

Remote tar.gz compression requires Python 3.4 or later and tar. It fails if the destination archive already exists; remove or rename it before retrying.

<!-- /coderabbit-ai-generated:v1 -->

## Contents

| Path | Contents |
| --- | --- |
| `WindowsApp/Bastion.Core/` | The core: sessions, host list, sync, encryption and tools |
| `WindowsApp/Bastion.Core.Tests/` | Tests for the core, with its own copy of the sync fixture |
| `WindowsApp/` (root) | The WinUI app: `App.xaml`, `MainWindow.xaml` and `WindowsApp.csproj` |
