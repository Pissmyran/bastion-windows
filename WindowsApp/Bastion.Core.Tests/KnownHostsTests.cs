using System.Text;
using Bastion.Core;

namespace Bastion.Core.Tests;

public class KnownHostsTests
{
    [Theory]
    [InlineData("", "utf8")]
    [InlineData("\n", "utf8")]
    [InlineData("\r", "utf8")]
    [InlineData("\r\n", "utf8")]
    [InlineData("", "utf8-bom")]
    [InlineData("", "utf16-le")]
    [InlineData("\r", "utf16-be")]
    [InlineData("", "utf32-le")]
    [InlineData("\n", "utf32-be")]
    public void LearnedPinSurvivesReopeningAfterExistingRecord(string separator, string encodingName)
    {
        Encoding encoding = encodingName switch
        {
            "utf8" => new UTF8Encoding(false),
            "utf8-bom" => new UTF8Encoding(true),
            "utf16-le" => new UnicodeEncoding(false, true),
            "utf16-be" => new UnicodeEncoding(true, true),
            "utf32-le" => new UTF32Encoding(false, true),
            "utf32-be" => new UTF32Encoding(true, true),
            _ => throw new ArgumentException(nameof(encodingName))
        };
        var dir = Path.Combine(Path.GetTempPath(), $"bastion-known-hosts-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "known_hosts");
        try
        {
            File.WriteAllText(path, $"old.example:22 ssh-ed25519 OLD{separator}", encoding);
            var hosts = new KnownHosts(path);
            Assert.Equal(KnownHostVerdict.Learned, hosts.Check("new.example", 2222, "ssh-ed25519 ORIGINAL").Verdict);

            var reopened = new KnownHosts(path);
            Assert.Equal(KnownHostVerdict.Trusted, reopened.Check("old.example", 22, "ssh-ed25519 OLD").Verdict);
            Assert.Equal(KnownHostVerdict.Trusted, reopened.Check("new.example", 2222, "ssh-ed25519 ORIGINAL").Verdict);
            var changed = reopened.Check("new.example", 2222, "ssh-ed25519 REPLACEMENT");
            Assert.Equal(KnownHostVerdict.Changed, changed.Verdict);
            Assert.Equal("ssh-ed25519 ORIGINAL", changed.StoredKey);
            Assert.Equal($"old.example:22 ssh-ed25519 OLD{(separator == "" ? "\n" : separator)}new.example:2222 ssh-ed25519 ORIGINAL\n", File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstPinSurvivesReopening(bool createEmptyFile)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bastion-known-hosts-{Guid.NewGuid()}");
        var path = Path.Combine(dir, "known_hosts");
        try
        {
            if (createEmptyFile)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, "");
            }
            Assert.Equal(KnownHostVerdict.Learned, new KnownHosts(path).Check("new.example", 22, "ssh-ed25519 ORIGINAL").Verdict);
            Assert.Equal("new.example:22 ssh-ed25519 ORIGINAL\n", File.ReadAllText(path));
            Assert.Equal(KnownHostVerdict.Changed, new KnownHosts(path).Check("new.example", 22, "ssh-ed25519 REPLACEMENT").Verdict);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void FirstPinInBomOnlyFileSurvivesReopening()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bastion-known-hosts-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "known_hosts");
        try
        {
            File.WriteAllText(path, "", new UnicodeEncoding(false, true));
            Assert.Equal(KnownHostVerdict.Learned, new KnownHosts(path).Check("new.example", 22, "ssh-ed25519 ORIGINAL").Verdict);
            Assert.Equal(KnownHostVerdict.Changed, new KnownHosts(path).Check("new.example", 22, "ssh-ed25519 REPLACEMENT").Verdict);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FailedWriteDoesNotMarkTheKeyTrustedInMemory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bastion-known-hosts-{Guid.NewGuid()}");
        var path = Path.Combine(dir, "known_hosts");
        Directory.CreateDirectory(path);
        try
        {
            var hosts = new KnownHosts(path);
            Assert.ThrowsAny<Exception>(() => hosts.Check("new.example", 22, "ssh-ed25519 ORIGINAL"));
            Directory.Delete(path);
            Assert.Equal(KnownHostVerdict.Learned, hosts.Check("new.example", 22, "ssh-ed25519 ORIGINAL").Verdict);
            Assert.Equal(KnownHostVerdict.Changed, new KnownHosts(path).Check("new.example", 22, "ssh-ed25519 REPLACEMENT").Verdict);
        }
        finally { Directory.Delete(dir, true); }
    }
}
