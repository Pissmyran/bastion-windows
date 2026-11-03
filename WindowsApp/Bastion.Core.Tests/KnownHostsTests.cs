using System.Text.Json;
using Bastion.Core;

namespace Bastion.Core.Tests;

public class KnownHostsTests
{
    [Fact]
    public void RecordsRoundTripAndChangedKeysAreRejected()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bastion-known-hosts-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "known_hosts");
            File.WriteAllText(path, "example.com:22 ssh-ed25519 original");
            var hosts = new KnownHosts(path);
            Assert.Equal(KnownHostVerdict.Learned, hosts.Check("2001:db8::1", 2222, "ssh-ed25519 new").Verdict);
            var reopened = new KnownHosts(path);
            Assert.Equal(KnownHostVerdict.Trusted, reopened.Check("example.com", 22, "ssh-ed25519 original").Verdict);
            Assert.Equal(KnownHostVerdict.Trusted, reopened.Check("2001:db8::1", 2222, "ssh-ed25519 new").Verdict);
            var changed = reopened.Check("example.com", 22, "ssh-ed25519 attacker");
            Assert.Equal(KnownHostVerdict.Changed, changed.Verdict);
            Assert.Equal("ssh-ed25519 original", changed.StoredKey);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("server\0\nvictim:22 ssh-ed25519 forged")]
    [InlineData("server\nvictim:22 ssh-ed25519 forged")]
    [InlineData("server\rvictim")]
    [InlineData("server name")]
    [InlineData("server\tname")]
    [InlineData("server\u2028name")]
    [InlineData("")]
    public void InvalidHostNamesCannotWriteRecords(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bastion-known-hosts-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "known_hosts");
            const string original = "victim:22 ssh-ed25519 original\n";
            File.WriteAllText(path, original);
            var hosts = new KnownHosts(path);
            Assert.Throws<ArgumentException>(() => hosts.Check(name, 22, "ssh-ed25519 attacker"));
            Assert.Equal(original, File.ReadAllText(path));
            Assert.Equal(KnownHostVerdict.Changed, new KnownHosts(path).Check("victim", 22, "ssh-ed25519 attacker").Verdict);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("ssh-ed25519 key\nvictim:22 ssh-ed25519 forged")]
    [InlineData("ssh-ed25519 key\rvictim")]
    [InlineData("ssh-ed25519 key\0")]
    public void InvalidKeysCannotWriteRecords(string key)
    {
        var hosts = new KnownHosts(null);
        Assert.Throws<ArgumentException>(() => hosts.Check("example.com", 22, key));
        Assert.Equal(KnownHostVerdict.Learned, hosts.Check("example.com", 22, "ssh-ed25519 valid").Verdict);
    }

    [Theory]
    [InlineData("victim:22 ssh-ed25519 original\nvictim:22 ssh-ed25519 forged\n")]
    [InlineData("server\0\nvictim:22 ssh-ed25519 forged\n:22 ssh-ed25519 key\n")]
    [InlineData("server:22 ssh-ed25519 key\0\n")]
    [InlineData("incomplete-record\n")]
    [InlineData("missing-port ssh-ed25519 key\n")]
    public void MalformedOrConflictingFilesFailClosed(string contents)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, contents);
            Assert.Throws<InvalidDataException>(() => new KnownHosts(path));
            Assert.Equal(contents, File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IdenticalDuplicatePinsRemainReadable()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "example.com:22 ssh-ed25519 key\nexample.com:22 ssh-ed25519 key\n");
            Assert.Equal(KnownHostVerdict.Trusted, new KnownHosts(path).Check("example.com", 22, "ssh-ed25519 key").Verdict);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SyncedHostNameIsRejectedByEveryConnectionPathBeforeAuthentication()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bastion-host-sync-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            var syncPath = Path.Combine(dir, "sync.json");
            var host = Host.Create("test", "localhost\0\nvictim:22 ssh-ed25519 forged", "user");
            File.WriteAllText(syncPath, JsonSerializer.Serialize(new SyncState { Hosts = { host } }));
            var store = new HostStore(Path.Combine(dir, "hosts.json"));
            store.Sync(new FolderSyncProvider(syncPath));
            var imported = Assert.Single(store.All());
            var hosts = new KnownHosts(Path.Combine(dir, "known_hosts"));

            Assert.Throws<ArgumentException>(() => SshSession.Connect(imported, null, hosts));
            Assert.Throws<ArgumentException>(() => SshSession.RunCommand(imported, null, hosts, "true"));
            Assert.Throws<ArgumentException>(() => SftpBrowserSession.Connect(imported, null, hosts));
            Assert.False(File.Exists(Path.Combine(dir, "known_hosts")));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public void ChangedPinRejectsCommandAndSftpConnections()
    {
        using var sshd = TestSshd.Start();
        if (sshd is null) return;
        var dir = Path.Combine(Path.GetTempPath(), $"bastion-known-hosts-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            var host = Host.Create("test", "127.0.0.1", Environment.GetEnvironmentVariable("USER") ?? Environment.UserName);
            host.Port = sshd.Port;
            host.Auth = new HostAuth.KeyFile(sshd.ClientKeyPath);
            var path = Path.Combine(dir, "known_hosts");
            var original = $"127.0.0.1:{sshd.Port} ssh-ed25519 original\n";
            File.WriteAllText(path, original);
            var hosts = new KnownHosts(path);

            Assert.ThrowsAny<Exception>(() => SshSession.RunCommand(host, null, hosts, "true"));
            Assert.Throws<SshHostKeyChangedException>(() => SftpBrowserSession.Connect(host, null, hosts));
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }

}
