using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LapCatCounter.Statistics;

public sealed class LapCatStatistics
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public long TotalRawSessions { get; set; }
    public long AchievementSessionCredits { get; set; }
    public long AchievementSessionsISatInTheirLaps { get; set; }
    public long AchievementSessionsTheySatInMyLap { get; set; }
    public Dictionary<string, int> AchievementSessionCreditsByDay { get; set; } = new(StringComparer.Ordinal);
    public string LatestCreditedLocalDay { get; set; } = "";
    public long TimesISatInTheirLaps { get; set; }
    public long TimesTheySatInMyLap { get; set; }
    public long CreditedVisits { get; set; }
    public long TotalLapTimeSeconds { get; set; }
    public long TimeISatInTheirLapsSeconds { get; set; }
    public long TimeTheySatInMyLapSeconds { get; set; }
    public long LongestSessionSeconds { get; set; }
    public List<string> DistinctLapDays { get; set; } = new();
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateTime? FirstLapUtc { get; set; }
    public DateTime? MostRecentLapUtc { get; set; }
    public Dictionary<string, LapCatCharacterStatistics> Characters { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, DateTime> AchievementUnlocksUtc { get; set; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public int UniqueLapCats => Characters.Count;

    [JsonIgnore]
    public int DaysWithLapCats => DistinctLapDays.Count;
}

public sealed class LapCatCharacterStatistics
{
    public string DisplayName { get; set; } = "";
    public long TotalRawSessions { get; set; }
    public long AchievementSessionsISatInTheirLap { get; set; }
    public long AchievementSessionsTheySatInMyLap { get; set; }
    public Dictionary<string, int> AchievementSessionCreditsByDay { get; set; } = new(StringComparer.Ordinal);
    public long TimesISatInTheirLap { get; set; }
    public long TimesTheySatInMyLap { get; set; }
    public List<string> CreditedVisitDays { get; set; } = new();
    public long TotalLapTimeSeconds { get; set; }
    public long TimeISatInTheirLapSeconds { get; set; }
    public long TimeTheySatInMyLapSeconds { get; set; }
    public long LongestSessionSeconds { get; set; }
    public DateTime? FirstSeenUtc { get; set; }
    public DateTime? LastSeenUtc { get; set; }
}
