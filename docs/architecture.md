# Architecture

Bastion for Windows is a standalone .NET app. The core is a port of the Swift core in Bastion for Apple, with the same field names in the sync format.

```text
        +-----------------------------+
        |   WinUI app (WindowsApp/)   |
        +--------------+--------------+
                       |
        +--------------+--------------+
        |  Bastion.Core (.NET, C#)    |
        |  ssh, sync, encryption      |
        +--------------+--------------+
                       |
                    SSH.NET
```

## Parts

- **Bastion.Core** holds everything that is not user interface and can be tested without Windows.
- **The WinUI app** in `WindowsApp/` is only the interface on top of the core.
- **Sync** (`SyncEngine`, `SyncState`, `SyncCrypto`) follows the format from Bastion for Apple.

## Security boundary

Host key verification in `KnownHosts.cs` and `HostAuth.cs` rejects a changed key for the same host and port. Keys and passwords are never stored in the repo or in test data.

<!-- coderabbit-ai-generated:v1 -->

<!-- coderabbit-ai-generated-section:v1:1f8462fc4c557d011feaf2499a78d1fe9d31fec455e1ffdb9aa73fe2070f4af6 -->

ZIP extraction uses Python 3 on the SSH host and opens destination directories without following symbolic links; it rejects archive links and unsafe member paths.

<!-- /coderabbit-ai-generated:v1 -->
