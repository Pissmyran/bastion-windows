using System.Text.Json;

namespace Bastion.Core;

/// <summary>
/// Port av SyncEngine.merge — deterministisk last-write-wins-sammanslagning,
/// identisk med Swift och LinuxApp/src/sync.rs. Se SYNC_PROTOCOL.md i
/// repo-roten för den formella specifikationen.
/// </summary>
public static class SyncEngine
{
    /// <summary>
    /// Stabil, ordningsoberoende tiebreak-nyckel för två <see cref="Host"/>-
    /// värden med EXAKT samma <c>ModifiedAt</c> — jämförelsen bryr sig bara
    /// om att den ger SAMMA svar för samma par oavsett besöksordning, inte
    /// om vilken av dem som "objektivt" är bäst (det finns inget sådant på
    /// en äkta tidsstämpel-krock). Samma princip som
    /// <c>LinuxApp/src/sync.rs</c>s <c>tiebreak_key</c>.
    /// </summary>
    private static string TiebreakKey(Host h) => JsonSerializer.Serialize(h);

    public static SyncState Merge(SyncState a, SyncState b)
    {
        var newestHost = new Dictionary<Guid, (Host Host, string? Key)>();
        foreach (var h in a.Hosts.Concat(b.Hosts))
        {
            if (!newestHost.TryGetValue(h.Id, out var existing))
            {
                newestHost[h.Id] = (h, null);
                continue;
            }
            // `>=` gjorde detta ORDNINGSBEROENDE på en EXAKT tidsstämpel-
            // krock: sist besökt i `Concat` (alltså `b`s kopia i
            // `Merge(a, b)`, men `a`s i `Merge(b, a)`) vann alltid —
            // `Merge(a, b) != Merge(b, a)`, ett brott mot kommutativitets-
            // löftet (CodeRabbit-fynd). Avgörs nu istället av en stabil
            // jämförelse av VÄRDET självt när tidsstämplarna är exakt lika.
            var cmp = h.ModifiedAt.Seconds.CompareTo(existing.Host.ModifiedAt.Seconds);
            if (cmp > 0)
                newestHost[h.Id] = (h, null);
            else if (cmp == 0)
            {
                // Cache the winner's key so tied duplicates cannot repeatedly serialize it.
                var existingKey = existing.Key ?? TiebreakKey(existing.Host);
                var candidateKey = TiebreakKey(h);
                newestHost[h.Id] = string.CompareOrdinal(candidateKey, existingKey) > 0
                    ? (h, candidateKey)
                    : (existing.Host, existingKey);
            }
        }

        var tomb = new Dictionary<Guid, ReferenceDate>();
        foreach (var (id, t) in a.Tombstones.Concat(b.Tombstones))
        {
            if (!tomb.TryGetValue(id, out var existing) || t.Seconds > existing.Seconds)
                tomb[id] = t;
        }

        var liveHosts = new List<Host>();
        var finalTombstones = new Dictionary<Guid, ReferenceDate>();
        var allIds = newestHost.Keys.Union(tomb.Keys);
        foreach (var id in allIds)
        {
            var hasHost = newestHost.TryGetValue(id, out var entry);
            var host = entry.Host;
            var hasTomb = tomb.TryGetValue(id, out var deletedAt);
            if (hasHost && hasTomb)
            {
                if (deletedAt.Seconds >= host!.ModifiedAt.Seconds) finalTombstones[id] = deletedAt;
                else liveHosts.Add(host);
            }
            else if (hasHost)
            {
                liveHosts.Add(host!);
            }
            else if (hasTomb)
            {
                finalTombstones[id] = deletedAt;
            }
        }

        return new SyncState
        {
            Hosts = liveHosts.OrderBy(h => h.Alias, StringComparer.OrdinalIgnoreCase).ToList(),
            Tombstones = finalTombstones,
        };
    }
}
