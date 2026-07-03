namespace Fogos.Infrastructure.Notifications;

/// <summary>
/// Topic-name and condition helpers ported from the legacy <c>NotificationTool</c>. Topic names get
/// an environment prefix (<c>dev-</c> outside production); district codes are the concelho DICO with
/// <c>"00"</c> appended (the legacy district-level convention). Conditions are built from ≤5 topics.
/// </summary>
public sealed class FcmTopics(string prefix, bool legacyEnabled)
{
    /// <summary>Max topics FCM allows per condition string.</summary>
    public const int MaxTopicsPerCondition = 5;

    private static readonly IReadOnlyDictionary<string, string> LegacyDistrictNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Bragança"] = "Braganca",
        ["Évora"] = "Evora",
        ["Castelo Branco"] = "CasteloBranco",
        ["Santarém"] = "Santarem",
        ["Setúbal"] = "Setubal",
        ["Viana Do Castelo"] = "VianadoCastelo",
        ["Vila Real"] = "VilaReal",
    };

    /// <summary>Data-only proximity topic (the app computes distance locally). No concelho embedded.</summary>
    public string Nearby() => $"{prefix}incident-nearby";

    /// <summary>Per-incident topics; adds the "important" fan-out topic when requested.</summary>
    public IReadOnlyList<string> Incident(string id, bool includeImportant)
    {
        var topics = new List<string> { $"{prefix}incident-{id}" };
        if (legacyEnabled)
        {
            topics.Add($"{prefix}web-{id}");
            topics.Add($"{prefix}mobile-android-{id}");
            topics.Add($"{prefix}mobile-ios-{id}");
        }

        if (includeImportant)
            topics.AddRange(Important());

        return topics;
    }

    /// <summary>Big/important-incident topics.</summary>
    public IReadOnlyList<string> Important()
    {
        var topics = new List<string> { $"{prefix}incident-important" };
        if (legacyEnabled)
        {
            topics.Add($"{prefix}web-important");
            topics.Add($"{prefix}mobile-android-important");
            topics.Add($"{prefix}mobile-ios-important");
        }
        return topics;
    }

    /// <summary>New-fire district topics (district = concelho DICO + "00").</summary>
    public IReadOnlyList<string> NewFire(string dico, string? district)
    {
        var code = dico + "00";
        var topics = new List<string> { $"{prefix}district-{code}" };
        if (legacyEnabled)
        {
            if (district is not null)
                topics.Add(LegacyDistrictNames.GetValueOrDefault(district, district));
            topics.Add($"{prefix}web-{code}");
            topics.Add($"{prefix}mobile-android-{code}");
            topics.Add($"{prefix}mobile-ios-{code}");
        }
        return topics;
    }

    /// <summary>All-incidents (any nature) district topic.</summary>
    public IReadOnlyList<string> AllIncidents(string dico) => [$"{prefix}district-all-{dico}00"];

    /// <summary>Warnings broadcast topics.</summary>
    public IReadOnlyList<string> Warnings()
    {
        var topics = new List<string> { $"{prefix}warnings" };
        if (legacyEnabled)
        {
            topics.Add($"{prefix}mobile-android-warnings");
            topics.Add($"{prefix}mobile-ios-warnings");
            topics.Add($"{prefix}web-warnings");
        }
        return topics;
    }

    /// <summary>A single FCM condition clause for a set of topics: <c>'a' in topics || 'b' in topics</c>.</summary>
    public static string BuildCondition(IEnumerable<string> topics) =>
        string.Join(" || ", topics.Select(t => $"'{t}' in topics"));

    /// <summary>
    /// Splits topics into conditions of at most <see cref="MaxTopicsPerCondition"/> topics each.
    /// (Legacy silently dropped anything past the 5th; we send every topic across multiple conditions.)
    /// </summary>
    public static IReadOnlyList<string> ChunkConditions(IReadOnlyList<string> topics)
    {
        var conditions = new List<string>();
        for (var i = 0; i < topics.Count; i += MaxTopicsPerCondition)
        {
            var chunk = topics.Skip(i).Take(MaxTopicsPerCondition);
            conditions.Add(BuildCondition(chunk));
        }
        return conditions;
    }
}
