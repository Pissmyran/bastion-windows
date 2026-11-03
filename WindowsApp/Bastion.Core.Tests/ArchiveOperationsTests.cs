using Bastion.Core;
using System.Diagnostics;
using System.IO.Compression;
using Xunit;

namespace Bastion.Core.Tests;

public class ArchiveOperationsTests
{
    [Fact]
    public void ShellQuoteEscapesEmbeddedSingleQuotes()
    {
        Assert.Equal("'plain'", ArchiveOperations.ShellQuote("plain"));
        Assert.Equal("'with space'", ArchiveOperations.ShellQuote("with space"));
        Assert.Equal(@"'it'\''s'", ArchiveOperations.ShellQuote("it's"));
    }

    /// <summary>Bevisar att en filnamn-injektion FAKTISKT nollställs av citeringen — en RIKTIG shell (/bin/sh -c) tolkar hela kommandot som EN sökväg.</summary>
    [Fact]
    public void ShellQuoteSurvivesRealShellParsing()
    {
        var malicious = $"innocent'; touch /tmp/bastion-cs-injection-proof-{Guid.NewGuid()}; echo '";
        var quoted = ArchiveOperations.ShellQuote(malicious);

        // ArgumentList (inte Arguments-strängen) så /bin/sh -c får EXAKT en sträng, orörd av .NET:s egen citering.
        var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"printf '%s' {quoted}");

        using var process = System.Diagnostics.Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(malicious, output);
    }

    [Fact]
    public void CreateTarGzCommandMatchesReferenceImplementation() =>
        Assert.Equal(
            "cd '/home/x' && tar czf 'out.tar.gz' -- 'a.txt' 'b.txt'",
            ArchiveOperations.CreateTarGzCommand(new[] { "a.txt", "b.txt" }, "out.tar.gz", "/home/x"));

    [Fact]
    public void ExtractTarGzCommandMatchesReferenceImplementation() =>
        Assert.Equal("cd '/home/x' && tar xzf 'out.tar.gz'", ArchiveOperations.ExtractTarGzCommand("out.tar.gz", "/home/x"));

    [Fact]
    public void CreateZipCommandMatchesReferenceImplementation() =>
        Assert.Equal(
            "cd '/home/x' && zip -r -q './out.zip' -- 'a.txt'",
            ArchiveOperations.CreateZipCommand(new[] { "a.txt" }, "out.zip", "/home/x"));

    [Fact]
    public void ExtractZipWritesOrdinaryFilesAndDoesNotFollowExistingLinks()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new ZipTestDirectory();
        var outside = Path.Combine(temp.Path, "outside");
        var destination = Path.Combine(temp.Path, "it's the destination");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(destination);
        var victim = Path.Combine(outside, "victim");
        File.WriteAllText(victim, "original");
        File.WriteAllText(Path.Combine(destination, "existing"), "old");
        File.CreateSymbolicLink(Path.Combine(destination, "file-link"), victim);
        Assert.Equal(0, Run("ln", victim, Path.Combine(destination, "hard-link")));
        CreateZip(Path.Combine(destination, "it's.zip"), ("nested/new", "new"), ("existing", "updated"),
            ("file-link", "link replacement"), ("hard-link", "hard link replacement"));

        Assert.Equal(0, Run("/bin/sh", "-c", "umask 022; " + ArchiveOperations.ExtractZipCommand("it's.zip", destination)));
        Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "nested/new")));
        Assert.Equal("updated", File.ReadAllText(Path.Combine(destination, "existing")));
        Assert.Equal("link replacement", File.ReadAllText(Path.Combine(destination, "file-link")));
        Assert.Equal("hard link replacement", File.ReadAllText(Path.Combine(destination, "hard-link")));
        Assert.Equal("original", File.ReadAllText(victim));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.Combine(destination, "nested")));
    }

    [Fact]
    public void ExtractZipRejectsDestinationDirectorySymlink()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new ZipTestDirectory();
        var destination = Path.Combine(temp.Path, "destination");
        var outside = Path.Combine(temp.Path, "outside");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(destination, "sub"), outside);
        CreateZip(Path.Combine(destination, "archive.zip"), ("sub/proof", "escaped"));

        Assert.NotEqual(0, Run("/bin/sh", "-c", ArchiveOperations.ExtractZipCommand("archive.zip", destination)));
        Assert.False(File.Exists(Path.Combine(outside, "proof")));
    }

    [Fact]
    public void ExtractZipOverSshReportsUnsafeDestination()
    {
        if (OperatingSystem.IsWindows()) return;
        using var sshd = TestSshd.Start();
        if (sshd is null) return;
        using var temp = new ZipTestDirectory();
        var destination = Path.Combine(temp.Path, "destination");
        var outside = Path.Combine(temp.Path, "outside");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(destination, "sub"), outside);
        CreateZip(Path.Combine(destination, "archive.zip"), ("sub/proof", "escaped"));
        CreateZip(Path.Combine(destination, "safe.zip"), ("safe", "content"));
        var host = Host.Create("test", "127.0.0.1", Environment.GetEnvironmentVariable("USER") ?? Environment.UserName);
        host.Port = sshd.Port;
        host.Auth = new HostAuth.KeyFile(sshd.ClientKeyPath);
        var knownHosts = new KnownHosts(null);

        SshSession.RunCommand(host, null, knownHosts, ArchiveOperations.ExtractZipCommand("safe.zip", destination), checkExitStatus: true);
        Assert.Equal("content", File.ReadAllText(Path.Combine(destination, "safe")));
        Assert.Throws<InvalidOperationException>(() => SshSession.RunCommand(host, null, knownHosts,
            ArchiveOperations.ExtractZipCommand("archive.zip", destination), checkExitStatus: true));
        Assert.False(File.Exists(Path.Combine(outside, "proof")));
    }

    [Fact]
    public void ExtractZipRejectsSymlinkInDestinationPath()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new ZipTestDirectory();
        var target = Path.Combine(temp.Path, "target");
        Directory.CreateDirectory(target);
        CreateZip(Path.Combine(target, "archive.zip"), ("proof", "escaped"));
        var link = Path.Combine(temp.Path, "link");
        Directory.CreateSymbolicLink(link, target);
        Assert.NotEqual(0, Run("/bin/sh", "-c", ArchiveOperations.ExtractZipCommand("archive.zip", link)));
        Assert.False(File.Exists(Path.Combine(target, "proof")));
    }

    [Theory]
    [InlineData("../outside/proof")]
    [InlineData("/absolute/proof")]
    [InlineData("dir/../../outside/proof")]
    [InlineData("dir\\proof")]
    public void ExtractZipRejectsUnsafeMemberPaths(string name)
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new ZipTestDirectory();
        CreateZip(Path.Combine(temp.Path, "archive.zip"), ("safe", "not written"), (name, "bad"));
        Assert.NotEqual(0, Run("/bin/sh", "-c", ArchiveOperations.ExtractZipCommand("archive.zip", temp.Path)));
        Assert.False(File.Exists(Path.Combine(temp.Path, "safe")));
    }

    [Fact]
    public void ExtractZipRejectsArchiveSymlinksBeforeWriting()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new ZipTestDirectory();
        using (var stream = File.Create(Path.Combine(temp.Path, "archive.zip")))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            zip.CreateEntry("safe");
            zip.CreateEntry("link").ExternalAttributes = unchecked((int)0xA1FF0000);
        }
        Assert.NotEqual(0, Run("/bin/sh", "-c", ArchiveOperations.ExtractZipCommand("archive.zip", temp.Path)));
        Assert.False(File.Exists(Path.Combine(temp.Path, "safe")));
    }

    [Fact]
    public void ExtractZipRespectsArchiveFilePermissions()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new ZipTestDirectory();
        using (var stream = File.Create(Path.Combine(temp.Path, "archive.zip")))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            zip.CreateEntry("private").ExternalAttributes = unchecked((int)0x81800000); // regular file, 0600
            zip.CreateEntry("executable").ExternalAttributes = unchecked((int)0x81ED0000); // regular file, 0755
        }
        var command = "umask 022; " + ArchiveOperations.ExtractZipCommand("archive.zip", temp.Path);
        Assert.Equal(0, Run("/bin/sh", "-c", command));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(temp.Path, "private")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute,
            File.GetUnixFileMode(Path.Combine(temp.Path, "executable")));
    }

    private static void CreateZip(string path, params (string Name, string Content)[] entries)
    {
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(content);
        }
    }

    private static int Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    private sealed class ZipTestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(PhysicalTempPath(), $"bastion-zip-{Guid.NewGuid()}");
        public ZipTestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);

        private static string PhysicalTempPath()
        {
            var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, RedirectStandardOutput = true };
            foreach (var argument in new[] { "-c", "cd \"$1\" && pwd -P", "sh", System.IO.Path.GetTempPath() })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var path = process.StandardOutput.ReadToEnd().TrimEnd('\r', '\n');
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
            return path;
        }
    }
}
