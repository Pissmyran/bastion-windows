using Bastion.Core;

namespace Bastion.Core.Tests;

public class KnownHostsTests
{
    [Fact]
    public void StaleInstancesUseTheCurrentPin()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bastion-known-hosts-{Guid.NewGuid()}");
        var path = Path.Combine(dir, "known_hosts");
        try
        {
            var first = new KnownHosts(path);
            var stale = new KnownHosts(path);
            Assert.Equal(KnownHostVerdict.Learned, first.Check("example.com", 22, "ssh-ed25519 first").Verdict);
            Assert.Equal(KnownHostVerdict.Trusted, stale.Check("example.com", 22, "ssh-ed25519 first").Verdict);
            var conflict = stale.Check("example.com", 22, "ssh-ed25519 second");
            Assert.Equal(KnownHostVerdict.Changed, conflict.Verdict);
            Assert.Equal("ssh-ed25519 first", conflict.StoredKey);
            Assert.Equal("example.com:22 ssh-ed25519 first\n", File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ConcurrentInstancesCannotLearnConflictingKeys()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bastion-known-hosts-{Guid.NewGuid()}");
        var path = Path.Combine(dir, "known_hosts");
        try
        {
            var first = new KnownHosts(path);
            var second = new KnownHosts(path);
            using var start = new ManualResetEventSlim();
            var a = Task.Run(() => { start.Wait(); return first.Check("example.com", 22, "ssh-ed25519 first"); });
            var b = Task.Run(() => { start.Wait(); return second.Check("example.com", 22, "ssh-ed25519 second"); });
            start.Set();
            var results = await Task.WhenAll(a, b);
            Assert.Single(results, r => r.Verdict == KnownHostVerdict.Learned);
            Assert.Single(results, r => r.Verdict == KnownHostVerdict.Changed);
            Assert.Single(File.ReadAllLines(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AppendPreservesExistingLineWithoutTrailingNewline()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bastion-known-hosts-{Guid.NewGuid()}");
        var path = Path.Combine(dir, "known_hosts");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(path, "example.com:22 ssh-ed25519 first");
            var hosts = new KnownHosts(path);
            Assert.Equal(KnownHostVerdict.Learned, hosts.Check("other.example.com", 22, "ssh-ed25519 second").Verdict);
            Assert.Equal(KnownHostVerdict.Changed, hosts.Check("example.com", 22, "ssh-ed25519 second").Verdict);
            Assert.Equal(2, File.ReadAllLines(path).Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void InMemoryStoreRetainsFirstPin()
    {
        var hosts = new KnownHosts(null);
        Assert.Equal(KnownHostVerdict.Learned, hosts.Check("example.com", 22, "ssh-ed25519 first").Verdict);
        Assert.Equal(KnownHostVerdict.Trusted, hosts.Check("example.com", 22, "ssh-ed25519 first").Verdict);
        Assert.Equal(KnownHostVerdict.Changed, hosts.Check("example.com", 22, "ssh-ed25519 second").Verdict);
    }
}
