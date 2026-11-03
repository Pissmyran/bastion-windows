using Bastion.Core;
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
    public void CreateTarGzCreatesArchiveAndRefusesExistingDestinations()
    {
        if (OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), $"bastion-archive-{Guid.NewGuid()}");
        var source = Path.Combine(root, "source ' folder");
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllText(Path.Combine(source, "file ' name"), "archive content");
            File.WriteAllText(Path.Combine(source, "sitecustomize.py"), "open('injected', 'w').write('executed')");
            var command = ArchiveOperations.CreateTarGzCommand(new[] { "file ' name" }, "../out.tar.gz", source);
            Assert.Equal(0, RunShell(command, source));
            Assert.False(File.Exists(Path.Combine(source, "injected")));
            Assert.Equal(0, RunShell($"tar xOzf {ArchiveOperations.ShellQuote(Path.Combine(root, "out.tar.gz"))} | test \"$(cat)\" = 'archive content'"));

            var output = Path.Combine(root, "out.tar.gz");
            var original = File.ReadAllBytes(output);
            Assert.NotEqual(0, RunShell(command));
            Assert.Equal(original, File.ReadAllBytes(output));

            File.Delete(output);
            var victim = Path.Combine(root, "victim");
            File.WriteAllText(victim, "untouched");
            File.CreateSymbolicLink(output, victim);
            Assert.NotEqual(0, RunShell(command));
            Assert.Equal("untouched", File.ReadAllText(victim));

            File.Delete(output);
            var missing = Path.Combine(root, "missing");
            File.CreateSymbolicLink(output, missing);
            Assert.NotEqual(0, RunShell(command));
            Assert.False(File.Exists(missing));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static int RunShell(string command, string? pythonPath = null)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh") { UseShellExecute = false, RedirectStandardError = true };
        if (pythonPath is not null) psi.Environment["PYTHONPATH"] = pythonPath;
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    [Fact]
    public void ExtractTarGzCommandMatchesReferenceImplementation() =>
        Assert.Equal("cd '/home/x' && tar xzf 'out.tar.gz'", ArchiveOperations.ExtractTarGzCommand("out.tar.gz", "/home/x"));

    [Fact]
    public void CreateZipCommandMatchesReferenceImplementation() =>
        Assert.Equal(
            "cd '/home/x' && zip -r -q './out.zip' -- 'a.txt'",
            ArchiveOperations.CreateZipCommand(new[] { "a.txt" }, "out.zip", "/home/x"));

    [Fact]
    public void ExtractZipCommandMatchesReferenceImplementation() =>
        Assert.Equal("cd '/home/x' && unzip -o -q './out.zip'", ArchiveOperations.ExtractZipCommand("out.zip", "/home/x"));
}
