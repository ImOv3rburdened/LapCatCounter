using Dalamud.Game.ClientState.Objects.SubKinds;
using System;
using System.Collections.Generic;
using System.Linq;
using static LapCatCounter.Configuration;

namespace LapCatCounter
{
    public sealed class LapTracker
    {
        private readonly Configuration cfg;

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
        public TimeSpan TotalLapTime => accumulatedLapTime + TimeSpan.FromSeconds(currentLapSeconds);
        public TimeSpan LongestLapTime => TimeSpan.FromSeconds(Math.Max(recordedLongestLapTime.TotalSeconds, currentLapSeconds));

        public LapTracker(Configuration cfg)
        {
            this.cfg = cfg;
            accumulatedLapTime = TimeSpan.FromSeconds(cfg.TotalLapSeconds);
            recordedLongestLapTime = TimeSpan.FromSeconds(cfg.LongestLapSeconds);
        }

        public int TotalLaps => cfg.People.Values.Sum(p => p.LapCount);
        public int UniquePeople => cfg.People.Count;
        public int TotalTimesISatInTheirLaps => cfg.People.Values.Sum(p => p.TimesISatInTheirLap);
        public int TotalTimesTheySatInMyLap => cfg.People.Values.Sum(p => p.TimesTheySatInMyLap);
        public TimeSpan TotalTimeISatInTheirLaps => TimeSpan.FromSeconds(cfg.People.Values.Sum(p => p.TimeISatInTheirLapSeconds));
        public TimeSpan TotalTimeTheySatInMyLap => TimeSpan.FromSeconds(cfg.People.Values.Sum(p => p.TimeTheySatInMyLapSeconds));

        public int GetCountFor(string key)
            => cfg.People.TryGetValue(key, out var s) ? s.LapCount : 0;

        private PersonStats GetOrCreatePerson(string key, string displayName)
        {
            if (!cfg.People.TryGetValue(key, out var stats))
            {
                stats = new PersonStats { DisplayName = displayName, Key = key };
                cfg.People[key] = stats;
            }

            if (!string.IsNullOrWhiteSpace(displayName))
                stats.DisplayName = displayName;

            stats.Key = key;
            return stats;
        }

        public void WriteTimeTotalsToConfig()
        {
            cfg.TotalLapSeconds = (long)TotalLapTime.TotalSeconds;
            cfg.LongestLapSeconds = (long)LongestLapTime.TotalSeconds;
        }

        public void ResetAllTotals()
        {
            ResetCurrent();
            accumulatedLapTime = TimeSpan.Zero;
            recordedLongestLapTime = TimeSpan.Zero;
            cfg.TotalLapSeconds = 0;
            cfg.LongestLapSeconds = 0;
        }

        public void RecalculateTotalsFromPeople()
        {
            EndLapSession();

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
            => cfg.People
                .Select(kvp =>
                {
                    kvp.Value.Key = kvp.Key;
                    return kvp.Value;
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

            var stats = GetOrCreatePerson(evidence.Key, evidence.DisplayName);
            stats.LapCount += 1;
            stats.LastLapUtc = DateTime.UtcNow;

            if (evidence.Role == LapInteractionRole.SittingInOtherLap)
                stats.TimesISatInTheirLap += 1;
            else if (evidence.Role == LapInteractionRole.OtherSittingInMyLap)
                stats.TimesTheySatInMyLap += 1;
        }

        private void EndLapSession()
        {
            if (!lapActive)
                return;

            var lapDuration = TimeSpan.FromSeconds(currentLapSeconds);
            accumulatedLapTime += lapDuration;

            if (lapDuration > recordedLongestLapTime)
                recordedLongestLapTime = lapDuration;

            var stats = GetOrCreatePerson(lapSessionKey, lapSessionDisplayName);
            var wholeSeconds = Math.Max(0L, (long)Math.Round(lapDuration.TotalSeconds));

            stats.TotalLapSeconds += wholeSeconds;
            if (wholeSeconds > stats.LongestLapSeconds)
                stats.LongestLapSeconds = wholeSeconds;

            if (lapSessionRole == LapInteractionRole.SittingInOtherLap)
                stats.TimeISatInTheirLapSeconds += wholeSeconds;
            else if (lapSessionRole == LapInteractionRole.OtherSittingInMyLap)
                stats.TimeTheySatInMyLapSeconds += wholeSeconds;

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
            var key = partner.Name.TextValue;
            if (string.IsNullOrWhiteSpace(key))
                return null;

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
                partner.Name.TextValue,
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
}

















