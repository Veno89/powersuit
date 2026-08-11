using System;
using System.Collections.Generic;

namespace Powersuit.Enemies
{
    public enum DemoEncounterStatus
    {
        WaitingForZone = 0,
        Active = 1,
        Intermission = 2,
        Complete = 3,
        Failed = 4
    }

    public sealed class DemoEncounterPhaseConfig
    {
        public DemoEncounterPhaseConfig(
            string phaseId,
            string displayName,
            int targetDefeats
        )
        {
            if (string.IsNullOrWhiteSpace(phaseId))
            {
                throw new ArgumentException("A phase id is required.", nameof(phaseId));
            }
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "A phase display name is required.",
                    nameof(displayName)
                );
            }
            if (targetDefeats <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetDefeats));
            }

            PhaseId = phaseId.Trim();
            DisplayName = displayName.Trim();
            TargetDefeats = targetDefeats;
        }

        public string PhaseId { get; }
        public string DisplayName { get; }
        public int TargetDefeats { get; }
    }

    /// <summary>
    /// Engine-independent phase ledger for the three-zone demo encounter.
    /// Unity adapters own positions and spawns; this state owns progression,
    /// exact defeat budgets, intermissions, failure, and restart semantics.
    /// </summary>
    public sealed class DemoEncounterState
    {
        private readonly DemoEncounterPhaseConfig[] phases;
        private readonly float intermissionSeconds;
        private float intermissionRemaining;

        public DemoEncounterState(
            IReadOnlyList<DemoEncounterPhaseConfig> phaseConfigs,
            float intermissionSeconds
        )
        {
            if (phaseConfigs == null || phaseConfigs.Count == 0)
            {
                throw new ArgumentException(
                    "At least one encounter phase is required.",
                    nameof(phaseConfigs)
                );
            }
            if (!IsFinite(intermissionSeconds) || intermissionSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(intermissionSeconds));
            }

            phases = new DemoEncounterPhaseConfig[phaseConfigs.Count];
            for (int index = 0; index < phases.Length; index++)
            {
                phases[index] = phaseConfigs[index] ??
                    throw new ArgumentException(
                        "Encounter phases cannot contain null entries.",
                        nameof(phaseConfigs)
                    );
            }
            this.intermissionSeconds = intermissionSeconds;
            ResetAll();
        }

        public DemoEncounterStatus Status { get; private set; }
        public int CurrentPhaseIndex { get; private set; }
        public DemoEncounterPhaseConfig CurrentPhase =>
            phases[Math.Min(CurrentPhaseIndex, phases.Length - 1)];
        public int PhaseCount => phases.Length;
        public int SpawnedThisPhase { get; private set; }
        public int DefeatedThisPhase { get; private set; }
        public int RemainingToSpawn => Math.Max(
            0,
            CurrentPhase.TargetDefeats - SpawnedThisPhase
        );
        public int RemainingToDefeat => Math.Max(
            0,
            CurrentPhase.TargetDefeats - DefeatedThisPhase
        );
        public float IntermissionRemaining => intermissionRemaining;
        public bool CanRequestSpawn =>
            Status == DemoEncounterStatus.Active && RemainingToSpawn > 0;

        public bool TryActivateCurrentPhase(bool playerInsideActivationArea)
        {
            if (
                Status != DemoEncounterStatus.WaitingForZone ||
                !playerInsideActivationArea
            )
            {
                return false;
            }

            SpawnedThisPhase = 0;
            DefeatedThisPhase = 0;
            Status = DemoEncounterStatus.Active;
            return true;
        }

        public int RegisterSpawned(int count)
        {
            if (Status != DemoEncounterStatus.Active || count <= 0)
            {
                return 0;
            }

            int accepted = Math.Min(count, RemainingToSpawn);
            SpawnedThisPhase += accepted;
            return accepted;
        }

        public bool RegisterDefeat()
        {
            if (
                Status != DemoEncounterStatus.Active ||
                DefeatedThisPhase >= SpawnedThisPhase
            )
            {
                return false;
            }

            DefeatedThisPhase++;
            return true;
        }

        public bool Advance(float deltaSeconds, bool noEnemiesRemaining)
        {
            if (!IsFinite(deltaSeconds) || deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            }

            if (
                Status == DemoEncounterStatus.Active &&
                RemainingToDefeat == 0 &&
                noEnemiesRemaining
            )
            {
                if (CurrentPhaseIndex >= phases.Length - 1)
                {
                    Status = DemoEncounterStatus.Complete;
                }
                else
                {
                    Status = DemoEncounterStatus.Intermission;
                    intermissionRemaining = intermissionSeconds;
                }
                return true;
            }

            if (Status != DemoEncounterStatus.Intermission)
            {
                return false;
            }

            intermissionRemaining = Math.Max(
                0f,
                intermissionRemaining - deltaSeconds
            );
            if (intermissionRemaining > 0f)
            {
                return false;
            }

            CurrentPhaseIndex++;
            SpawnedThisPhase = 0;
            DefeatedThisPhase = 0;
            Status = DemoEncounterStatus.WaitingForZone;
            return true;
        }

        public bool Fail()
        {
            if (
                Status == DemoEncounterStatus.Complete ||
                Status == DemoEncounterStatus.Failed
            )
            {
                return false;
            }

            Status = DemoEncounterStatus.Failed;
            return true;
        }

        public void RestartCurrentPhase()
        {
            SpawnedThisPhase = 0;
            DefeatedThisPhase = 0;
            intermissionRemaining = 0f;
            Status = DemoEncounterStatus.WaitingForZone;
        }

        public void ResetAll()
        {
            CurrentPhaseIndex = 0;
            SpawnedThisPhase = 0;
            DefeatedThisPhase = 0;
            intermissionRemaining = 0f;
            Status = DemoEncounterStatus.WaitingForZone;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
