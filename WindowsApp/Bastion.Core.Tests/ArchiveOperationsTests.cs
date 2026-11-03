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
    public void ExtractZipCommandMatchesReferenceImplementation() =>
        Assert.Equal("cd '/home/x' && unzip -o -q './out.zip'", ArchiveOperations.ExtractZipCommand("out.zip", "/home/x"));

    [Fact]
    public void ArchiveCommandsTreatHostileNamesAsData()
    {
        if (OperatingSystem.IsWindows()) return;
        ExerciseArchiveCommands("/bin/sh", "-c");
    }

    [Fact]
    public void ArchiveCommandsTreatHostileNamesAsDataInPowerShell()
    {
        var powerShell = Environment.GetEnvironmentVariable("BASTION_TEST_PWSH");
        if (string.IsNullOrEmpty(powerShell)) return;
        ExerciseArchiveCommands(powerShell, "-Command");
    }

    private static void ExerciseArchiveCommands(string shell, string commandOption)
    {
        var root = Path.Combine(Path.GetTempPath(), $"bastion-archive-{Guid.NewGuid()}");
        var directory = Path.Combine(root, "dir';touch PWNED;echo 'å\n");
        var archive = "archive';touch PWNED;echo 'å.tar.gz";
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "content.txt"), "content");
            RunArchiveCommand(shell, commandOption, root, ArchiveOperations.CreateTarGzCommand(new[] { "." }, "../" + archive, directory));
            Assert.True(File.Exists(Path.Combine(root, archive)));
            File.Delete(Path.Combine(directory, "content.txt"));
            RunArchiveCommand(shell, commandOption, root, ArchiveOperations.ExtractTarGzCommand(archive, root));
            Assert.Equal("content", File.ReadAllText(Path.Combine(root, "content.txt")));
            Assert.False(File.Exists(Path.Combine(root, "PWNED")));
            Assert.False(File.Exists(Path.Combine(directory, "PWNED")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RunArchiveCommand(string shell, string commandOption, string workingDirectory, string command)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(shell)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(commandOption);
        psi.ArgumentList.Add(ArchiveOperations.ForPosixShell(command));
        using var process = System.Diagnostics.Process.Start(psi)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }
}
