using Bastion.Core;
using Xunit;

namespace Bastion.Core.Tests;

public class SftpBrowserSessionUtf8Tests
{
    [Fact]
    public void TryDecodeUtf8AcceptsValidText()
    {
        var ok = SftpBrowserSession.TryDecodeUtf8(System.Text.Encoding.UTF8.GetBytes("hej å ä ö"), out var text);
        Assert.True(ok);
        Assert.Equal("hej å ä ö", text);
    }

    [Fact]
    public void TryDecodeUtf8RejectsInvalidByteSequences()
    {
        // 0xFF är aldrig giltigt i UTF-8, i vilken position som helst.
        var ok = SftpBrowserSession.TryDecodeUtf8(new byte[] { 0x68, 0x65, 0x6A, 0xFF, 0x00 }, out var text);
        Assert.False(ok);
        Assert.Equal("", text);
    }
}

/// <summary>
/// Riktiga integrationstester mot localhosts sshd — samma
/// BASTION_TEST_SSH_KEY-mönster som SshSessionTests. Körs inte i vanlig
/// `dotnet test` (ingen nyckel satt i CI, och kontot den här koden faktiskt
/// körs som på mp100 har `DenyUsers claude` i sshd_config — SSH till
/// localhost är avsiktligt blockerat för det kontot, se
/// feedback_claude_account_no_docker_no_berduf_home).
/// </summary>
public class SftpBrowserSessionTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public SftpBrowserSessionTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private static bool HasTestKey => Environment.GetEnvironmentVariable("BASTION_TEST_SSH_KEY") is not null;

    private static Host BuildTestHost()
    {
        var keyPath = Environment.GetEnvironmentVariable("BASTION_TEST_SSH_KEY")!;
        var user = Environment.GetEnvironmentVariable("USER") ?? Environment.UserName;
        var host = Host.Create("test", "127.0.0.1", user);
        host.Auth = new HostAuth.KeyFile(keyPath);
        return host;
    }

    [Fact]
    public void ArchiveOperationsUseTheSelectedWhitespaceSuffixedDirectory()
    {
        using var sshd = TestSshd.Start();
        if (sshd is null)
        {
            _output.WriteLine("Standalone sshd unavailable; archive integration test did not run.");
            return;
        }
        _output.WriteLine("Standalone sshd started; running archive integration test.");
        var host = Host.Create("test", "127.0.0.1", Environment.GetEnvironmentVariable("USER") ?? Environment.UserName);
        host.Port = sshd.Port;
        host.Auth = new HostAuth.KeyFile(sshd.ClientKeyPath);
        var knownHosts = new KnownHosts(null);
        using var sftp = SftpBrowserSession.Connect(host, null, knownHosts);

        var root = $"/tmp/bastion-cs-archive-test-{Guid.NewGuid()}";
        var selectedName = "reports \t\n";
        var selected = $"{root}/{selectedName}";
        var neighbor = $"{root}/reports";
        sftp.CreateDirectory(root);
        try
        {
            sftp.CreateDirectory(selected);
            sftp.CreateDirectory(neighbor);
            sftp.WriteFile($"{selected}/selected.txt", "selected"u8.ToArray());
            sftp.WriteFile($"{neighbor}/private.txt", "private"u8.ToArray());

            var entry = Assert.Single(sftp.List(root), e => e.Name == selectedName);
            var resolved = ArchiveOperations.ParseWorkingDirectory(SshSession.RunCommand(
                host, null, knownHosts, ArchiveOperations.ResolveDirectoryCommand($"{root}/{entry.Name}")));
            Assert.Equal(selected, resolved);
            SshSession.RunCommand(host, null, knownHosts,
                ArchiveOperations.CreateTarGzCommand(new[] { "." }, $"../{entry.Name}.tar.gz", resolved));

            var archive = sftp.ReadFile($"{root}/{entry.Name}.tar.gz");
            using (var gzip = new System.IO.Compression.GZipStream(new MemoryStream(archive), System.IO.Compression.CompressionMode.Decompress))
            using (var tar = new System.Formats.Tar.TarReader(gzip))
            {
                var names = new List<string>();
                System.Formats.Tar.TarEntry? item;
                while ((item = tar.GetNextEntry()) is not null) names.Add(item.Name);
                Assert.Contains("./selected.txt", names);
                Assert.DoesNotContain("./private.txt", names);
            }

            sftp.WriteFile($"{selected}/archive.tar.gz", archive);
            sftp.RemoveFile($"{selected}/selected.txt");
            var extractionDirectory = ArchiveOperations.ParseWorkingDirectory(SshSession.RunCommand(
                host, null, knownHosts, ArchiveOperations.ResolveDirectoryCommand(selected)));
            SshSession.RunCommand(host, null, knownHosts,
                ArchiveOperations.ExtractTarGzCommand("archive.tar.gz", extractionDirectory));
            Assert.Equal("selected", System.Text.Encoding.UTF8.GetString(sftp.ReadFile($"{selected}/selected.txt")));
            Assert.Equal("private", System.Text.Encoding.UTF8.GetString(sftp.ReadFile($"{neighbor}/private.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Samma sak som <see cref="FullRoundTripAgainstARealSftpServer"/>, men
    /// mot en FRISTÅENDE test-sshd (se <see cref="TestSshd"/>) istället för
    /// den nyckel-gatade riktiga systemtjänsten — kan alltså faktiskt köra
    /// i den här sandlådan (och i CI) utan `BASTION_TEST_SSH_KEY`.
    /// </summary>
    [Fact]
    public void FullRoundTripAgainstAStandaloneTestSshd()
    {
        using var sshd = TestSshd.Start();
        if (sshd is null) return; // se TestSshd.Start doc — hoppar över om miljön saknar sshd/ssh-keygen
        var host = Host.Create("test", "127.0.0.1", Environment.GetEnvironmentVariable("USER") ?? Environment.UserName);
        host.Port = sshd.Port;
        host.Auth = new HostAuth.KeyFile(sshd.ClientKeyPath);
        var knownHosts = new KnownHosts(null);
        using var sftp = SftpBrowserSession.Connect(host, null, knownHosts);

        var dir = $"/tmp/bastion-cs-sftp-standalone-test-{Guid.NewGuid()}";
        sftp.CreateDirectory(dir);
        try
        {
            var filePath = $"{dir}/hello.txt";
            sftp.WriteFile(filePath, System.Text.Encoding.UTF8.GetBytes("hej å ä ö"));

            var readBack = sftp.ReadFile(filePath);
            Assert.True(SftpBrowserSession.TryDecodeUtf8(readBack, out var text));
            Assert.Equal("hej å ä ö", text);

            var listing = sftp.List(dir);
            Assert.Single(listing);
            Assert.Equal("hello.txt", listing[0].Name);
            Assert.False(listing[0].IsDirectory);

            var renamedPath = $"{dir}/renamed.txt";
            sftp.Rename(filePath, renamedPath);
            listing = sftp.List(dir);
            Assert.Equal("renamed.txt", listing[0].Name);

            sftp.RemoveFile(renamedPath);
            Assert.Empty(sftp.List(dir));
        }
        finally
        {
            sftp.RemoveDirectory(dir);
        }
    }

    [Fact]
    public void FullRoundTripAgainstARealSftpServer()
    {
        if (!HasTestKey) return; // se klassdoc
        var host = BuildTestHost();
        var knownHosts = new KnownHosts(null);
        using var sftp = SftpBrowserSession.Connect(host, null, knownHosts);

        var dir = $"/tmp/bastion-cs-sftp-test-{Guid.NewGuid()}";
        sftp.CreateDirectory(dir);
        try
        {
            var filePath = $"{dir}/hello.txt";
            sftp.WriteFile(filePath, System.Text.Encoding.UTF8.GetBytes("hej å ä ö"));

            var readBack = sftp.ReadFile(filePath);
            Assert.True(SftpBrowserSession.TryDecodeUtf8(readBack, out var text));
            Assert.Equal("hej å ä ö", text);

            var listing = sftp.List(dir);
            Assert.Single(listing);
            Assert.Equal("hello.txt", listing[0].Name);
            Assert.False(listing[0].IsDirectory);

            var renamedPath = $"{dir}/renamed.txt";
            sftp.Rename(filePath, renamedPath);
            listing = sftp.List(dir);
            Assert.Equal("renamed.txt", listing[0].Name);

            sftp.RemoveFile(renamedPath);
            Assert.Empty(sftp.List(dir));
        }
        finally
        {
            sftp.RemoveDirectory(dir);
        }
    }

}
