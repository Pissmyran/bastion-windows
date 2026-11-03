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
    private readonly Dictionary<string, string> _entries;
    private readonly object _lock = new();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bastion", "known_hosts");

    public KnownHosts(string? path)
    {
        _path = path;
        _entries = Load(path);
    }

    private static Dictionary<string, string> Load(string? path)
    {
        var result = new Dictionary<string, string>();
        if (path is null || !File.Exists(path)) return result;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0) continue;
            var spaceIndex = line.IndexOf(' ');
            if (spaceIndex <= 0 || spaceIndex == line.Length - 1)
                throw new InvalidDataException("Malformed known-host record.");
            var id = line[..spaceIndex];
            var key = line[(spaceIndex + 1)..];
            var portSeparator = id.LastIndexOf(':');
            if (portSeparator <= 0 || !int.TryParse(id.AsSpan(portSeparator + 1), out _) ||
                id.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) ||
                key.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029'))
                throw new InvalidDataException("Malformed known-host record.");
            if (result.TryGetValue(id, out var stored) && stored != key)
                throw new InvalidDataException($"Conflicting known-host records for {id}.");
            result[id] = key;
        }
        return result;
    }

    internal static void ValidateHostName(string host)
    {
        if (string.IsNullOrEmpty(host) || host.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
            throw new ArgumentException("Host name must not be empty or contain whitespace or control characters.", nameof(host));
    }

    public KnownHostResult Check(string host, int port, string keyString)
    {
        ValidateHostName(host);
        if (string.IsNullOrEmpty(keyString) || keyString.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029'))
            throw new ArgumentException("Host key must not be empty or contain control characters.", nameof(keyString));
        lock (_lock)
        {
            var id = $"{host}:{port}";
            if (_entries.TryGetValue(id, out var stored))
                return stored == keyString
                    ? new KnownHostResult(KnownHostVerdict.Trusted)
                    : new KnownHostResult(KnownHostVerdict.Changed, stored);

            Append(id, keyString);
            _entries[id] = keyString;
            return new KnownHostResult(KnownHostVerdict.Learned);
        }
    }

    private void Append(string id, string keyString)
    {
        if (_path is null) return;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // A valid existing file may not end in a newline.
        var separator = "";
        if (File.Exists(_path))
        {
            using var file = File.OpenRead(_path);
            if (file.Length > 0)
            {
                file.Seek(-1, SeekOrigin.End);
                var lastByte = file.ReadByte();
                if (lastByte != '\n' && lastByte != '\r') separator = "\n";
            }
        }
        File.AppendAllText(_path, $"{separator}{id} {keyString}\n");
    }
}
