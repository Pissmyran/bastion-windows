# Sync format

<!-- coderabbit-ai-generated:v1 -->

<!-- coderabbit-ai-generated-section:v1:acf53bdf3eadedc7828a6f967fa363ef98ef75f18d95ad4f91d5a50d542fa918 -->

The Windows sync format follows the Swift models in [Bastion for Apple](https://github.com/pissmyran/bastion_apple), allowing it to be shared across platforms.

## Switching to encrypted sync

Encrypted sync reports an error while `hosts.json` is present in the selected folder. Stop plaintext sync on other devices, then sync in plaintext once to merge its contents into the local host database. Move `hosts.json` out of the shared folder and run encrypted sync; it merges the local state with any existing `hosts.enc`. Keep the moved file in a safe location until the encrypted state has been verified, then dispose of it securely. If the selected folder is Bastion's local database folder (`~/.bastion`), choose a different sync folder instead of moving the local database. Other devices or the folder provider can recreate the plaintext file. Provider history and backups may retain older copies.

<!-- /coderabbit-ai-generated:v1 -->

## The fixture

`WindowsApp/Bastion.Core.Tests/csharp-hosts-fixture.json` is a copy of the host format from [Bastion for Apple](https://github.com/pissmyran/bastion_apple/tree/main/Tests/fixtures). `HostStoreTests` reads it and shows that the C# side can decode Swift's format.

## When the format changes in Bastion for Apple

1. Update the fixture from `Tests/fixtures/` in Bastion for Apple.
2. Update `Host`, `HostStore`, `Snippet`, `SyncState`, `SyncEngine` and `SyncCrypto`.
3. Run the tests. Never change a fixture to make a test pass.
