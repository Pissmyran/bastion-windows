using System.Text;

namespace Bastion.Core;

/// <summary>
/// Port av Sources/SSHCore/ArchiveOperations.swift (samma design som
/// LinuxApp/src/archive.rs). SFTP version 3 har ingen egen arkivsemantik —
/// shellar ut till tar/zip över <see cref="SshSession.RunCommand"/> (samma
/// engångsexec-mönster som Docker-vyn). Sökvägar VALIDERAS inte mot en
/// whitelist (filnamn kan legitimt innehålla mellanslag/unicode) — istället
/// citeras varje sökväg för sig med enkla citattecken (POSIX-shell-säkert).
/// </summary>
public static class ArchiveOperations
{
    /// <summary>Enkla citattecken runt s, med inbäddade ' eskapade som '\'' — standard POSIX-shell-säkert sätt att citera GODTYCKLIG text.</summary>
    public static string ShellQuote(string s) => $"'{s.Replace("'", "'\\''")}'";

    /// <summary>
    /// Run a POSIX-quoted archive command with an explicit sh. The login shell
    /// sees only a fixed command and octal digits, never the path characters.
    /// printf's %b decodes UTF-8 bytes inside sh before eval parses the command.
    /// </summary>
    public static string ForPosixShell(string command)
    {
        var encoded = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(command))
            encoded.Append("\\0").Append(Convert.ToString(b, 8).PadLeft(3, '0'));
        return $"sh -c 'eval \"$(printf %b \"$1\")\"' sh '{encoded}'";
    }

    public static string CreateTarGzCommand(IReadOnlyList<string> paths, string archiveName, string directory)
    {
        var quotedPaths = string.Join(" ", paths.Select(ShellQuote));
        return $"cd {ShellQuote(directory)} && tar czf {ShellQuote(archiveName)} -- {quotedPaths}";
    }

    public static string ExtractTarGzCommand(string archiveName, string directory) =>
        $"cd {ShellQuote(directory)} && tar xzf {ShellQuote(archiveName)}";

    /// <summary>
    /// ./-prefix på arkivnamnet + -- före sökvägarna — zip tar arkivnamnet
    /// som ett rent positionellt argument, så ett namn som börjar med -
    /// skulle annars tolkas som en flagga.
    /// </summary>
    public static string CreateZipCommand(IReadOnlyList<string> paths, string archiveName, string directory)
    {
        var quotedPaths = string.Join(" ", paths.Select(ShellQuote));
        var safeArchiveName = $"./{archiveName}";
        return $"cd {ShellQuote(directory)} && zip -r -q {ShellQuote(safeArchiveName)} -- {quotedPaths}";
    }

    public static string ExtractZipCommand(string archiveName, string directory) =>
        $"cd {ShellQuote(directory)} && unzip -o -q {ShellQuote($"./{archiveName}")}";
}
