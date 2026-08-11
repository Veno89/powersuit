using System;
using Powersuit.Enemies;
using Powersuit.Enemies.UnityAdapters;
using UnityEngine;

[Serializable]
public sealed class PowerSuitEncounterSpawnEntry
{
    [SerializeField] private string archetypeId = "patrol-rifleman";
    [SerializeField, Min(1)] private int count = 1;

    public string ArchetypeId => archetypeId;
    public int Count => Mathf.Max(1, count);

    public void Configure(string id, int authoredCount)
    {
        archetypeId = string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
        count = Mathf.Max(1, authoredCount);
    }
}

[Serializable]
public sealed class PowerSuitEncounterPhase
{
    [SerializeField] private string phaseId = "causeway";
    [SerializeField] private string displayName = "CENTRAL CAUSEWAY";
    [SerializeField] private Vector3 activationCenter;
    [SerializeField, Min(0.5f)] private float activationRadius = 8f;
    [SerializeField] private string[] allowedZoneIds = Array.Empty<string>();
    [SerializeField] private PowerSuitEncounterSpawnEntry[] spawnEntries =
        Array.Empty<PowerSuitEncounterSpawnEntry>();

    public string PhaseId => phaseId;
    public string DisplayName => displayName;
    public Vector3 ActivationCenter => activationCenter;
    public float ActivationRadius => Mathf.Max(0.5f, activationRadius);
    public string[] AllowedZoneIds => allowedZoneIds;
    public PowerSuitEncounterSpawnEntry[] SpawnEntries => spawnEntries;
    public int TargetDefeats
    {
        get
        {
            int total = 0;
            if (spawnEntries != null)
            {
                for (int index = 0; index < spawnEntries.Length; index++)
                {
                    if (spawnEntries[index] != null)
                    {
                        total += spawnEntries[index].Count;
                    }
                }
            }
            return total;
        }
    }

    public void Configure(
        string id,
        string title,
        Vector3 center,
        float radius,
        string[] zoneIds,
        PowerSuitEncounterSpawnEntry[] entries
    )
    {
        phaseId = string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
        displayName = string.IsNullOrWhiteSpace(title)
            ? string.Empty
            : title.Trim();
        activationCenter = center;
        activationRadius = Mathf.Max(0.5f, radius);
        allowedZoneIds = zoneIds != null
            ? (string[])zoneIds.Clone()
            : Array.Empty<string>();
        spawnEntries = entries != null
            ? (PowerSuitEncounterSpawnEntry[])entries.Clone()
            : Array.Empty<PowerSuitEncounterSpawnEntry>();
    }

    public DemoEncounterPhaseConfig CreateRuntimeConfig()
    {
        return new DemoEncounterPhaseConfig(
            phaseId,
            displayName,
            TargetDefeats
        );
    }
}

/// <summary>
/// Turns the generated three-zone sandbox into a short deterministic combat
/// slice. Each phase activates by proximity, owns an exact enemy budget and
/// zone filter, and exposes allocation-stable objective text to the HUD.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(170)]
public sealed class PowerSuitEncounterDirector : MonoBehaviour
{
    [SerializeField] private EnemySpawnDirector spawnDirector;
    [SerializeField] private PowerSuitEncounterPhase[] phases =
        Array.Empty<PowerSuitEncounterPhase>();
    [SerializeField, Min(0f)] private float intermissionSeconds = 2.5f;
    [SerializeField, Min(0.05f)] private float spawnRequestInterval = 0.35f;
    [SerializeField, Min(1)] private int maximumSpawnRequestSize = 2;
    [SerializeField, Min(0f)] private float encounterSafeRadius = 5f;

    private Transform player;
    private PlayerHealth playerHealth;
    private DemoEncounterState state;
    private int spawnEntryIndex;
    private int spawnedFromCurrentEntry;
    private float spawnRequestRemaining;
    private bool subscribed;
    private string objectiveText = string.Empty;

    public EnemySpawnDirector SpawnDirector => spawnDirector;
    public DemoEncounterStatus Status => state?.Status ??
        DemoEncounterStatus.WaitingForZone;
    public int CurrentPhaseIndex => state?.CurrentPhaseIndex ?? 0;
    public int PhaseCount => state?.PhaseCount ?? phases.Length;
    public int DefeatedThisPhase => state?.DefeatedThisPhase ?? 0;
    public int TargetDefeats => state?.CurrentPhase.TargetDefeats ?? 0;
    public float ProgressNormalized => TargetDefeats > 0
        ? Mathf.Clamp01(DefeatedThisPhase / (float)TargetDefeats)
        : 0f;
    public string ObjectiveText => objectiveText;
    public int ObjectiveRevision { get; private set; }

    public event Action ObjectiveChanged;

    private void OnEnable()
    {
        if (player != null && state != null)
        {
            Subscribe();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (
            state == null ||
            player == null ||
            spawnDirector == null ||
            !spawnDirector.IsInitialized
        )
        {
            return;
        }

        DemoEncounterStatus previousStatus = state.Status;
        int previousPhase = state.CurrentPhaseIndex;
        int previousDefeats = state.DefeatedThisPhase;

        if (state.Status == DemoEncounterStatus.WaitingForZone)
        {
            PowerSuitEncounterPhase phase = phases[state.CurrentPhaseIndex];
            bool inside = Vector3.Distance(
                player.position,
                phase.ActivationCenter
            ) <= phase.ActivationRadius;
            if (state.TryActivateCurrentPhase(inside))
            {
                BeginCurrentPhase();
            }
        }
        else if (state.Status == DemoEncounterStatus.Active)
        {
            spawnRequestRemaining = Mathf.Max(
                0f,
                spawnRequestRemaining - Time.deltaTime
            );
            if (spawnRequestRemaining <= 0f && state.CanRequestSpawn)
            {
                TryRequestNextSpawns();
            }
        }

        bool noEnemies = spawnDirector.ActiveInstanceCount == 0 &&
            spawnDirector.PendingSpawnCount == 0;
        state.Advance(Time.deltaTime, noEnemies);
        if (
            state.Status != previousStatus ||
            state.CurrentPhaseIndex != previousPhase ||
            state.DefeatedThisPhase != previousDefeats
        )
        {
            PublishObjective();
        }
    }

    public void ConfigureAuthored(
        EnemySpawnDirector director,
        PowerSuitEncounterPhase[] authoredPhases,
        float authoredIntermissionSeconds = 2.5f
    )
    {
        spawnDirector = director;
        phases = authoredPhases != null
            ? (PowerSuitEncounterPhase[])authoredPhases.Clone()
            : Array.Empty<PowerSuitEncounterPhase>();
        intermissionSeconds = Mathf.Max(0f, authoredIntermissionSeconds);
        state = null;
    }

    public void BindPlayer(Transform owningPlayer, PlayerHealth health)
    {
        if (
            player == owningPlayer &&
            playerHealth == health &&
            state != null &&
            subscribed
        )
        {
            return;
        }

        bool ownershipChanged = player != owningPlayer || playerHealth != health;
        bool needsInitialReset = state == null || ownershipChanged;
        Unsubscribe();
        player = owningPlayer;
        playerHealth = health;
        EnsureState();
        Subscribe();
        if (
            needsInitialReset &&
            spawnDirector != null &&
            spawnDirector.IsInitialized
        )
        {
            spawnDirector.SetDirectorEnabled(false);
            spawnDirector.ClearActiveEnemies();
        }
        PublishObjective();
    }

    public void ResetEncounter()
    {
        EnsureState();
        spawnDirector?.ClearActiveEnemies();
        spawnDirector?.SetDirectorEnabled(false);
        spawnDirector?.SetAllowedSpawnZones(Array.Empty<string>());
        state.ResetAll();
        ResetSpawnCursor();
        PublishObjective();
    }

    private void EnsureState()
    {
        if (state != null)
        {
            return;
        }
        if (phases == null || phases.Length == 0)
        {
            throw new InvalidOperationException(
                "The demo encounter requires at least one authored phase."
            );
        }

        DemoEncounterPhaseConfig[] configs =
            new DemoEncounterPhaseConfig[phases.Length];
        for (int index = 0; index < phases.Length; index++)
        {
            if (phases[index] == null)
            {
                throw new InvalidOperationException(
                    "The demo encounter contains a null phase."
                );
            }
            configs[index] = phases[index].CreateRuntimeConfig();
        }
        state = new DemoEncounterState(configs, intermissionSeconds);
    }

    private void BeginCurrentPhase()
    {
        PowerSuitEncounterPhase phase = phases[state.CurrentPhaseIndex];
        spawnDirector.ClearActiveEnemies();
        spawnDirector.SetDirectorEnabled(false);
        spawnDirector.SetAllowedSpawnZones(phase.AllowedZoneIds);
        ResetSpawnCursor();
        TryRequestNextSpawns();
    }

    private void TryRequestNextSpawns()
    {
        if (!state.CanRequestSpawn)
        {
            return;
        }

        PowerSuitEncounterSpawnEntry[] entries =
            phases[state.CurrentPhaseIndex].SpawnEntries;
        while (
            spawnEntryIndex < entries.Length &&
            (entries[spawnEntryIndex] == null ||
             spawnedFromCurrentEntry >= entries[spawnEntryIndex].Count)
        )
        {
            spawnEntryIndex++;
            spawnedFromCurrentEntry = 0;
        }

        if (spawnEntryIndex >= entries.Length)
        {
            return;
        }

        PowerSuitEncounterSpawnEntry entry = entries[spawnEntryIndex];
        int requested = Mathf.Min(
            maximumSpawnRequestSize,
            Mathf.Min(
                entry.Count - spawnedFromCurrentEntry,
                state.RemainingToSpawn
            )
        );
        int spawned = spawnDirector.SpawnArchetypeForEncounter(
            entry.ArchetypeId,
            requested,
            encounterSafeRadius
        );
        if (spawned > 0)
        {
            int accepted = state.RegisterSpawned(spawned);
            spawnedFromCurrentEntry += accepted;
            PublishObjective();
        }
        spawnRequestRemaining = spawnRequestInterval;
    }

    private void OnEnemyDefeated(EnemyArchetypeController enemy)
    {
        if (state != null && state.RegisterDefeat())
        {
            PublishObjective();
        }
    }

    private void OnPlayerDefeated()
    {
        if (state != null && state.Fail())
        {
            spawnDirector?.ClearActiveEnemies();
            PublishObjective();
        }
    }

    private void OnPlayerRespawned()
    {
        if (state == null || state.Status != DemoEncounterStatus.Failed)
        {
            return;
        }

        state.RestartCurrentPhase();
        ResetSpawnCursor();
        PublishObjective();
    }

    private void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        if (spawnDirector != null)
        {
            spawnDirector.EnemyDefeated += OnEnemyDefeated;
        }
        if (playerHealth != null)
        {
            playerHealth.OnDefeated += OnPlayerDefeated;
            playerHealth.OnRespawned += OnPlayerRespawned;
        }
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }
        if (spawnDirector != null)
        {
            spawnDirector.EnemyDefeated -= OnEnemyDefeated;
        }
        if (playerHealth != null)
        {
            playerHealth.OnDefeated -= OnPlayerDefeated;
            playerHealth.OnRespawned -= OnPlayerRespawned;
        }
        subscribed = false;
    }

    private void ResetSpawnCursor()
    {
        spawnEntryIndex = 0;
        spawnedFromCurrentEntry = 0;
        spawnRequestRemaining = 0f;
    }

    private void PublishObjective()
    {
        objectiveText = BuildObjectiveText();
        ObjectiveRevision++;
        ObjectiveChanged?.Invoke();
    }

    private string BuildObjectiveText()
    {
        if (state == null)
        {
            return "OBJECTIVE --";
        }

        int number = state.CurrentPhaseIndex + 1;
        string title = state.CurrentPhase.DisplayName.ToUpperInvariant();
        switch (state.Status)
        {
            case DemoEncounterStatus.WaitingForZone:
                return $"OBJECTIVE {number}/{state.PhaseCount}  REACH {title}";
            case DemoEncounterStatus.Active:
                return $"SECURE {title}  {state.DefeatedThisPhase}/{state.CurrentPhase.TargetDefeats}";
            case DemoEncounterStatus.Intermission:
                return "ZONE SECURED  ADVANCE TO THE NEXT SECTOR";
            case DemoEncounterStatus.Complete:
                return "MISSION COMPLETE  ALL ZONES SECURED";
            case DemoEncounterStatus.Failed:
                return "SUIT DOWN  CURRENT ENCOUNTER WILL RESET";
            default:
                return "OBJECTIVE --";
        }
    }
}
