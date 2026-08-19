using LapCatCounter.Statistics;
using System;
using System.Collections.Generic;

namespace LapCatCounter.Achievements;

public sealed class AchievementManager
{
    private readonly LapCatStatistics statistics;

    public AchievementManager(LapCatStatistics statistics) => this.statistics = statistics;

    public IReadOnlyList<AchievementDefinition> Definitions => AchievementCatalog.All;

    public bool IsUnlocked(AchievementDefinition definition)
        => statistics.AchievementUnlocksUtc.ContainsKey(definition.Id);

    public DateTime? GetUnlockedUtc(AchievementDefinition definition)
        => statistics.AchievementUnlocksUtc.TryGetValue(definition.Id, out var value) ? value : null;

    public long GetProgress(AchievementDefinition definition) => definition.GetProgress(statistics);

    public List<AchievementDefinition> Evaluate(DateTime nowUtc, bool suppressNotifications = false)
    {
        var newlyUnlocked = new List<AchievementDefinition>();
        foreach (var definition in Definitions)
        {
            if (IsUnlocked(definition) || definition.GetProgress(statistics) < definition.Target)
                continue;

            statistics.AchievementUnlocksUtc[definition.Id] = nowUtc;
            if (!suppressNotifications)
                newlyUnlocked.Add(definition);
        }

        return newlyUnlocked;
    }
}
