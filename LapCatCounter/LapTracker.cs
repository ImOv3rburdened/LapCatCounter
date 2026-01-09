using Dalamud.Game.ClientState.Objects.SubKinds;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LapCatCounter
{
    public sealed class LapTracker
    {
        private readonly Configuration cfg;

        public enum Mode
        {
            SatIn,
            SatOnYou,
        }

        private readonly Mode mode;
        public string CurrentLapKey { get; private set; } = "";
        public string CurrentLapDisplayName { get; private set; } = "";

        public string candidateKey = "";
        private float stableSeconds = 0f;
        private bool countedThisGate = false;
        private float noCandidateSeconds = 0f;

        public LapTracker(Configuration cfg, Mode mode = Mode.SatIn)
        {
            this.cfg = cfg;
            this.mode = mode;
        }

        public int TotalLaps => cfg.People.Values.Sum(p => p.LapCount);
        public int TotalSatOnYou => cfg.People.Values.Sum(p => p.SatOnYouCount);
        public int UniquePeople => cfg.People.Count;

        public int GetCountFor(string key)
            => cfg.People.TryGetValue(key, out var s) ? s.LapCount : 0;

        public int GetSatOnYouCountFor(string key)
            => cfg.People.TryGetValue(key, out var s) ? s.SatOnYouCount : 0;

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
            CurrentLapKey = "";
            CurrentLapDisplayName = "";
            candidateKey = "";
            stableSeconds = 0f;
            countedThisGate = false;
            noCandidateSeconds = 0f;
        }

        public void Update(
            float dt,
            bool gateActive,
            Vector3 localPos,
            IEnumerable<IPlayerCharacter> others,
            Action onCounted,
            out LapDebugInfo? debug)
            => Update(dt, gateActive, localPos, others, requiredObjectId: 0, onCounted, out debug);

            public void Update(
                float dt,
                bool gateActive,
                Vector3 localPos,
                IEnumerable<IPlayerCharacter> others,
                ulong requiredObjectId,
                Action onCounted,
                out LapDebugInfo? debug)
        {
            debug = null;

            if (!gateActive)
            {
                ResetCurrent();
                debug = new LapDebugInfo { Reason = "gateActive=false -> ResetCurrent()" };
                return;
            }

            IPlayerCharacter? best = null;
            float bestDist = float.MaxValue;
            string bestKey = "";

            IPlayerCharacter? nearest = null;
            float nearestDist = float.MaxValue;

            float zMin = mode == Mode.SatIn ? cfg.MinZAbove : -cfg.MaxZAbove;
            float zMax = mode == Mode.SatIn ? cfg.MaxZAbove : cfg.SatOnYouEqualZTolerance;

            foreach (var pc in others)
            {
                if (requiredObjectId != 0 && pc.GameObjectId != requiredObjectId)
                    continue;

                var name = pc.Name.TextValue;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                float dx = localPos.X - pc.Position.X;
                float dz = localPos.Z - pc.Position.Z;
                float dy = localPos.Y - pc.Position.Y;

                float horizontal = MathF.Sqrt(dx * dx + dz * dz);
                float dist3 = MathF.Sqrt(dx * dx + dz * dz + dy * dy);

                if (dist3 < nearestDist)
                {
                    nearestDist = dist3;
                    nearest = pc;
                }

                if (dist3 > cfg.Radius)
                    continue;

                if (MathF.Abs(dx) > cfg.XYThreshold)
                    continue;
                if (MathF.Abs(dz) > cfg.XYThreshold)
                    continue;

                if (dy < zMin || dy > zMax)
                    continue;

                if (dist3 < bestDist)
                {
                    best = pc;
                    bestDist = dist3;
                    bestKey = name;
                }
            }

            if (best is null)
            {
                noCandidateSeconds += dt;
                if (noCandidateSeconds >= 15.0f)
                    ResetCurrent();

                if (nearest != null)
                {
                    var lp = localPos;
                    var op = nearest.Position;

                    float dx = lp.X - op.X;
                    float dz = lp.Z - op.Z;
                    float dy = lp.Y - op.Y;

                    float horizontal = MathF.Sqrt(dx * dx + dx * dx);
                    float dist3 = MathF.Sqrt(dx * dx + dz * dz + dy * dy);

                    bool passR = dist3 <= cfg.Radius;
                    bool passXY = MathF.Abs(dx) <= cfg.XYThreshold && MathF.Abs(dz) <= cfg.XYThreshold;
                    bool passZ = dy >= zMin && dy <= zMax;

                    debug = new LapDebugInfo
                    {
                        CandidateName = nearest.Name.TextValue,
                        CandidateObjectId = nearest.GameObjectId,
                        Distance3D = dist3,
                        HorizontalXZ = horizontal,
                        Dx = dx,
                        Dz = dz,
                        Dy = dy,
                        PassRadius = passR,
                        PassXY = passXY,
                        PassZ = passZ,
                        StableSeconds = stableSeconds,
                        CountedThisGate = countedThisGate,
                        Reason = "No candidate passed thresholds (showing nearest)"
                    };
                }
                else
                {
                    debug = new LapDebugInfo { Reason = "No players in range to evaluate" };
                }
                return;
            }

            noCandidateSeconds = 0f;

            CurrentLapKey = bestKey;
            CurrentLapDisplayName = best.Name.TextValue;

            if (candidateKey == bestKey)
                stableSeconds += dt;
            else
            {
                candidateKey = bestKey;
                stableSeconds = 0f;
                countedThisGate = false;
            }

            {
                var lp = localPos;
                var op = best.Position;

                float dx = lp.X - op.X;
                float dz = lp.Z - op.Z;
                float dy = lp.Y - op.Y;

                float horizontal = MathF.Sqrt(dx * dx + dz * dz);
                float dist3 = MathF.Sqrt(dx * dx + dz * dz + dy * dy);

                bool passR = dist3 <= cfg.Radius;
                bool passXY = MathF.Abs(dx) <= cfg.XYThreshold && MathF.Abs(dz) <= cfg.XYThreshold;
                bool passZ = dy >= zMin && dy <= zMax;

                debug = new LapDebugInfo
                {
                    CandidateName = best.Name.TextValue,
                    CandidateObjectId = best.GameObjectId,
                    Distance3D = dist3,
                    HorizontalXZ = horizontal,
                    Dx = dx,
                    Dz = dz,
                    Dy = dy,
                    PassRadius = passR,
                    PassXY = passXY,
                    PassZ = passZ,
                    StableSeconds = stableSeconds,
                    CountedThisGate = countedThisGate,
                    Reason = "Best candidate selected"
                };
            }

            if (!countedThisGate && stableSeconds >= cfg.StableSecondsToCount)
            {
                var now = DateTime.UtcNow;

                if (!cfg.People.TryGetValue(bestKey, out var person))
                {
                    person = new Configuration.PersonStats
                    {
                        DisplayName = best.Name.TextValue,
                        LapCount = 0,
                        LastLapUtc = DateTime.MinValue,
                        SatOnYouCount = 0,
                        LastSatOnYouUtc = DateTime.MinValue,
                        Key = bestKey
                    };
                    cfg.People[bestKey] = person;
                }
                else
                {
                    person.DisplayName = best.Name.TextValue;
                    person.Key = bestKey;
                }

                var cooldown = TimeSpan.FromSeconds(Math.Max(0, cfg.CooldownSecondsPerPerson));

                if (mode == Mode.SatIn)
                {
                    if (person.LastLapUtc + cooldown <= now)
                    {
                        person.LapCount += 1;
                        person.LastLapUtc = now;
                        countedThisGate = true;
                        onCounted();
                    }
                }
            }
        }
    }
}

