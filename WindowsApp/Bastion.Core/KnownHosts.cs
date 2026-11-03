using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Bastion.Core;

/// <summary>
/// Port av Sources/SSHCore/KnownHosts.swift (samma fil/format som
/// LinuxApp/src/known_hosts.rs): trust-on-first-use-lagring av
/// värdnycklar, `~/.bastion/known_hosts`, en rad per värd
/// (`host:port ssh-ed25519 AAAA...`).
/// </summary>
public enum KnownHostVerdict { Trusted, Learned, Changed }

public sealed record KnownHostResult(KnownHostVerdict Verdict, string? StoredKey = null);

public sealed class KnownHosts
{
    private readonly string? _path;
    private readonly Dictionary<string, string> _entries = new();
    private readonly object _lock = new();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bastion", "known_hosts");

    public KnownHosts(string? path) => _path = path is null ? null : Path.GetFullPath(path);

    public KnownHostResult Check(string host, int port, string keyString)
    {
        lock (_lock)
        {
            var id = $"{host}:{port}";
            if (_path is null)
                return CheckEntries(_entries, id, keyString);

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var file = OpenLocked(_path);
            using var reader = new StreamReader(file, Encoding.UTF8, true, leaveOpen: true);
            var entries = new Dictionary<string, string>();
            while (reader.ReadLine() is { } line)
            {
                var spaceIndex = line.IndexOf(' ');
                if (spaceIndex > 0) entries[line[..spaceIndex]] = line[(spaceIndex + 1)..];
            }

            var result = CheckEntries(entries, id, keyString);
            if (result.Verdict == KnownHostVerdict.Learned)
            {
                // Preserve an existing final line even if it has no newline.
                var needsNewline = false;
                if (file.Length > 0)
                {
                    file.Seek(-1, SeekOrigin.End);
                    needsNewline = file.ReadByte() != '\n';
                }
                file.Seek(0, SeekOrigin.End);
                using var writer = new StreamWriter(file, new UTF8Encoding(false), leaveOpen: true);
                if (needsNewline) writer.Write('\n');
                writer.Write($"{id} {keyString}\n");
                writer.Flush();
            }
            return result;
        }
    }

    private static KnownHostResult CheckEntries(Dictionary<string, string> entries, string id, string keyString)
    {
        if (entries.TryGetValue(id, out var stored))
            return stored == keyString
                ? new KnownHostResult(KnownHostVerdict.Trusted)
                : new KnownHostResult(KnownHostVerdict.Changed, stored);
        entries[id] = keyString;
        return new KnownHostResult(KnownHostVerdict.Learned);
    }

    private static FileStream OpenLocked(string path)
    {
        const int lockExclusive = 2;
        const int lockNonBlocking = 4;
        var timeout = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    // FileShare locks are best-effort on Unix. Verify that flock actually succeeded.
                    if (!OperatingSystem.IsWindows())
                    {
                        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
                            throw new PlatformNotSupportedException("Host key file locking is unsupported on this platform.");
                        if (Flock(file.SafeFileHandle.DangerousGetHandle().ToInt32(), lockExclusive | lockNonBlocking) != 0)
                            throw new IOException("Could not lock the host key file.", new Win32Exception(Marshal.GetLastPInvokeError()));
                    }
                    return file;
                }
                catch
                {
                    file.Dispose();
                    throw;
                }
            }
            catch (IOException) when (timeout.Elapsed < TimeSpan.FromSeconds(10))
            {
                Thread.Sleep(25);
            }
        }
    }

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(int fd, int operation);
}
