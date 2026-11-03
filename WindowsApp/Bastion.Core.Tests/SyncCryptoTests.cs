using System.Text;
using Bastion.Core;

namespace Bastion.Core.Tests;

public class SyncCryptoTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), $"bastion-cs-enc-test-{Guid.NewGuid()}");

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    // Kända testvektorer för PBKDF2-HMAC-SHA256 (password="password", salt="salt"),
    // identiska med Tests/SSHCoreTests/SyncCryptoTests.swift::testPBKDF2KnownAnswerVectors.
    [Theory]
    [InlineData(1, "120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b")]
    [InlineData(2, "ae4d0c95af6b46d32d0adff928f06dd02a303f8ef3c251dfd6e2d85a95474c43")]
    [InlineData(4096, "c5e478d59288c841aa530db6845c4c8d962893a001ce4e11a4963873aa98134a")]
    public void Pbkdf2KnownAnswerVectorsMatchSwiftAndRustReference(int iterations, string expectedHex)
    {
        var key = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes("password"), Encoding.UTF8.GetBytes("salt"), iterations,
            System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        Assert.Equal(expectedHex, Hex(key));
    }

    private static SyncState SampleState() => new()
    {
        Hosts = [Host.Create("web", "10.0.0.5", "deploy")],
    };

    [Fact]
    public void SealOpenRoundTrips()
    {
        var blob = SyncCrypto.Seal(SampleState(), "correct horse", 1000);
        var opened = SyncCrypto.Open(blob, "correct horse");
        Assert.Equal("web", opened.Hosts[0].Alias);
    }

    [Fact]
    public void WrongPassphraseFails()
    {
        var blob = SyncCrypto.Seal(SampleState(), "rätt", 1000);
        var ex = Assert.Throws<SyncCryptoException>(() => SyncCrypto.Open(blob, "fel"));
        Assert.Equal(SyncCryptoError.WrongPassphraseOrTampered, ex.Error);
    }

    [Fact]
    public void TamperedCiphertextFails()
    {
        var blob = SyncCrypto.Seal(SampleState(), "pw", 1000);
        blob[^1] ^= 0xFF;
        var ex = Assert.Throws<SyncCryptoException>(() => SyncCrypto.Open(blob, "pw"));
        Assert.Equal(SyncCryptoError.WrongPassphraseOrTampered, ex.Error);
    }

    [Fact]
    public void BadFormatIsRejectedCleanly()
    {
        var ex = Assert.Throws<SyncCryptoException>(() => SyncCrypto.Open(Encoding.UTF8.GetBytes("junk"), "pw"));
        Assert.Equal(SyncCryptoError.BadFormat, ex.Error);
    }

    /// <summary>
    /// En manipulerad fil kan sätta `iterations` i kuverthuvudet till vad
    /// som helst — ett absurt lågt (bruteforce-billigt) eller absurt högt
    /// (CPU-DoS) värde ska avvisas INNAN någon PBKDF2-körning eller
    /// AEAD-dekryptering ens försöks.
    /// </summary>
    [Theory]
    [InlineData(1u)]
    [InlineData(uint.MaxValue)]
    public void OutOfRangeIterationCountInTheEnvelopeHeaderIsRejected(uint tamperedIterations)
    {
        var blob = SyncCrypto.Seal(SampleState(), "pw", 1000);
        var iterationsBytes = BitConverter.GetBytes(tamperedIterations);
        if (BitConverter.IsLittleEndian) Array.Reverse(iterationsBytes);
        Array.Copy(iterationsBytes, 0, blob, 6, 4); // magic "BSYNC1" är 6 bytes
        var ex = Assert.Throws<SyncCryptoException>(() => SyncCrypto.Open(blob, "pw"));
        Assert.Equal(SyncCryptoError.BadFormat, ex.Error);
    }

    [Fact]
    public void CiphertextLeaksNoPlaintext()
    {
        var blob = SyncCrypto.Seal(SampleState(), "pw", 1000);
        var text = Encoding.UTF8.GetString(blob);
        Assert.DoesNotContain("10.0.0.5", text);
        Assert.DoesNotContain("deploy", text);
        Assert.DoesNotContain("web", text);
    }

    [Fact]
    public void EncryptedProviderConvergesAndRejectsWrongPassphrase()
    {
        var dir = TempDir();
        try
        {
            var sharedPath = Path.Combine(dir, "shared.enc");
            var provider = new EncryptedFolderSyncProvider(sharedPath, "delad-hemlis");

            var deviceA = new HostStore(Path.Combine(dir, "a.json"));
            var deviceB = new HostStore(Path.Combine(dir, "b.json"));

            var host = Host.Create("nas", "10.0.0.2", "root");
            deviceA.Upsert(host);
            deviceA.Sync(provider);
            deviceB.Sync(provider);

            Assert.Equal("nas", deviceB.All().Single(h => h.Id == host.Id).Alias);

            var wrong = new EncryptedFolderSyncProvider(sharedPath, "gissning");
            Assert.Throws<SyncCryptoException>(() => wrong.Pull());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EncryptedFolderSyncRequiresRetiringPlaintextAndPreservesBothSnapshots()
    {
        var dir = TempDir();
        try
        {
            var folder = Path.Combine(dir, "shared");
            var plainPath = Path.Combine(folder, "hosts.json");
            var encryptedPath = Path.Combine(folder, "hosts.enc");
            var plainHost = Host.Create("plain", "plain.example.com", "user");
            var encryptedHost = Host.Create("encrypted", "encrypted.example.com", "user");
            new FolderSyncProvider(plainPath).Push(new SyncState { Hosts = [plainHost] });
            new EncryptedFolderSyncProvider(encryptedPath, "pw").Push(new SyncState { Hosts = [encryptedHost] });
            var plainBytes = File.ReadAllBytes(plainPath);
            var encryptedBytes = File.ReadAllBytes(encryptedPath);
            var store = new HostStore(Path.Combine(dir, "local", "hosts.json"));

            Assert.Throws<PlaintextSyncSnapshotException>(() => EncryptedFolderSync.Sync(store, folder, "pw"));
            Assert.Equal(plainBytes, File.ReadAllBytes(plainPath));
            Assert.Equal(encryptedBytes, File.ReadAllBytes(encryptedPath));
            Assert.Empty(store.All());

            store.Sync(new FolderSyncProvider(plainPath));
            File.Move(plainPath, Path.Combine(dir, "retired.json"));
            EncryptedFolderSync.Sync(store, folder, "pw");

            var merged = new EncryptedFolderSyncProvider(encryptedPath, "pw").Pull()!;
            Assert.Contains(merged.Hosts, h => h.Id == plainHost.Id);
            Assert.Contains(merged.Hosts, h => h.Id == encryptedHost.Id);
            Assert.False(File.Exists(plainPath));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EncryptedFolderSyncCreatesNewFolderWithoutPlaintextSnapshot()
    {
        var dir = TempDir();
        try
        {
            var store = new HostStore(Path.Combine(dir, "local", "hosts.json"));
            store.Upsert(Host.Create("local", "local.example.com", "user"));
            var folder = Path.Combine(dir, "new-folder");

            EncryptedFolderSync.Sync(store, folder, "pw");

            Assert.True(File.Exists(Path.Combine(folder, "hosts.enc")));
            Assert.False(File.Exists(Path.Combine(folder, "hosts.json")));
            Assert.Equal("local", new EncryptedFolderSyncProvider(Path.Combine(folder, "hosts.enc"), "pw").Pull()!.Hosts.Single().Alias);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EncryptedFolderSyncDoesNotDeleteLocalDatabaseWhenItIsTheSyncFolder()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "hosts.json");
            var store = new HostStore(path);
            store.Upsert(Host.Create("local", "local.example.com", "user"));
            var original = File.ReadAllBytes(path);

            Assert.Throws<PlaintextSyncSnapshotException>(() => EncryptedFolderSync.Sync(store, dir, "pw"));

            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.False(File.Exists(Path.Combine(dir, "hosts.enc")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
