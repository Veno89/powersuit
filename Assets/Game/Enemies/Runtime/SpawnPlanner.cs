using System;
using System.Collections.Generic;
using Powersuit.Combat;

namespace Powersuit.Enemies
{
    /// <summary>
    /// Small platform-stable pseudo-random generator. Unlike System.Random, its
    /// sequence is explicit and can be reset exactly for repeatable bug reports.
    /// </summary>
    public sealed class DeterministicSpawnRandom
    {
        private const uint ZeroSeedFallback = 0x6D2B79F5u;
        private uint state;

        public DeterministicSpawnRandom(uint seed)
        {
            Reset(seed);
        }

        public uint State => state;

        public void Reset(uint seed)
        {
            state = seed == 0u ? ZeroSeedFallback : seed;
        }

        public uint NextUInt()
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return value;
        }

        public int NextIndex(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            }

            uint bound = (uint)exclusiveMaximum;
            uint threshold = unchecked(0u - bound) % bound;
            uint value;

            do
            {
                value = NextUInt();
            }
            while (value < threshold);

            return (int)(value % bound);
        }

        public double NextUnitDouble()
        {
            return NextUInt() / 4294967296d;
        }

        public float NextFloat(float minimum, float maximum)
        {
            if (
                !EnemyAttackProfile.IsFinite(minimum) ||
                !EnemyAttackProfile.IsFinite(maximum) ||
                maximum < minimum
            )
            {
                throw new ArgumentOutOfRangeException(nameof(maximum));
            }

            return minimum + (float)(NextUnitDouble() * (maximum - minimum));
        }
    }

    public readonly struct SpawnRequest
    {
        public SpawnRequest(
            EnemyArchetypeConfig archetype,
            SpawnPointCandidate point,
            int candidateIndex,
            float initialAttackDelaySeconds,
            float spawnProtectionSeconds
        )
        {
            Archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
            Point = point;
            CandidateIndex = candidateIndex;
            InitialAttackDelaySeconds = initialAttackDelaySeconds;
            SpawnProtectionSeconds = spawnProtectionSeconds;
        }

        public EnemyArchetypeConfig Archetype { get; }
        public SpawnPointCandidate Point { get; }
        public int CandidateIndex { get; }
        public float InitialAttackDelaySeconds { get; }
        public float SpawnProtectionSeconds { get; }
    }

    public readonly struct SpawnPlanResult
    {
        public SpawnPlanResult(int count, int requestedGroupSize, float threatSpent)
        {
            Count = count;
            RequestedGroupSize = requestedGroupSize;
            ThreatSpent = threatSpent;
        }

        public int Count { get; }
        public int RequestedGroupSize { get; }
        public float ThreatSpent { get; }
        public bool HasSpawns => Count > 0;
    }

    /// <summary>
    /// Seeded weighted selector. Results are written into a caller-owned buffer
    /// so an ordinary spawn interval does not need a temporary list or plan
    /// allocation after the adapter has warmed its pools.
    /// </summary>
    public sealed class SpawnPlanner
    {
        private const float ThreatEpsilon = 0.0001f;

        private readonly SpawnDirectorConfig config;
        private readonly DeterministicSpawnRandom random;
        private int activeEnemyCap;
        private uint initialSeed;

        public SpawnPlanner(
            SpawnDirectorConfig config,
            uint nonDeterministicSessionSeed = 0xA341316Cu
        )
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            activeEnemyCap = config.ActiveEnemyCap;
            initialSeed = config.UseDeterministicSeed
                ? config.DeterministicSeed
                : nonDeterministicSessionSeed;
            random = new DeterministicSpawnRandom(initialSeed);
        }

        public uint RandomState => random.State;
        public int ActiveEnemyCap => activeEnemyCap;

        public void SetActiveEnemyCap(int value)
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            activeEnemyCap = value;
        }

        public void Reset()
        {
            random.Reset(initialSeed);
        }

        public void Reset(uint seed)
        {
            initialSeed = seed;
            random.Reset(seed);
        }

        public SpawnPlanResult FillPlan(
            IReadOnlyList<EnemySpawnEntry> entries,
            IReadOnlyList<SpawnPointCandidate> candidates,
            CombatVector3 playerPosition,
            int activeEnemyCount,
            SpawnRequest[] output
        )
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            if (activeEnemyCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(activeEnemyCount));
            }

            int capacityRemaining = Math.Max(0, activeEnemyCap - activeEnemyCount);
            if (
                capacityRemaining == 0 ||
                output.Length == 0 ||
                entries.Count == 0 ||
                candidates.Count == 0
            )
            {
                return default;
            }

            int groupSizeRange = config.MaximumGroupSize - config.MinimumGroupSize + 1;
            int requestedGroupSize =
                config.MinimumGroupSize + random.NextIndex(groupSizeRange);
            requestedGroupSize = Math.Min(requestedGroupSize, capacityRemaining);
            requestedGroupSize = Math.Min(requestedGroupSize, output.Length);

            int written = 0;
            float threatRemaining = config.GroupThreatBudget;
            float threatSpent = 0f;

            while (written < requestedGroupSize)
            {
                int entryIndex = SelectEntryIndex(
                    entries,
                    candidates,
                    playerPosition,
                    threatRemaining,
                    output,
                    written
                );

                if (entryIndex < 0)
                {
                    break;
                }

                EnemyArchetypeConfig archetype = entries[entryIndex].Archetype;
                int candidateIndex = SelectCandidateIndex(
                    archetype,
                    candidates,
                    playerPosition,
                    output,
                    written
                );

                if (candidateIndex < 0)
                {
                    break;
                }

                float initialAttackDelay = CreateInitialAttackDelay(
                    written,
                    requestedGroupSize
                );
                output[written] = new SpawnRequest(
                    archetype,
                    candidates[candidateIndex],
                    candidateIndex,
                    initialAttackDelay,
                    config.SpawnProtectionSeconds
                );
                written++;
                threatRemaining = Math.Max(0f, threatRemaining - archetype.ThreatCost);
                threatSpent += archetype.ThreatCost;
            }

            return new SpawnPlanResult(written, requestedGroupSize, threatSpent);
        }

        private int SelectEntryIndex(
            IReadOnlyList<EnemySpawnEntry> entries,
            IReadOnlyList<SpawnPointCandidate> candidates,
            CombatVector3 playerPosition,
            float threatRemaining,
            SpawnRequest[] output,
            int outputCount
        )
        {
            double totalWeight = 0d;

            for (int index = 0; index < entries.Count; index++)
            {
                EnemySpawnEntry entry = entries[index];
                if (
                    IsSelectable(
                        entry,
                        candidates,
                        playerPosition,
                        threatRemaining,
                        output,
                        outputCount
                    )
                )
                {
                    totalWeight += entry.EffectiveWeight;
                }
            }

            if (totalWeight <= 0d)
            {
                return -1;
            }

            double selection = random.NextUnitDouble() * totalWeight;
            int fallback = -1;

            for (int index = 0; index < entries.Count; index++)
            {
                EnemySpawnEntry entry = entries[index];
                if (
                    !IsSelectable(
                        entry,
                        candidates,
                        playerPosition,
                        threatRemaining,
                        output,
                        outputCount
                    )
                )
                {
                    continue;
                }

                fallback = index;
                if (selection < entry.EffectiveWeight)
                {
                    return index;
                }

                selection -= entry.EffectiveWeight;
            }

            return fallback;
        }

        private bool IsSelectable(
            EnemySpawnEntry entry,
            IReadOnlyList<SpawnPointCandidate> candidates,
            CombatVector3 playerPosition,
            float threatRemaining,
            SpawnRequest[] output,
            int outputCount
        )
        {
            return
                entry != null &&
                entry.IsEnabled &&
                entry.Archetype.ThreatCost <= threatRemaining + ThreatEpsilon &&
                CountEligibleCandidates(
                    entry.Archetype,
                    candidates,
                    playerPosition,
                    output,
                    outputCount
                ) > 0;
        }

        private int SelectCandidateIndex(
            EnemyArchetypeConfig archetype,
            IReadOnlyList<SpawnPointCandidate> candidates,
            CombatVector3 playerPosition,
            SpawnRequest[] output,
            int outputCount
        )
        {
            int eligibleCount = CountEligibleCandidates(
                archetype,
                candidates,
                playerPosition,
                output,
                outputCount
            );

            if (eligibleCount == 0)
            {
                return -1;
            }

            int selectedOrdinal = random.NextIndex(eligibleCount);

            for (int index = 0; index < candidates.Count; index++)
            {
                if (
                    !IsCandidateEligible(
                        archetype,
                        candidates[index],
                        index,
                        playerPosition,
                        output,
                        outputCount
                    )
                )
                {
                    continue;
                }

                if (selectedOrdinal == 0)
                {
                    return index;
                }

                selectedOrdinal--;
            }

            return -1;
        }

        private int CountEligibleCandidates(
            EnemyArchetypeConfig archetype,
            IReadOnlyList<SpawnPointCandidate> candidates,
            CombatVector3 playerPosition,
            SpawnRequest[] output,
            int outputCount
        )
        {
            int count = 0;

            for (int index = 0; index < candidates.Count; index++)
            {
                if (
                    IsCandidateEligible(
                        archetype,
                        candidates[index],
                        index,
                        playerPosition,
                        output,
                        outputCount
                    )
                )
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsCandidateEligible(
            EnemyArchetypeConfig archetype,
            SpawnPointCandidate candidate,
            int candidateIndex,
            CombatVector3 playerPosition,
            SpawnRequest[] output,
            int outputCount
        )
        {
            for (int outputIndex = 0; outputIndex < outputCount; outputIndex++)
            {
                if (output[outputIndex].CandidateIndex == candidateIndex)
                {
                    return false;
                }
            }

            return SpawnEligibility.Evaluate(
                archetype,
                candidate,
                playerPosition,
                config.PlayerSafeRadius,
                config.AvoidCameraView
            ) == SpawnEligibilityFailure.None;
        }

        private float CreateInitialAttackDelay(int index, int groupSize)
        {
            float maximum = config.MaximumInitialAttackStaggerSeconds;
            if (maximum <= 0f)
            {
                return 0f;
            }

            float slotMinimum = maximum * index / groupSize;
            float slotMaximum = maximum * (index + 1) / groupSize;
            return random.NextFloat(slotMinimum, slotMaximum);
        }
    }
}
