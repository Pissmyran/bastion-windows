namespace Bastion.Core;

/// <summary>
/// Port av Sources/SSHCore/ArchiveOperations.swift (samma design som
/// LinuxApp/src/archive.rs). SFTP version 3 har ingen egen arkivsemantik —
/// kör tar, zip eller Python över <see cref="SshSession.RunCommand"/> (samma
/// engångsexec-mönster som Docker-vyn). Sökvägar VALIDERAS inte mot en
/// whitelist (filnamn kan legitimt innehålla mellanslag/unicode) — istället
/// citeras varje sökväg för sig med enkla citattecken (POSIX-shell-säkert).
/// </summary>
public static class ArchiveOperations
{
    /// <summary>Enkla citattecken runt s, med inbäddade ' eskapade som '\'' — standard POSIX-shell-säkert sätt att citera GODTYCKLIG text.</summary>
    public static string ShellQuote(string s) => $"'{s.Replace("'", "'\\''")}'";

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

    // Open each destination component relative to an already-open directory,
    // without following links. Replace files by unlinking and exclusively
    // creating them so an existing hard link cannot redirect a write either.
    private const string ExtractZipScript = """
import contextlib, os, shutil, stat, sys, zipfile

archive, directory = sys.argv[1:]
if not os.path.isabs(directory) or '/' in archive or archive in ('', '.', '..'):
    raise ValueError('Invalid archive or directory name')
flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW
with contextlib.ExitStack() as roots:
    root = os.open('/', flags)
    roots.callback(os.close, root)
    for part in directory.split('/'):
        if part in ('', '.'):
            continue
        if part == '..':
            raise ValueError('Invalid directory')
        root = os.open(part, flags, dir_fd=root)
        roots.callback(os.close, root)
    with os.fdopen(os.open(archive, os.O_RDONLY, dir_fd=root), 'rb') as source, zipfile.ZipFile(source) as z:
        members = []
        for item in z.infolist():
            name = item.orig_filename
            parts = [p for p in name.split('/') if p not in ('', '.')]
            kind = stat.S_IFMT(item.external_attr >> 16) if item.create_system == 3 else 0
            if ('\x00' in name or '\\' in name or name.startswith('/') or '..' in parts
                    or kind not in (0, stat.S_IFREG, stat.S_IFDIR)
                    or (kind == stat.S_IFDIR and not item.is_dir())
                    or (kind == stat.S_IFREG and item.is_dir())
                    or (not parts and not item.is_dir())):
                raise ValueError('Unsafe ZIP member: ' + repr(name))
            members.append((item, parts))
        for item, parts in members:
            with contextlib.ExitStack() as opened:
                parent = root
                for part in parts if item.is_dir() else parts[:-1]:
                    try:
                        os.mkdir(part, 0o700, dir_fd=parent)
                    except FileExistsError:
                        pass
                    parent = os.open(part, flags, dir_fd=parent)
                    opened.callback(os.close, parent)
                if not item.is_dir():
                    with z.open(item) as contents:
                        try:
                            os.unlink(parts[-1], dir_fd=parent)
                        except FileNotFoundError:
                            pass
                        mode = (item.external_attr >> 16) & 0o777 if item.create_system == 3 and item.external_attr >> 16 else 0o600
                        fd = os.open(parts[-1], os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, mode, dir_fd=parent)
                        with os.fdopen(fd, 'wb') as target:
                            shutil.copyfileobj(contents, target)
""";

    public static string ExtractZipCommand(string archiveName, string directory) =>
        $"python3 -c {ShellQuote(ExtractZipScript)} {ShellQuote(archiveName)} {ShellQuote(directory)}";
}
