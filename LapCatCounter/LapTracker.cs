using Dalamud.Game.ClientState.Objects.SubKinds;
using System;
using System.Collections.Generic;
using System.Linq;
using LapCatCounter.Statistics;

namespace LapCatCounter
{
    public sealed class LapTracker
    {
        private readonly Configuration cfg;
        private LapCatStatisticsManager? statisticsManager;

        private string pendingKey = string.Empty;
        private string pendingDisplayName = string.Empty;
        private ulong pendingObjectId;
        private LapInteractionRole pendingRole = LapInteractionRole.None;
        private float pendingStableSeconds;

        private bool lapActive;
        private string lapSessionKey = string.Empty;
        private string lapSessionDisplayName = string.Empty;
        private ulong lapSessionObjectId;
        private LapInteractionRole lapSessionRole = LapInteractionRole.None;
        private float currentLapSeconds;
        private float missingEvidenceSeconds;
        private DateTime? currentLapStartedUtc;

        private TimeSpan accumulatedLapTime;
        private TimeSpan recordedLongestLapTime;

        public string CurrentLapKey { get; private set; } = string.Empty;
        public string CurrentLapDisplayName { get; private set; } = string.Empty;
        public string? CurrentBestCandidateKey { get; private set; }
        public LapInteractionRole CurrentRole { get; private set; } = LapInteractionRole.None;
        public LapInteractionStatus CurrentStatus { get; private set; } = LapInteractionStatus.None;
        public DateTime? CurrentLapStartedUtc => currentLapStartedUtc;
        public TimeSpan CurrentLapTime => TimeSpan.FromSeconds(currentLapSeconds);
        public TimeSpan TotalLapTime => TimeSpan.FromSeconds(statisticsManager?.Statistics.TotalLapTimeSeconds ?? accumulatedLapTime.TotalSeconds)
                                        + TimeSpan.FromSeconds(currentLapSeconds);
        public TimeSpan LongestLapTime => TimeSpan.FromSeconds(Math.Max(
            statisticsManager?.Statistics.LongestSessionSeconds ?? recordedLongestLapTime.TotalSeconds,
            currentLapSeconds));
        public event Action<LapSessionStarted>? SessionStarted;
        public event Action<LapSessionEnded>? SessionEnded;

        public LapTracker(Configuration cfg)
        {
            this.cfg = cfg;
            accumulatedLapTime = TimeSpan.FromSeconds(cfg.TotalLapSeconds);
            recordedLongestLapTime = TimeSpan.FromSeconds(cfg.LongestLapSeconds);
        }

        public int TotalLaps => ClampToInt(statisticsManager?.Statistics.TotalRawSessions ?? cfg.People.Values.Sum(p => (long)p.LapCount));
        public int UniquePeople => statisticsManager?.Statistics.UniqueLapCats ?? cfg.People.Count;
        public int TotalTimesISatInTheirLaps => ClampToInt(statisticsManager?.Statistics.TimesISatInTheirLaps ?? cfg.People.Values.Sum(p => (long)p.TimesISatInTheirLap));
        public int TotalTimesTheySatInMyLap => ClampToInt(statisticsManager?.Statistics.TimesTheySatInMyLap ?? cfg.People.Values.Sum(p => (long)p.TimesTheySatInMyLap));
        public TimeSpan TotalTimeISatInTheirLaps => TimeSpan.FromSeconds(statisticsManager?.Statistics.TimeISatInTheirLapsSeconds ?? cfg.People.Values.Sum(p => p.TimeISatInTheirLapSeconds));
        public TimeSpan TotalTimeTheySatInMyLap => TimeSpan.FromSeconds(statisticsManager?.Statistics.TimeTheySatInMyLapSeconds ?? cfg.People.Values.Sum(p => p.TimeTheySatInMyLapSeconds));

        public void AttachStatistics(LapCatStatisticsManager manager) => statisticsManager = manager;

        public int GetCountFor(string key)
            => statisticsManager?.Statistics.Characters.TryGetValue(key, out var s) == true
                ? ClampToInt(s.TotalRawSessions)
                : cfg.People.TryGetValue(key, out var legacy) ? legacy.LapCount : 0;

        public void WriteTimeTotalsToConfig()
        {
            if (statisticsManager is not null)
                return;
            cfg.TotalLapSeconds = (long)TotalLapTime.TotalSeconds;
            cfg.LongestLapSeconds = (long)LongestLapTime.TotalSeconds;
        }

        public void ResetAllTotals()
        {
            ResetCurrent();
            if (statisticsManager is not null)
                return;
            accumulatedLapTime = TimeSpan.Zero;
            recordedLongestLapTime = TimeSpan.Zero;
            cfg.TotalLapSeconds = 0;
            cfg.LongestLapSeconds = 0;
        }

        public void RecalculateTotalsFromPeople()
        {
            EndLapSession();
            if (statisticsManager is not null)
                return;

            long totalSeconds = 0;
            long longestSeconds = 0;

            foreach (var s in cfg.People.Values)
            {
                totalSeconds += s.TotalLapSeconds;
                if (s.LongestLapSeconds > longestSeconds)
                    longestSeconds = s.LongestLapSeconds;
            }

            accumulatedLapTime = TimeSpan.FromSeconds(totalSeconds);
            recordedLongestLapTime = TimeSpan.FromSeconds(longestSeconds);
            cfg.TotalLapSeconds = totalSeconds;
            cfg.LongestLapSeconds = longestSeconds;
        }

        public IReadOnlyList<Configuration.PersonStats> TopPeople(int take = 200)
            => statisticsManager is null
                ? cfg.People
                .Select(kvp =>
                {
                    kvp.Value.Key = kvp.Key;
                    return kvp.Value;
                })
                .OrderByDescending(p => p.LapCount)
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(take)
                .ToList()
                : statisticsManager.Statistics.Characters
                    .Select(kvp => new Configuration.PersonStats
                    {
                        Key = kvp.Key,
                        DisplayName = kvp.Value.DisplayName,
                        LapCount = ClampToInt(kvp.Value.TotalRawSessions),
                        LastLapUtc = kvp.Value.LastSeenUtc ?? DateTime.MinValue,
                        TotalLapSeconds = kvp.Value.TotalLapTimeSeconds,
                        LongestLapSeconds = kvp.Value.LongestSessionSeconds,
                        TimesISatInTheirLap = ClampToInt(kvp.Value.TimesISatInTheirLap),
                        TimesTheySatInMyLap = ClampToInt(kvp.Value.TimesTheySatInMyLap),
                        TimeISatInTheirLapSeconds = kvp.Value.TimeISatInTheirLapSeconds,
                        TimeTheySatInMyLapSeconds = kvp.Value.TimeTheySatInMyLapSeconds,
                    })
                    .OrderByDescending(p => p.LapCount)
                    .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Take(take)
                    .ToList();

        public void ResetCurrent()
        {
            EndLapSession();
            ClearPending();
            CurrentLapKey = string.Empty;
            CurrentLapDisplayName = string.Empty;
            CurrentBestCandidateKey = null;
            CurrentRole = LapInteractionRole.None;
            CurrentStatus = LapInteractionStatus.None;
            currentLapStartedUtc = null;
        }

        public void Update(
            float dt,
            IPlayerCharacter local,
            IEnumerable<IPlayerCharacter> others,
            EmoteHook emoteHook,
            Action onSessionStarted,
            out LapDebugInfo? debug)
        {
            debug = null;
            CurrentBestCandidateKey = null;

            var othersList = others.ToList();
            var othersByObjectId = othersList.ToDictionary(p => p.GameObjectId, p => p);
            CandidateEvidence? evidence = null;

            if (lapActive && othersByObjectId.TryGetValue(lapSessionObjectId, out var activePartner))
                evidence = TryCreateActiveSessionEvidence(local, activePartner, lapSessionRole, "Locked session partner");

            if (!lapActive && pendingRole != LapInteractionRole.None && othersByObjectId.TryGetValue(pendingObjectId, out var pendingPartner))
                evidence ??= TryCreateRangeEvidence(local, pendingPartner, pendingRole, "Pending session partner");

            if (!lapActive && evidence is null)
                evidence = FindEvidenceFromSitOrder(local, othersList, emoteHook);

            if (evidence is { } candidate)
            {
                CurrentBestCandidateKey = candidate.Key;
                SyncCurrent(candidate.Key, candidate.DisplayName, candidate.Role);
                debug = candidate.ToDebugInfo(this);
            }
            else
            {
                debug = CreateIdleDebug(local, "No sit-order partner found in range");
            }

            if (lapActive)
            {
                if (evidence is { } activeEvidence && MatchesActive(activeEvidence))
                {
                    missingEvidenceSeconds = 0f;
                    currentLapSeconds += dt;
                    SyncCurrent(activeEvidence.Key, activeEvidence.DisplayName, activeEvidence.Role);
                    CurrentStatus = LapInteractionStatus.Active;
                    debug = activeEvidence.ToDebugInfo(this);
                    return;
                }

                missingEvidenceSeconds += dt;
                currentLapSeconds += dt;
                CurrentStatus = LapInteractionStatus.Ending;

                if (missingEvidenceSeconds >= cfg.SessionBreakGraceSeconds)
                {
                    EndLapSession();
                    if (evidence is null)
                    {
                        ClearPending();
                        CurrentLapKey = string.Empty;
                        CurrentLapDisplayName = string.Empty;
                        CurrentBestCandidateKey = null;
                        CurrentRole = LapInteractionRole.None;
                        CurrentStatus = LapInteractionStatus.None;
                        currentLapStartedUtc = null;
                    }
                }

                if (debug.HasValue)
                {
                    var d = debug.Value;
                    d.MissingSeconds = missingEvidenceSeconds;
                    d.CurrentStatus = CurrentStatus.ToString();
                    debug = d;
                }

                return;
            }

            if (evidence is not { } newEvidence)
            {
                ClearPending();
                CurrentLapKey = string.Empty;
                CurrentLapDisplayName = string.Empty;
                CurrentRole = LapInteractionRole.None;
                CurrentStatus = LapInteractionStatus.None;
                currentLapStartedUtc = null;
                return;
            }

            if (IsSamePending(newEvidence))
            {
                pendingStableSeconds += dt;
            }
            else
            {
                SetPending(newEvidence);
            }

            CurrentStatus = LapInteractionStatus.Starting;
            currentLapStartedUtc = DateTime.UtcNow - TimeSpan.FromSeconds(pendingStableSeconds);

            if (debug.HasValue)
            {
                var d = debug.Value;
                d.StableSeconds = pendingStableSeconds;
                d.CurrentStatus = CurrentStatus.ToString();
                debug = d;
            }

            if (pendingStableSeconds < cfg.StableSecondsToCount)
                return;

            StartLapSession(newEvidence, DateTime.UtcNow - TimeSpan.FromSeconds(pendingStableSeconds));
            if (newEvidence.Role == LapInteractionRole.SittingInOtherLap)
                emoteHook.ConsumeRecentSitForInstigator(local.GameObjectId);
            else if (newEvidence.Role == LapInteractionRole.OtherSittingInMyLap)
                emoteHook.ConsumeRecentSitForInstigator(newEvidence.ObjectId);
            currentLapSeconds = pendingStableSeconds;
            onSessionStarted();
            CurrentStatus = LapInteractionStatus.Active;
            ClearPending();

            if (debug.HasValue)
            {
                var d = debug.Value;
                d.CurrentStatus = CurrentStatus.ToString();
                debug = d;
            }
        }

        private CandidateEvidence? FindEvidenceFromSitOrder(IPlayerCharacter local, IEnumerable<IPlayerCharacter> others, EmoteHook emoteHook)
        {
            bool localUsedSitRecently = emoteHook.TryGetRecentSitForInstigator(
                local.GameObjectId,
                cfg.SitEmoteId,
                cfg.GroundSitEmoteId,
                cfg.EmoteHookSeconds,
                out _);

            if (IsSeatedAnchor(local, emoteHook)
                && emoteHook.TryGetRecentObservedLapEvent(cfg.SitEmoteId, cfg.GroundSitEmoteId, cfg.EmoteHookSeconds, out var observed)
                && !observed.InstigatorIsLocal
                && observed.InstigatorObjectId != 0)
            {
                var partner = others.FirstOrDefault(p => p.GameObjectId == observed.InstigatorObjectId);
                if (partner != null)
                    return TryCreateRangeEvidence(local, partner, LapInteractionRole.OtherSittingInMyLap, "Partner sat near seated local");
            }

            if (localUsedSitRecently)
            {
                var partner = FindClosestSeatedAnchorPartner(local, others, emoteHook);
                if (partner is { } seatedPartner)
                    return TryCreateRangeEvidence(local, seatedPartner, LapInteractionRole.SittingInOtherLap, "Local sat near seated partner");
            }

            return null;
        }

        private IPlayerCharacter? FindClosestSeatedAnchorPartner(IPlayerCharacter local, IEnumerable<IPlayerCharacter> others, EmoteHook emoteHook)
        {
            IPlayerCharacter? best = null;
            float bestDistance = float.MaxValue;

            foreach (var other in others)
            {
                if (TryCreateRangeEvidence(local, other, LapInteractionRole.SittingInOtherLap, "Candidate in range") is not { } match)
                    continue;

                if (!IsSeatedAnchor(other, emoteHook))
                    continue;

                if (match.Distance3D < bestDistance)
                {
                    bestDistance = match.Distance3D;
                    best = other;
                }
            }

            return best;
        }

        private bool IsSeatedAnchor(IPlayerCharacter player, EmoteHook emoteHook)
            => ActorStateReader.IsLapCompatibleState(player);

        private void StartLapSession(CandidateEvidence evidence, DateTime startedUtc)
        {
            lapActive = true;
            lapSessionKey = evidence.Key;
            lapSessionDisplayName = evidence.DisplayName;
            lapSessionObjectId = evidence.ObjectId;
            lapSessionRole = evidence.Role;
            currentLapStartedUtc = startedUtc;
            missingEvidenceSeconds = 0f;
            SyncCurrent(evidence.Key, evidence.DisplayName, evidence.Role);

            SessionStarted?.Invoke(new LapSessionStarted(evidence.Key, evidence.DisplayName, evidence.Role, startedUtc));
        }

        private void EndLapSession()
        {
            if (!lapActive)
                return;

            var lapDuration = TimeSpan.FromSeconds(currentLapSeconds);
            SessionEnded?.Invoke(new LapSessionEnded(
                lapSessionKey,
                lapSessionDisplayName,
                lapSessionRole,
                lapDuration,
                DateTime.UtcNow));

            lapActive = false;
            lapSessionKey = string.Empty;
            lapSessionDisplayName = string.Empty;
            lapSessionObjectId = 0;
            lapSessionRole = LapInteractionRole.None;
            currentLapSeconds = 0f;
            missingEvidenceSeconds = 0f;
            currentLapStartedUtc = null;
        }

        private CandidateEvidence? TryCreateRangeEvidence(
            IPlayerCharacter local,
            IPlayerCharacter partner,
            LapInteractionRole role,
            string reason)
        {
            var displayName = partner.Name.TextValue;
            if (string.IsNullOrWhiteSpace(displayName))
                return null;
            var key = $"{displayName}@{partner.HomeWorld.RowId}";

            var dx = local.Position.X - partner.Position.X;
            var dz = local.Position.Z - partner.Position.Z;
            var dy = local.Position.Y - partner.Position.Y;
            var horizontal = MathF.Sqrt(dx * dx + dz * dz);
            var dist3 = MathF.Sqrt(dx * dx + dz * dz + dy * dy);

            const float oldBestMaxDist3 = 0.40f;
            float effectiveRadius = MathF.Min(cfg.Radius, oldBestMaxDist3);
            bool passRadius = dist3 <= effectiveRadius;
            bool passXY = MathF.Abs(dx) <= cfg.StartXYThreshold && MathF.Abs(dz) <= cfg.StartXYThreshold;
            bool passVertical = MathF.Abs(dy) <= cfg.MaxZAbove;

            if (!passRadius || !passXY || !passVertical)
                return null;

            var localMode = ActorStateReader.Describe(local);
            var partnerMode = ActorStateReader.Describe(partner);
            var localStateOk = ActorStateReader.IsLapCompatibleState(local);
            var partnerStateOk = ActorStateReader.IsLapCompatibleState(partner);

            return new CandidateEvidence(
                key,
                displayName,
                partner.GameObjectId,
                role,
                dist3,
                horizontal,
                dy,
                passRadius,
                passXY,
                passVertical,
                localStateOk,
                partnerStateOk,
                localMode,
                partnerMode,
                reason);
        }
        private CandidateEvidence? TryCreateActiveSessionEvidence(
            IPlayerCharacter local,
            IPlayerCharacter partner,
            LapInteractionRole role,
            string reason)
        {
            var evidence = TryCreateRangeEvidence(local, partner, role, reason);
            if (evidence is not { } candidate)
                return null;

            if (!candidate.LocalStateOk || !candidate.PartnerStateOk)
                return null;

            bool passTightHorizontal = MathF.Abs(local.Position.X - partner.Position.X) <= cfg.ActiveXYThreshold
                && MathF.Abs(local.Position.Z - partner.Position.Z) <= cfg.ActiveXYThreshold;

            if (!passTightHorizontal)
                return null;

            return candidate;
        }

        private LapDebugInfo CreateIdleDebug(IPlayerCharacter local, string reason)
        {
            return new LapDebugInfo
            {
                CandidateName = string.Empty,
                CandidateObjectId = 0,
                LocalMode = ActorStateReader.Describe(local),
                PartnerMode = "None",
                LocalStateOk = ActorStateReader.IsLapCompatibleState(local),
                PartnerStateOk = false,
                CurrentRole = CurrentRole.ToString(),
                CurrentStatus = CurrentStatus.ToString(),
                StableSeconds = pendingStableSeconds,
                MissingSeconds = missingEvidenceSeconds,
                Reason = reason,
            };
        }

        private void SyncCurrent(string key, string displayName, LapInteractionRole role)
        {
            CurrentLapKey = key;
            CurrentLapDisplayName = displayName;
            CurrentRole = role;
        }

        private bool MatchesActive(CandidateEvidence evidence)
            => lapActive
               && evidence.Role == lapSessionRole
               && string.Equals(evidence.Key, lapSessionKey, StringComparison.Ordinal)
               && evidence.ObjectId == lapSessionObjectId;

        private bool IsSamePending(CandidateEvidence evidence)
            => pendingRole == evidence.Role
               && string.Equals(pendingKey, evidence.Key, StringComparison.Ordinal)
               && pendingObjectId == evidence.ObjectId;

        private void SetPending(CandidateEvidence evidence)
        {
            pendingKey = evidence.Key;
            pendingDisplayName = evidence.DisplayName;
            pendingObjectId = evidence.ObjectId;
            pendingRole = evidence.Role;
            pendingStableSeconds = 0f;
            SyncCurrent(evidence.Key, evidence.DisplayName, evidence.Role);
        }

        private void ClearPending()
        {
            pendingKey = string.Empty;
            pendingDisplayName = string.Empty;
            pendingObjectId = 0;
            pendingRole = LapInteractionRole.None;
            pendingStableSeconds = 0f;
        }

        private static int ClampToInt(long value) => (int)Math.Clamp(value, 0, int.MaxValue);

        private readonly record struct CandidateEvidence(
            string Key,
            string DisplayName,
            ulong ObjectId,
            LapInteractionRole Role,
            float Distance3D,
            float HorizontalXZ,
            float VerticalDelta,
            bool PassRadius,
            bool PassXY,
            bool PassVertical,
            bool LocalStateOk,
            bool PartnerStateOk,
            string LocalMode,
            string PartnerMode,
            string Reason)
        {
            public LapDebugInfo ToDebugInfo(LapTracker tracker)
            {
                return new LapDebugInfo
                {
                    CandidateName = DisplayName,
                    CandidateObjectId = ObjectId,
                    Distance3D = Distance3D,
                    HorizontalXZ = HorizontalXZ,
                    VerticalDelta = VerticalDelta,
                    PassRadius = PassRadius,
                    PassXY = PassXY,
                    PassVertical = PassVertical,
                    LocalStateOk = LocalStateOk,
                    PartnerStateOk = PartnerStateOk,
                    LocalMode = LocalMode,
                    PartnerMode = PartnerMode,
                    StableSeconds = tracker.pendingStableSeconds,
                    MissingSeconds = tracker.missingEvidenceSeconds,
                    CurrentRole = Role.ToString(),
                    CurrentStatus = tracker.CurrentStatus.ToString(),
                    Reason = Reason,
                };
            }
        }
    }

    public readonly record struct LapSessionStarted(
        string CharacterKey,
        string DisplayName,
        LapInteractionRole Role,
        DateTime StartedUtc);

    public readonly record struct LapSessionEnded(
        string CharacterKey,
        string DisplayName,
        LapInteractionRole Role,
        TimeSpan Duration,
        DateTime EndedUtc);
}










