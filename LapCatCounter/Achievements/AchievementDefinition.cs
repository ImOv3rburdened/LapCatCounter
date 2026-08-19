using LapCatCounter.Statistics;
using System;

namespace LapCatCounter.Achievements;

public enum AchievementStatistic
{
    TotalRawSessions,
    AchievementSessionCredits,
    AchievementSessionsISatInTheirLaps,
    AchievementSessionsTheySatInMyLap,
    TimesISatInTheirLaps,
    TimesTheySatInMyLap,
    CreditedVisits,
    UniqueLapCats,
    TotalLapTimeSeconds,
    TimeISatInTheirLapsSeconds,
    TimeTheySatInMyLapSeconds,
    LongestSessionSeconds,
    DaysWithLapCats,
    LongestStreak,
}

public sealed record AchievementDefinition(
    string Id,
    string Name,
    string Description,
    long Target,
    AchievementStatistic? Statistic = null,
    Func<LapCatStatistics, long>? CustomEvaluator = null,
    bool IsDuration = false)
{
    public long GetProgress(LapCatStatistics statistics) => Math.Max(0, CustomEvaluator?.Invoke(statistics) ?? Statistic switch
    {
        AchievementStatistic.TotalRawSessions => statistics.TotalRawSessions,
        AchievementStatistic.AchievementSessionCredits => statistics.AchievementSessionCredits,
        AchievementStatistic.AchievementSessionsISatInTheirLaps => statistics.AchievementSessionsISatInTheirLaps,
        AchievementStatistic.AchievementSessionsTheySatInMyLap => statistics.AchievementSessionsTheySatInMyLap,
        AchievementStatistic.TimesISatInTheirLaps => statistics.TimesISatInTheirLaps,
        AchievementStatistic.TimesTheySatInMyLap => statistics.TimesTheySatInMyLap,
        AchievementStatistic.CreditedVisits => statistics.CreditedVisits,
        AchievementStatistic.UniqueLapCats => statistics.UniqueLapCats,
        AchievementStatistic.TotalLapTimeSeconds => statistics.TotalLapTimeSeconds,
        AchievementStatistic.TimeISatInTheirLapsSeconds => statistics.TimeISatInTheirLapsSeconds,
        AchievementStatistic.TimeTheySatInMyLapSeconds => statistics.TimeTheySatInMyLapSeconds,
        AchievementStatistic.LongestSessionSeconds => statistics.LongestSessionSeconds,
        AchievementStatistic.DaysWithLapCats => statistics.DaysWithLapCats,
        AchievementStatistic.LongestStreak => statistics.LongestStreak,
        _ => 0,
    });
}
