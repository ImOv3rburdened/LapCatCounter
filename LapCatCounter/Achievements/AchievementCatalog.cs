using LapCatCounter.Statistics;
using System.Collections.Generic;

namespace LapCatCounter.Achievements;

public static class AchievementCatalog
{
    public static IReadOnlyList<AchievementDefinition> All { get; } = new AchievementDefinition[]
    {
        new("first_lapcat", "First LapCat", "Share your first lap.", 1, AchievementStatistic.TotalRawSessions),
        new("sits_5", "Getting Comfortable", "Earn credit for 5 lap sessions.", 5, AchievementStatistic.AchievementSessionCredits),
        new("sits_10", "Settle In", "Earn credit for 10 lap sessions.", 10, AchievementStatistic.AchievementSessionCredits),
        new("sits_50", "Take a Seat", "Earn credit for 50 lap sessions.", 50, AchievementStatistic.AchievementSessionCredits),
        new("sits_100", "Seat Warmer", "Earn credit for 100 lap sessions.", 100, AchievementStatistic.AchievementSessionCredits),
        new("sits_250", "Part of the Furniture", "Earn credit for 250 lap sessions.", 250, AchievementStatistic.AchievementSessionCredits),
        new("sits_500", "Reserved Seating", "Earn credit for 500 lap sessions.", 500, AchievementStatistic.AchievementSessionCredits),
        new("sits_1000", "Permanent Reservation", "Earn credit for 1,000 lap sessions.", 1_000, AchievementStatistic.AchievementSessionCredits),

        new("visits_10", "Getting Popular", "Welcome 10 credited LapCat visits.", 10, AchievementStatistic.CreditedVisits),
        new("visits_100", "Cat Furniture", "Welcome 100 credited LapCat visits.", 100, AchievementStatistic.CreditedVisits),
        new("visits_1000", "Certified Cat Furniture", "Welcome 1,000 credited LapCat visits.", 1_000, AchievementStatistic.CreditedVisits),
        new("visits_2500", "Neighborhood Landmark", "Welcome 2,500 credited LapCat visits.", 2_500, AchievementStatistic.CreditedVisits),

        new("unique_10", "Collector", "Meet 10 different LapCats.", 10, AchievementStatistic.UniqueLapCats),
        new("unique_25", "Full House", "Meet 25 different LapCats.", 25, AchievementStatistic.UniqueLapCats),
        new("unique_50", "Cat Café", "Meet 50 different LapCats.", 50, AchievementStatistic.UniqueLapCats),
        new("unique_100", "Everybody Knows Your Name", "Meet 100 different LapCats.", 100, AchievementStatistic.UniqueLapCats),
        new("unique_250", "The Long Guest List", "Meet 250 different LapCats.", 250, AchievementStatistic.UniqueLapCats),

        new("regular_customer", "Regular Customer", "See the same LapCat on 10 different days.", 10,
            CustomEvaluator: MostCreditedDaysForOneCharacter),
        new("old_friend", "Old Friend", "See the same LapCat on 25 different days.", 25,
            CustomEvaluator: MostCreditedDaysForOneCharacter),
        new("familiar_routine", "Familiar Routine", "See the same LapCat on 50 different days.", 50,
            CustomEvaluator: MostCreditedDaysForOneCharacter),
        new("inseparable", "Inseparable", "See the same LapCat on 100 different days.", 100,
            CustomEvaluator: MostCreditedDaysForOneCharacter),

        new("session_30m", "Cozy", "Keep one lap session going for 30 minutes.", 30 * 60,
            AchievementStatistic.LongestSessionSeconds, IsDuration: true),
        new("session_60m", "Professional Lap", "Keep one lap session going for an hour.", 60 * 60,
            AchievementStatistic.LongestSessionSeconds, IsDuration: true),
        new("session_2h", "No Plans Today", "Keep one lap session going for 2 hours.", 2 * 60 * 60,
            AchievementStatistic.LongestSessionSeconds, IsDuration: true),
        new("session_4h", "Still Here", "Keep one lap session going for 4 hours.", 4 * 60 * 60,
            AchievementStatistic.LongestSessionSeconds, IsDuration: true),
        new("session_8h", "All-Day Seat", "Keep one lap session going for 8 hours.", 8 * 60 * 60,
            AchievementStatistic.LongestSessionSeconds, IsDuration: true),

        new("time_5h", "Perfectly Cozy", "Spend 5 hours sharing laps.", 5 * 60 * 60,
            AchievementStatistic.TotalLapTimeSeconds, IsDuration: true),
        new("time_24h", "Around the Clock", "Spend a full day sharing laps.", 24 * 60 * 60,
            AchievementStatistic.TotalLapTimeSeconds, IsDuration: true),
        new("time_100h", "Lifetime Appointment", "Spend 100 hours sharing laps.", 100 * 60 * 60,
            AchievementStatistic.TotalLapTimeSeconds, IsDuration: true),
        new("time_250h", "Well Worn", "Spend 250 hours sharing laps.", 250 * 60 * 60,
            AchievementStatistic.TotalLapTimeSeconds, IsDuration: true),
        new("time_500h", "Second Home", "Spend 500 hours sharing laps.", 500 * 60 * 60,
            AchievementStatistic.TotalLapTimeSeconds, IsDuration: true),
        new("time_1000h", "Home Sweet Home", "Spend 1,000 hours sharing laps.", 1_000 * 60 * 60,
            AchievementStatistic.TotalLapTimeSeconds, IsDuration: true),

        new("streak_7", "LapCat Streak", "Share a lap on 7 days in a row.", 7,
            AchievementStatistic.LongestStreak),
        new("streak_30", "On a Roll", "Share a lap on 30 days in a row.", 30,
            AchievementStatistic.LongestStreak),
        new("streak_100", "No Days Off", "Share a lap on 100 days in a row.", 100,
            AchievementStatistic.LongestStreak),
        new("days_30", "Local Fixture", "Share a lap on 30 different days.", 30,
            AchievementStatistic.DaysWithLapCats),
        new("days_100", "Always Around", "Share a lap on 100 different days.", 100,
            AchievementStatistic.DaysWithLapCats),
        new("days_365", "A Year of LapCats", "Share a lap on 365 different days.", 365,
            AchievementStatistic.DaysWithLapCats),

        new("sat_in_25", "Lap Hopper", "Sit in another lap 25 times.", 25,
            AchievementStatistic.AchievementSessionsISatInTheirLaps),
        new("sat_in_100", "Seat Seeker", "Sit in another lap 100 times.", 100,
            AchievementStatistic.AchievementSessionsISatInTheirLaps),
        new("hosted_25", "Open Invitation", "Have someone sit in your lap 25 times.", 25,
            AchievementStatistic.AchievementSessionsTheySatInMyLap),
        new("hosted_100", "Favorite Seat", "Have someone sit in your lap 100 times.", 100,
            AchievementStatistic.AchievementSessionsTheySatInMyLap),
        new("sat_in_500", "Frequent Passenger", "Sit in another lap 500 times.", 500,
            AchievementStatistic.AchievementSessionsISatInTheirLaps),
        new("hosted_500", "House Favorite", "Have someone sit in your lap 500 times.", 500,
            AchievementStatistic.AchievementSessionsTheySatInMyLap),

        new("laps_visited_10", "Musical Chairs", "Sit in 10 different laps.", 10,
            CustomEvaluator: LapsVisited),
        new("visitors_hosted_10", "Full Lap", "Welcome 10 different LapCats to your lap.", 10,
            CustomEvaluator: VisitorsHosted),
        new("same_lap_10", "My Usual Spot", "Sit in the same LapCat's lap 10 times.", 10,
            CustomEvaluator: MostVisitsToOneLap),
        new("same_visitor_10", "Familiar Face", "Welcome the same LapCat to your lap 10 times.", 10,
            CustomEvaluator: MostVisitsFromOneLapCat),

        new("sat_in_time_5h", "Along for the Ride", "Spend 5 hours sitting in other laps.", 5 * 60 * 60,
            AchievementStatistic.TimeISatInTheirLapsSeconds, IsDuration: true),
        new("hosted_time_5h", "Make Yourself at Home", "Host LapCats for 5 hours.", 5 * 60 * 60,
            AchievementStatistic.TimeTheySatInMyLapSeconds, IsDuration: true),
    };

    private static long MostCreditedDaysForOneCharacter(LapCatStatistics statistics)
    {
        long maximum = 0;
        foreach (var character in statistics.Characters.Values)
            if (character.CreditedVisitDays.Count > maximum)
                maximum = character.CreditedVisitDays.Count;
        return maximum;
    }

    private static long LapsVisited(LapCatStatistics statistics)
    {
        long total = 0;
        foreach (var character in statistics.Characters.Values)
            if (character.TimesISatInTheirLap > 0)
                total++;
        return total;
    }

    private static long VisitorsHosted(LapCatStatistics statistics)
    {
        long total = 0;
        foreach (var character in statistics.Characters.Values)
            if (character.TimesTheySatInMyLap > 0)
                total++;
        return total;
    }

    private static long MostVisitsToOneLap(LapCatStatistics statistics)
    {
        long maximum = 0;
        foreach (var character in statistics.Characters.Values)
            if (character.AchievementSessionsISatInTheirLap > maximum)
                maximum = character.AchievementSessionsISatInTheirLap;
        return maximum;
    }

    private static long MostVisitsFromOneLapCat(LapCatStatistics statistics)
    {
        long maximum = 0;
        foreach (var character in statistics.Characters.Values)
            if (character.AchievementSessionsTheySatInMyLap > maximum)
                maximum = character.AchievementSessionsTheySatInMyLap;
        return maximum;
    }
}
