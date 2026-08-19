using LapCatCounter.Achievements;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LapCatCounter.Statistics;

public sealed class LapCatStatisticsManager
{
    private const int EnvelopeSchemaVersion = 1;
    private const string DayFormat = "yyyy-MM-dd";
    private readonly string filePath;
    private readonly byte[] integrityKey;
    private readonly Action<string, Exception?> logWarning;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = false };

    public LapCatStatistics Statistics { get; private set; }
    public AchievementManager Achievements { get; private set; }
    public event Action<AchievementDefinition>? AchievementUnlocked;

    public LapCatStatisticsManager(
        string configDirectory,
        Configuration configuration,
        Action<string, Exception?> logWarning,
        Action saveConfiguration)
    {
        this.logWarning = logWarning;
        Directory.CreateDirectory(configDirectory);
        filePath = Path.Combine(configDirectory, "LapCatStatistics.json");

        if (!TryReadSecret(configuration.StatisticsIntegritySecret, out integrityKey!))
        {
            integrityKey = RandomNumberGenerator.GetBytes(32);
            configuration.StatisticsIntegritySecret = Convert.ToBase64String(integrityKey);
            saveConfiguration();
        }

        var loaded = Load();
        var migrated = loaded is null;
        Statistics = loaded ?? MigrateLegacy(configuration);
        if (!migrated && Statistics.SchemaVersion < 2)
            MigrateDirectionalStatistics(Statistics, configuration);
        if (!migrated && Statistics.SchemaVersion < 3)
        {
            // Historical raw sessions do not contain enough information to apply the daily cap honestly.
            Statistics.AchievementSessionCredits = 0;
            Statistics.AchievementSessionCreditsByDay = new(StringComparer.Ordinal);
        }
        if (!migrated && Statistics.SchemaVersion < 4)
        {
            Statistics.AchievementSessionsISatInTheirLaps = 0;
            Statistics.AchievementSessionsTheySatInMyLap = 0;
            foreach (var character in Statistics.Characters.Values)
            {
                character.AchievementSessionsISatInTheirLap = 0;
                character.AchievementSessionsTheySatInMyLap = 0;
                character.AchievementSessionCreditsByDay = new(StringComparer.Ordinal);
            }
        }
        ValidateAndRepair(Statistics);
        Achievements = new AchievementManager(Statistics);
        ReconcileUnlocks();

        // Reconcile migrated or repaired data without producing a startup toast storm.
        var reconciliationTime = migrated
            ? Statistics.MostRecentLapUtc ?? DateTime.UtcNow.AddHours(-3)
            : DateTime.UtcNow;
        Achievements.Evaluate(reconciliationTime, suppressNotifications: true);
        Save();
    }

    public void SessionStarted(
        string characterKey,
        string displayName,
        LapInteractionRole role,
        DateTime startedUtc,
        DateTime localNow)
    {
        var nowUtc = NormalizeUtc(startedUtc);
        var character = GetOrCreateCharacter(characterKey, displayName);
        Statistics.TotalRawSessions++;
        character.TotalRawSessions++;
        if (role == LapInteractionRole.SittingInOtherLap)
        {
            Statistics.TimesISatInTheirLaps++;
            character.TimesISatInTheirLap++;
        }
        else if (role == LapInteractionRole.OtherSittingInMyLap)
        {
            Statistics.TimesTheySatInMyLap++;
            character.TimesTheySatInMyLap++;
        }
        Statistics.FirstLapUtc ??= nowUtc;
        character.FirstSeenUtc ??= nowUtc;
        Statistics.MostRecentLapUtc = nowUtc;
        character.LastSeenUtc = nowUtc;

        var day = localNow.Date.ToString(DayFormat, CultureInfo.InvariantCulture);
        var isCurrentOrLaterDay = string.IsNullOrEmpty(Statistics.LatestCreditedLocalDay)
                                  || string.CompareOrdinal(day, Statistics.LatestCreditedLocalDay) >= 0;
        if (isCurrentOrLaterDay && string.CompareOrdinal(day, Statistics.LatestCreditedLocalDay) > 0)
            Statistics.LatestCreditedLocalDay = day;

        Statistics.AchievementSessionCreditsByDay.TryGetValue(day, out var sessionCreditsToday);
        if (isCurrentOrLaterDay && sessionCreditsToday < 2)
        {
            Statistics.AchievementSessionCreditsByDay[day] = sessionCreditsToday + 1;
            Statistics.AchievementSessionCredits++;
            character.AchievementSessionCreditsByDay.TryGetValue(day, out var characterCreditsToday);
            character.AchievementSessionCreditsByDay[day] = Math.Min(2, characterCreditsToday + 1);
            if (role == LapInteractionRole.SittingInOtherLap)
            {
                Statistics.AchievementSessionsISatInTheirLaps++;
                character.AchievementSessionsISatInTheirLap++;
            }
            else if (role == LapInteractionRole.OtherSittingInMyLap)
            {
                Statistics.AchievementSessionsTheySatInMyLap++;
                character.AchievementSessionsTheySatInMyLap++;
            }
        }
        if (isCurrentOrLaterDay)
        {
            AddUniqueDay(Statistics.DistinctLapDays, day);
            if (AddUniqueDay(character.CreditedVisitDays, day))
                Statistics.CreditedVisits++;
        }
        RecalculateStreaks(localNow.Date);
        EvaluateAndSave(nowUtc);
    }

    public void SessionEnded(
        string characterKey,
        string displayName,
        LapInteractionRole role,
        TimeSpan duration,
        DateTime endedUtc)
    {
        var seconds = Math.Max(0L, (long)Math.Round(duration.TotalSeconds));
        var character = GetOrCreateCharacter(characterKey, displayName);
        Statistics.TotalLapTimeSeconds = SaturatingAdd(Statistics.TotalLapTimeSeconds, seconds);
        character.TotalLapTimeSeconds = SaturatingAdd(character.TotalLapTimeSeconds, seconds);
        if (role == LapInteractionRole.SittingInOtherLap)
        {
            Statistics.TimeISatInTheirLapsSeconds = SaturatingAdd(Statistics.TimeISatInTheirLapsSeconds, seconds);
            character.TimeISatInTheirLapSeconds = SaturatingAdd(character.TimeISatInTheirLapSeconds, seconds);
        }
        else if (role == LapInteractionRole.OtherSittingInMyLap)
        {
            Statistics.TimeTheySatInMyLapSeconds = SaturatingAdd(Statistics.TimeTheySatInMyLapSeconds, seconds);
            character.TimeTheySatInMyLapSeconds = SaturatingAdd(character.TimeTheySatInMyLapSeconds, seconds);
        }
        Statistics.LongestSessionSeconds = Math.Max(Statistics.LongestSessionSeconds, seconds);
        character.LongestSessionSeconds = Math.Max(character.LongestSessionSeconds, seconds);
        Statistics.MostRecentLapUtc = NormalizeUtc(endedUtc);
        character.LastSeenUtc = NormalizeUtc(endedUtc);
        EvaluateAndSave(endedUtc);
    }

    public void ResetAll()
    {
        Statistics = new LapCatStatistics();
        Achievements = new AchievementManager(Statistics);
        Save();
    }

    public void ResetCharacter(string characterKey)
    {
        if (!Statistics.Characters.Remove(characterKey))
            return;
        Statistics.TotalRawSessions = Statistics.Characters.Values.Sum(c => c.TotalRawSessions);
        Statistics.AchievementSessionCreditsByDay = Statistics.Characters.Values
            .SelectMany(character => character.AchievementSessionCreditsByDay)
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => Math.Min(2, group.Sum(entry => entry.Value)),
                StringComparer.Ordinal);
        Statistics.AchievementSessionCredits = Statistics.AchievementSessionCreditsByDay.Values.Sum(value => (long)value);
        Statistics.TimesISatInTheirLaps = Statistics.Characters.Values.Sum(c => c.TimesISatInTheirLap);
        Statistics.TimesTheySatInMyLap = Statistics.Characters.Values.Sum(c => c.TimesTheySatInMyLap);
        Statistics.AchievementSessionsISatInTheirLaps = Statistics.Characters.Values.Sum(c => c.AchievementSessionsISatInTheirLap);
        Statistics.AchievementSessionsTheySatInMyLap = Statistics.Characters.Values.Sum(c => c.AchievementSessionsTheySatInMyLap);
        Statistics.CreditedVisits = Statistics.Characters.Values.Sum(c => (long)c.CreditedVisitDays.Count);
        Statistics.TotalLapTimeSeconds = Statistics.Characters.Values.Sum(c => c.TotalLapTimeSeconds);
        Statistics.TimeISatInTheirLapsSeconds = Statistics.Characters.Values.Sum(c => c.TimeISatInTheirLapSeconds);
        Statistics.TimeTheySatInMyLapSeconds = Statistics.Characters.Values.Sum(c => c.TimeTheySatInMyLapSeconds);
        Statistics.LongestSessionSeconds = Statistics.Characters.Values.DefaultIfEmpty().Max(c => c?.LongestSessionSeconds ?? 0);
        Statistics.DistinctLapDays = Statistics.Characters.Values.SelectMany(c => c.CreditedVisitDays)
            .Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal).ToList();
        Statistics.FirstLapUtc = Statistics.Characters.Values.Where(c => c.FirstSeenUtc.HasValue).Select(c => c.FirstSeenUtc).Min();
        Statistics.MostRecentLapUtc = Statistics.Characters.Values.Where(c => c.LastSeenUtc.HasValue).Select(c => c.LastSeenUtc).Max();
        RecalculateStreaks(DateTime.Now.Date);
        ReconcileUnlocks();
        Save();
    }

    public void Save()
    {
        ValidateAndRepair(Statistics);
        var payload = JsonSerializer.Serialize(Statistics, jsonOptions);
        var envelope = new StatisticsEnvelope
        {
            SchemaVersion = EnvelopeSchemaVersion,
            Payload = payload,
            Integrity = ComputeIntegrity(payload),
        };
        var tempPath = filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(envelope, jsonOptions), Encoding.UTF8);
        File.Move(tempPath, filePath, true);
    }

    private void EvaluateAndSave(DateTime nowUtc)
    {
        foreach (var achievement in Achievements.Evaluate(NormalizeUtc(nowUtc)))
            AchievementUnlocked?.Invoke(achievement);
        Save();
    }

    private void ReconcileUnlocks()
    {
        var knownIds = Achievements.Definitions.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in Statistics.AchievementUnlocksUtc.Keys.Where(id => !knownIds.Contains(id)).ToArray())
            Statistics.AchievementUnlocksUtc.Remove(id);

        foreach (var definition in Achievements.Definitions)
            if (definition.GetProgress(Statistics) < definition.Target)
                Statistics.AchievementUnlocksUtc.Remove(definition.Id);
    }

    private LapCatCharacterStatistics GetOrCreateCharacter(string key, string displayName)
    {
        if (!Statistics.Characters.ContainsKey(key)
            && Statistics.Characters.TryGetValue(displayName, out var legacyCharacter))
        {
            Statistics.Characters.Remove(displayName);
            Statistics.Characters[key] = legacyCharacter;
        }

        if (!Statistics.Characters.TryGetValue(key, out var character))
        {
            character = new LapCatCharacterStatistics();
            Statistics.Characters[key] = character;
        }
        if (!string.IsNullOrWhiteSpace(displayName))
            character.DisplayName = displayName;
        return character;
    }

    private LapCatStatistics? Load()
    {
        if (!File.Exists(filePath))
            return null;
        try
        {
            var envelope = JsonSerializer.Deserialize<StatisticsEnvelope>(File.ReadAllText(filePath, Encoding.UTF8), jsonOptions);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.Payload))
                return null;
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(ComputeIntegrity(envelope.Payload)),
                    Encoding.UTF8.GetBytes(envelope.Integrity ?? "")))
                logWarning("[LapCatCounter] Statistics integrity validation failed. Valid fields will be repaired and re-signed; tracking will continue.", null);
            return JsonSerializer.Deserialize<LapCatStatistics>(envelope.Payload, jsonOptions);
        }
        catch (Exception ex)
        {
            logWarning("[LapCatCounter] Could not load statistics. Legacy data will be preserved where possible.", ex);
            return null;
        }
    }

    private static LapCatStatistics MigrateLegacy(Configuration configuration)
    {
        var statistics = new LapCatStatistics
        {
            TotalRawSessions = configuration.People.Values.Sum(p => Math.Max(0, p.LapCount)),
            TimesISatInTheirLaps = configuration.People.Values.Sum(p => Math.Max(0, p.TimesISatInTheirLap)),
            TimesTheySatInMyLap = configuration.People.Values.Sum(p => Math.Max(0, p.TimesTheySatInMyLap)),
            TotalLapTimeSeconds = Math.Max(0, configuration.TotalLapSeconds),
            TimeISatInTheirLapsSeconds = configuration.People.Values.Sum(p => Math.Max(0, p.TimeISatInTheirLapSeconds)),
            TimeTheySatInMyLapSeconds = configuration.People.Values.Sum(p => Math.Max(0, p.TimeTheySatInMyLapSeconds)),
            LongestSessionSeconds = Math.Max(0, configuration.LongestLapSeconds),
        };
        foreach (var (key, legacy) in configuration.People)
        {
            statistics.Characters[key] = new LapCatCharacterStatistics
            {
                DisplayName = legacy.DisplayName,
                TotalRawSessions = Math.Max(0, legacy.LapCount),
                TimesISatInTheirLap = Math.Max(0, legacy.TimesISatInTheirLap),
                TimesTheySatInMyLap = Math.Max(0, legacy.TimesTheySatInMyLap),
                TotalLapTimeSeconds = Math.Max(0, legacy.TotalLapSeconds),
                TimeISatInTheirLapSeconds = Math.Max(0, legacy.TimeISatInTheirLapSeconds),
                TimeTheySatInMyLapSeconds = Math.Max(0, legacy.TimeTheySatInMyLapSeconds),
                LongestSessionSeconds = Math.Max(0, legacy.LongestLapSeconds),
                LastSeenUtc = legacy.LastLapUtc == DateTime.MinValue ? null : NormalizeUtc(legacy.LastLapUtc),
            };
        }
        return statistics;
    }

    private static void MigrateDirectionalStatistics(LapCatStatistics statistics, Configuration configuration)
    {
        statistics.TimesISatInTheirLaps = configuration.People.Values.Sum(p => Math.Max(0, p.TimesISatInTheirLap));
        statistics.TimesTheySatInMyLap = configuration.People.Values.Sum(p => Math.Max(0, p.TimesTheySatInMyLap));
        statistics.TimeISatInTheirLapsSeconds = configuration.People.Values.Sum(p => Math.Max(0, p.TimeISatInTheirLapSeconds));
        statistics.TimeTheySatInMyLapSeconds = configuration.People.Values.Sum(p => Math.Max(0, p.TimeTheySatInMyLapSeconds));

        foreach (var (key, legacy) in configuration.People)
        {
            var character = statistics.Characters.TryGetValue(key, out var exact)
                ? exact
                : statistics.Characters.Values.FirstOrDefault(c =>
                    string.Equals(c.DisplayName, legacy.DisplayName, StringComparison.Ordinal));
            if (character is null)
                continue;
            character.TimesISatInTheirLap = Math.Max(0, legacy.TimesISatInTheirLap);
            character.TimesTheySatInMyLap = Math.Max(0, legacy.TimesTheySatInMyLap);
            character.TimeISatInTheirLapSeconds = Math.Max(0, legacy.TimeISatInTheirLapSeconds);
            character.TimeTheySatInMyLapSeconds = Math.Max(0, legacy.TimeTheySatInMyLapSeconds);
        }
    }

    private void ValidateAndRepair(LapCatStatistics value)
    {
        value.SchemaVersion = LapCatStatistics.CurrentSchemaVersion;
        value.TotalRawSessions = Math.Max(0, value.TotalRawSessions);
        value.AchievementSessionCreditsByDay ??= new(StringComparer.Ordinal);
        foreach (var day in value.AchievementSessionCreditsByDay.Keys.ToArray())
        {
            if (!ParseDay(day).HasValue
                || string.CompareOrdinal(day, DateTime.Now.ToString(DayFormat, CultureInfo.InvariantCulture)) > 0)
            {
                value.AchievementSessionCreditsByDay.Remove(day);
                continue;
            }
            value.AchievementSessionCreditsByDay[day] = Math.Clamp(value.AchievementSessionCreditsByDay[day], 0, 2);
        }
        value.AchievementSessionCredits = value.AchievementSessionCreditsByDay.Values.Sum(value => (long)value);
        var latestStoredDay = value.AchievementSessionCreditsByDay.Keys
            .Concat(value.DistinctLapDays)
            .Where(day => ParseDay(day).HasValue)
            .DefaultIfEmpty("")
            .Max(StringComparer.Ordinal) ?? "";
        var localToday = DateTime.Now.ToString(DayFormat, CultureInfo.InvariantCulture);
        value.LatestCreditedLocalDay = ParseDay(value.LatestCreditedLocalDay).HasValue
                                       && string.CompareOrdinal(value.LatestCreditedLocalDay, localToday) <= 0
            ? string.CompareOrdinal(value.LatestCreditedLocalDay, latestStoredDay) >= 0
                ? value.LatestCreditedLocalDay
                : latestStoredDay
            : latestStoredDay;
        value.AchievementSessionsISatInTheirLaps = Math.Clamp(
            value.AchievementSessionsISatInTheirLaps, 0, value.AchievementSessionCredits);
        value.AchievementSessionsTheySatInMyLap = Math.Clamp(
            value.AchievementSessionsTheySatInMyLap, 0, value.AchievementSessionCredits);
        value.TimesISatInTheirLaps = Math.Max(0, value.TimesISatInTheirLaps);
        value.TimesTheySatInMyLap = Math.Max(0, value.TimesTheySatInMyLap);
        value.TimesISatInTheirLaps = Math.Min(value.TimesISatInTheirLaps, value.TotalRawSessions);
        value.TimesTheySatInMyLap = Math.Min(value.TimesTheySatInMyLap, value.TotalRawSessions);
        value.CreditedVisits = Math.Max(0, value.CreditedVisits);
        value.TotalLapTimeSeconds = Math.Max(0, value.TotalLapTimeSeconds);
        value.TimeISatInTheirLapsSeconds = Math.Max(0, value.TimeISatInTheirLapsSeconds);
        value.TimeTheySatInMyLapSeconds = Math.Max(0, value.TimeTheySatInMyLapSeconds);
        value.TimeISatInTheirLapsSeconds = Math.Min(value.TimeISatInTheirLapsSeconds, value.TotalLapTimeSeconds);
        value.TimeTheySatInMyLapSeconds = Math.Min(value.TimeTheySatInMyLapSeconds, value.TotalLapTimeSeconds);
        value.LongestSessionSeconds = Math.Clamp(value.LongestSessionSeconds, 0, value.TotalLapTimeSeconds);
        value.DistinctLapDays ??= new();
        value.Characters ??= new(StringComparer.Ordinal);
        value.AchievementUnlocksUtc ??= new(StringComparer.Ordinal);
        value.DistinctLapDays = NormalizeDays(value.DistinctLapDays);
        value.FirstLapUtc = ValidateTimestamp(value.FirstLapUtc);
        value.MostRecentLapUtc = ValidateTimestamp(value.MostRecentLapUtc);

        foreach (var key in value.Characters.Keys.ToArray())
        {
            if (string.IsNullOrWhiteSpace(key) || value.Characters[key] is null)
            {
                value.Characters.Remove(key);
                continue;
            }
            var character = value.Characters[key];
            character.DisplayName ??= "";
            character.TotalRawSessions = Math.Max(0, character.TotalRawSessions);
            character.AchievementSessionCreditsByDay ??= new(StringComparer.Ordinal);
            foreach (var day in character.AchievementSessionCreditsByDay.Keys.ToArray())
            {
                if (!ParseDay(day).HasValue
                    || string.CompareOrdinal(day, DateTime.Now.ToString(DayFormat, CultureInfo.InvariantCulture)) > 0)
                {
                    character.AchievementSessionCreditsByDay.Remove(day);
                    continue;
                }
                character.AchievementSessionCreditsByDay[day] = Math.Clamp(character.AchievementSessionCreditsByDay[day], 0, 2);
            }
            var characterAchievementCredits = character.AchievementSessionCreditsByDay.Values.Sum(value => (long)value);
            character.AchievementSessionsISatInTheirLap = Math.Clamp(
                character.AchievementSessionsISatInTheirLap, 0, characterAchievementCredits);
            character.AchievementSessionsTheySatInMyLap = Math.Clamp(
                character.AchievementSessionsTheySatInMyLap, 0, characterAchievementCredits);
            character.TimesISatInTheirLap = Math.Max(0, character.TimesISatInTheirLap);
            character.TimesTheySatInMyLap = Math.Max(0, character.TimesTheySatInMyLap);
            character.TimesISatInTheirLap = Math.Min(character.TimesISatInTheirLap, character.TotalRawSessions);
            character.TimesTheySatInMyLap = Math.Min(character.TimesTheySatInMyLap, character.TotalRawSessions);
            character.TotalLapTimeSeconds = Math.Max(0, character.TotalLapTimeSeconds);
            character.TimeISatInTheirLapSeconds = Math.Max(0, character.TimeISatInTheirLapSeconds);
            character.TimeTheySatInMyLapSeconds = Math.Max(0, character.TimeTheySatInMyLapSeconds);
            character.TimeISatInTheirLapSeconds = Math.Min(character.TimeISatInTheirLapSeconds, character.TotalLapTimeSeconds);
            character.TimeTheySatInMyLapSeconds = Math.Min(character.TimeTheySatInMyLapSeconds, character.TotalLapTimeSeconds);
            character.LongestSessionSeconds = Math.Clamp(character.LongestSessionSeconds, 0, character.TotalLapTimeSeconds);
            character.CreditedVisitDays = NormalizeDays(character.CreditedVisitDays ?? new());
            character.FirstSeenUtc = ValidateTimestamp(character.FirstSeenUtc);
            character.LastSeenUtc = ValidateTimestamp(character.LastSeenUtc);
        }
        foreach (var id in value.AchievementUnlocksUtc.Keys.ToArray())
            value.AchievementUnlocksUtc[id] = ValidateTimestamp(value.AchievementUnlocksUtc[id]) ?? DateTime.UtcNow;
        value.CreditedVisits = value.Characters.Values.Sum(c => (long)c.CreditedVisitDays.Count);
        RecalculateStreaks(DateTime.Now.Date);
    }

    private void RecalculateStreaks(DateTime localToday)
    {
        var days = Statistics.DistinctLapDays
            .Select(ParseDay)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToArray();
        var longest = 0;
        var run = 0;
        DateTime? previous = null;
        foreach (var day in days)
        {
            run = previous.HasValue && day == previous.Value.AddDays(1) ? run + 1 : 1;
            longest = Math.Max(longest, run);
            previous = day;
        }
        Statistics.LongestStreak = longest;
        if (days.Length == 0 || days[^1] < localToday.AddDays(-1) || days[^1] > localToday)
            Statistics.CurrentStreak = 0;
        else
        {
            var current = 1;
            for (var i = days.Length - 1; i > 0 && days[i - 1] == days[i].AddDays(-1); i--)
                current++;
            Statistics.CurrentStreak = current;
        }
    }

    private static bool AddUniqueDay(List<string> days, string day)
    {
        if (days.Contains(day, StringComparer.Ordinal))
            return false;
        days.Add(day);
        return true;
    }

    private static List<string> NormalizeDays(IEnumerable<string> days)
        => days.Select(ParseDay).Where(d => d.HasValue).Select(d => d!.Value.ToString(DayFormat, CultureInfo.InvariantCulture))
            .Where(d => string.CompareOrdinal(d, DateTime.Now.ToString(DayFormat, CultureInfo.InvariantCulture)) <= 0)
            .Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal).ToList();

    private static DateTime? ValidateTimestamp(DateTime? value)
    {
        if (!value.HasValue || value.Value == DateTime.MinValue)
            return null;
        var utc = NormalizeUtc(value.Value);
        return utc <= DateTime.UtcNow.AddDays(1) ? utc : null;
    }

    private static DateTime? ParseDay(string value)
        => DateTime.TryParseExact(value, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day.Date : null;

    private string ComputeIntegrity(string payload)
        => Convert.ToBase64String(HMACSHA256.HashData(integrityKey, Encoding.UTF8.GetBytes(payload)));

    private static bool TryReadSecret(string value, out byte[] secret)
    {
        try { secret = Convert.FromBase64String(value); return secret.Length >= 32; }
        catch { secret = Array.Empty<byte>(); return false; }
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static long SaturatingAdd(long left, long right)
        => left > long.MaxValue - right ? long.MaxValue : left + right;

    private sealed class StatisticsEnvelope
    {
        public int SchemaVersion { get; set; }
        public string Payload { get; set; } = "";
        public string Integrity { get; set; } = "";
    }
}
