using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace LapCatCounter;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    public float Radius { get; set; } = 1.15f;
    public float XYThreshold { get; set; } = 0.32f;
    public float StartXYThreshold { get; set; } = 0.40f;
    public float ActiveXYThreshold { get; set; } = 0.40f;
    public float MinZAbove { get; set; } = 0.05f;
    public float MaxZAbove { get; set; } = 0.70f;
    public float StableSecondsToCount { get; set; } = 1.0f;
    public int CooldownSecondsPerPerson { get; set; } = 30;
    public bool OverlayEnabled { get; set; } = true;
    public float HeadOffsetZ { get; set; } = 2.2f;
    public bool RequireSitEmote { get; set; } = true;
    public ushort SitEmoteId { get; set; } = 50;
    public ushort GroundSitEmoteId { get; set; } = 51;
    public float EmoteHookSeconds { get; set; } = 6.0f;
    public float SessionBreakGraceSeconds { get; set; } = 3.0f;
    public long TotalLapSeconds { get; set; } = 0;
    public long LongestLapSeconds { get; set; } = 0;

    public Dictionary<string, PersonStats> People { get; set; } = new();

    public sealed class PersonStats
    {
        public string DisplayName { get; set; } = "";
        public int LapCount { get; set; } = 0;
        public DateTime LastLapUtc { get; set; } = DateTime.MinValue;
        public string Key { get; set; } = "";
        public long TotalLapSeconds { get; set; } = 0;
        public long LongestLapSeconds { get; set; } = 0;
        public int TimesISatInTheirLap { get; set; } = 0;
        public int TimesTheySatInMyLap { get; set; } = 0;
        public long TimeISatInTheirLapSeconds { get; set; } = 0;
        public long TimeTheySatInMyLapSeconds { get; set; } = 0;
    }
}









