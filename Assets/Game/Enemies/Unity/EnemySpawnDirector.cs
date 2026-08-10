using System;
using System.Collections.Generic;
using Powersuit.Combat;
using UnityEngine;

namespace Powersuit.Enemies.UnityAdapters
{
    [Serializable]
    public sealed class EnemySpawnPrefabEntry
    {
        [SerializeField] private EnemyArchetypeDefinition definition;
        [SerializeField] private GameObject prefab;
        [SerializeField] private bool isEnabled = true;
        [Min(0.01f)] [SerializeField] private float weightMultiplier = 1f;
        [Min(0)] [SerializeField] private int prewarmCount;

        public EnemySpawnPrefabEntry(
            EnemyArchetypeDefinition definition,
            GameObject prefab,
            bool isEnabled = true,
            float weightMultiplier = 1f,
            int prewarmCount = 0
        )
        {
            this.definition = definition;
            this.prefab = prefab;
            this.isEnabled = isEnabled;
            this.weightMultiplier = weightMultiplier;
            this.prewarmCount = prewarmCount;
        }

        public EnemyArchetypeDefinition Definition => definition;
        public GameObject Prefab => prefab;
        public bool IsEnabled => isEnabled;
        public float WeightMultiplier => weightMultiplier;
        public int PrewarmCount => prewarmCount;

        internal bool TryCreateRuntimeEntry(
            out EnemySpawnEntry runtimeEntry,
            out string validationError
        )
        {
            if (definition == null)
            {
                runtimeEntry = null;
                validationError = "Spawn entry has no archetype definition.";
                return false;
            }

            if (prefab == null)
            {
                runtimeEntry = null;
                validationError = definition.name + " has no enemy prefab.";
                return false;
            }

            if (prefab.GetComponent<EnemyArchetypeController>() == null)
            {
                runtimeEntry = null;
                validationError = prefab.name
                    + " must have EnemyArchetypeController on its root.";
                return false;
            }

            if (
                float.IsNaN(weightMultiplier) ||
                float.IsInfinity(weightMultiplier) ||
                weightMultiplier <= 0f
            )
            {
                runtimeEntry = null;
                validationError = definition.name + " has an invalid weight multiplier.";
                return false;
            }

            try
            {
                runtimeEntry = new EnemySpawnEntry(
                    definition.CreateRuntimeConfig(),
                    isEnabled,
                    weightMultiplier
                );
                validationError = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is InvalidOperationException
            )
            {
                runtimeEntry = null;
                validationError = definition.name + ": " + exception.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// Unity lifecycle and pooling adapter for the deterministic spawn planner.
    /// All selection, budget, cap, safe-radius, and stagger math remains in the
    /// engine-independent Enemies.Runtime assembly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySpawnDirector : MonoBehaviour
    {
        private static uint sessionSequence = 0x9E3779B9u;
        public const int MinimumActiveEnemyCap = 1;
        public const int MaximumActiveEnemyCap = 512;
        public const float MinimumSpawnIntervalSeconds = 0.05f;
        public const float MaximumSpawnIntervalSeconds = 3600f;

        private sealed class CandidateBufferView : IReadOnlyList<SpawnPointCandidate>
        {
            private SpawnPointCandidate[] buffer = Array.Empty<SpawnPointCandidate>();

            public int Count { get; private set; }
            public SpawnPointCandidate this[int index]
            {
                get
                {
                    if (index < 0 || index >= Count)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    return buffer[index];
                }
            }

            public void Set(SpawnPointCandidate[] source, int count)
            {
                buffer = source ?? throw new ArgumentNullException(nameof(source));
                if (count < 0 || count > source.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }

                Count = count;
            }

            public IEnumerator<SpawnPointCandidate> GetEnumerator()
            {
                for (int index = 0; index < Count; index++)
                {
                    yield return buffer[index];
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private readonly struct RuntimeSpawnSource
        {
            public RuntimeSpawnSource(
                EnemySpawnEntry runtimeEntry,
                EnemyArchetypeDefinition definition,
                GameObject prefab,
                int prewarmCount
            )
            {
                RuntimeEntry = runtimeEntry;
                Definition = definition;
                Prefab = prefab;
                PrewarmCount = prewarmCount;
            }

            public EnemySpawnEntry RuntimeEntry { get; }
            public EnemyArchetypeDefinition Definition { get; }
            public GameObject Prefab { get; }
            public int PrewarmCount { get; }
        }

        private struct PendingSpawn
        {
            public SpawnRequest Request;
            public RuntimeSpawnSource Source;
            public float ActivationDelayRemaining;
        }

        private struct ActiveEnemy
        {
            public GameObject Instance;
            public EnemyArchetypeController Controller;
            public float DeathRecycleRemaining;
            public bool IsAwaitingRecycle;
        }

        [Header("References")]
        [SerializeField] private Transform playerTarget;
        [SerializeField] private Camera visibilityCamera;
        [SerializeField] private SpawnZone[] spawnZones = Array.Empty<SpawnZone>();
        [SerializeField] private EnemySpawnPrefabEntry[] spawnEntries =
            Array.Empty<EnemySpawnPrefabEntry>();

        [Header("Schedule")]
        [Min(1)] [SerializeField] private int activeEnemyCap = 12;
        [Min(0.01f)] [SerializeField] private float spawnIntervalSeconds = 4f;
        [Min(1)] [SerializeField] private int minimumGroupSize = 1;
        [Min(1)] [SerializeField] private int maximumGroupSize = 3;
        [Min(0.01f)] [SerializeField] private float groupThreatBudget = 5f;
        [Min(0f)] [SerializeField] private float groupActivationSpacingSeconds = 0.18f;

        [Header("Fairness")]
        [Min(0f)] [SerializeField] private float playerSafeRadius = 10f;
        [SerializeField] private bool avoidCameraView = true;
        [Min(0f)] [SerializeField] private float spawnProtectionSeconds = 0.65f;
        [Min(0f)] [SerializeField] private float maximumInitialAttackStaggerSeconds = 1.1f;
        [Min(0f)] [SerializeField] private float deathRecycleDelaySeconds = 0.6f;

        [Header("Randomness and lifecycle")]
        [SerializeField] private bool useDeterministicSeed = true;
        [SerializeField] private int deterministicSeed = 109;
        [SerializeField] private bool spawnImmediately = true;
        [SerializeField] private bool prewarmPools = true;
        [SerializeField] private bool initializeOnStart = true;
        [SerializeField] private bool automaticTick = true;

        private readonly CandidateBufferView candidateView = new CandidateBufferView();
        private SpawnDirectorConfig runtimeConfig;
        private SpawnDirectorRuntimeState directorState;
        private SpawnPlanner planner;
        private RuntimeSpawnSource[] runtimeSources = Array.Empty<RuntimeSpawnSource>();
        private EnemySpawnEntry[] runtimeEntries = Array.Empty<EnemySpawnEntry>();
        private SpawnPointCandidate[] candidateBuffer = Array.Empty<SpawnPointCandidate>();
        private SpawnRequest[] planBuffer = Array.Empty<SpawnRequest>();
        private PendingSpawn[] pendingSpawns = Array.Empty<PendingSpawn>();
        private ActiveEnemy[] activeEnemies = Array.Empty<ActiveEnemy>();
        private int pendingCount;
        private int activeCount;
        private float enemyHealthMultiplier = 1f;
        private float enemyDamageMultiplier = 1f;
        private float enemySpeedMultiplier = 1f;
        private bool initialized;
        private bool isDestroying;

        public SpawnDirectorConfig RuntimeConfig => runtimeConfig;
        public SpawnDirectorRuntimeState RuntimeState => directorState;
        public bool IsInitialized => initialized;
        public bool IsDirectorEnabled => initialized && directorState.IsEnabled;
        public bool IsPaused => initialized && directorState.IsPaused;
        public int ActiveInstanceCount => activeCount;
        public int PendingSpawnCount => pendingCount;
        public int ReservedEnemyCount => initialized ? directorState.ActiveEnemyCount : 0;
        public int CandidateCount => candidateView.Count;
        public int SpawnEntryCount => runtimeSources.Length;
        public int ActiveEnemyCap => initialized
            ? directorState.ActiveEnemyCap
            : activeEnemyCap;
        public float SpawnIntervalSeconds => initialized
            ? directorState.SpawnIntervalSeconds
            : spawnIntervalSeconds;
        public float EnemyHealthMultiplier => enemyHealthMultiplier;
        public float EnemyDamageMultiplier => enemyDamageMultiplier;
        public float EnemySpeedMultiplier => enemySpeedMultiplier;
        public int TotalSpawned { get; private set; }
        public SpawnPlanResult LastPlanResult { get; private set; }
        public string LastValidationError { get; private set; } = string.Empty;

        public event Action<EnemyArchetypeController> EnemySpawned;
        public event Action<EnemyArchetypeController> EnemyRecycled;
        public event Action<SpawnPlanResult> SpawnCyclePlanned;

        private void Start()
        {
            if (initializeOnStart && !initialized)
            {
                TryInitializeFromSerialized();
            }
        }

        private void Update()
        {
            if (automaticTick)
            {
                Tick(Time.deltaTime);
            }
        }

        private void OnDestroy()
        {
            isDestroying = true;
            ClearActiveEnemies();
        }

        public SpawnDirectorConfig CreateSerializedRuntimeConfig()
        {
            int runtimeMaximumGroupSize = Mathf.Clamp(
                maximumGroupSize,
                1,
                activeEnemyCap
            );
            int runtimeMinimumGroupSize = Mathf.Clamp(
                minimumGroupSize,
                1,
                runtimeMaximumGroupSize
            );
            return new SpawnDirectorConfig(
                activeEnemyCap,
                spawnIntervalSeconds,
                runtimeMinimumGroupSize,
                runtimeMaximumGroupSize,
                groupThreatBudget,
                playerSafeRadius,
                avoidCameraView,
                spawnProtectionSeconds,
                maximumInitialAttackStaggerSeconds,
                useDeterministicSeed,
                unchecked((uint)deterministicSeed)
            );
        }

        public bool TryInitializeFromSerialized()
        {
            try
            {
                Initialize(
                    playerTarget,
                    visibilityCamera,
                    spawnZones,
                    spawnEntries,
                    CreateSerializedRuntimeConfig(),
                    spawnImmediately
                );
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is InvalidOperationException
            )
            {
                LastValidationError = exception.Message;
                Debug.LogError(
                    "EnemySpawnDirector could not initialize: " + LastValidationError,
                    this
                );
                return false;
            }
        }

        public bool TryInitializeForPlayer(
            Transform explicitPlayerTarget,
            Camera explicitVisibilityCamera
        )
        {
            playerTarget = explicitPlayerTarget;
            visibilityCamera = explicitVisibilityCamera;
            return TryInitializeFromSerialized();
        }

        public void Initialize(
            Transform explicitPlayerTarget,
            Camera explicitVisibilityCamera,
            SpawnZone[] explicitZones,
            EnemySpawnPrefabEntry[] explicitEntries,
            SpawnDirectorConfig config,
            bool shouldSpawnImmediately = true
        )
        {
            if (explicitPlayerTarget == null)
            {
                throw new ArgumentNullException(nameof(explicitPlayerTarget));
            }

            if (explicitZones == null)
            {
                throw new ArgumentNullException(nameof(explicitZones));
            }

            if (explicitEntries == null)
            {
                throw new ArgumentNullException(nameof(explicitEntries));
            }

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.AvoidCameraView && explicitVisibilityCamera == null)
            {
                throw new ArgumentNullException(
                    nameof(explicitVisibilityCamera),
                    "Camera avoidance requires an explicitly assigned camera."
                );
            }

            for (int index = 0; index < explicitZones.Length; index++)
            {
                if (explicitZones[index] == null)
                {
                    throw new ArgumentException(
                        "Spawn zone references cannot contain null entries.",
                        nameof(explicitZones)
                    );
                }
            }

            if (initialized)
            {
                ClearActiveEnemies();
            }

            playerTarget = explicitPlayerTarget;
            visibilityCamera = explicitVisibilityCamera;
            spawnZones = (SpawnZone[])explicitZones.Clone();
            spawnEntries = (EnemySpawnPrefabEntry[])explicitEntries.Clone();
            runtimeConfig = config;
            BuildRuntimeSources();
            BuildBuffers();

            sessionSequence = unchecked(sessionSequence + 0x9E3779B9u);
            uint sessionSeed = unchecked(
                (uint)Environment.TickCount ^ sessionSequence
            );
            planner = new SpawnPlanner(runtimeConfig, sessionSeed);
            directorState = new SpawnDirectorRuntimeState(
                runtimeConfig,
                shouldSpawnImmediately
            );
            pendingCount = 0;
            activeCount = 0;
            TotalSpawned = 0;
            LastPlanResult = default;
            initialized = true;

            if (prewarmPools)
            {
                PrewarmConfiguredPools();
            }
        }

        public void Tick(float deltaSeconds)
        {
            RequireFiniteNonNegative(deltaSeconds, nameof(deltaSeconds));
            if (!initialized)
            {
                return;
            }

            NormalizeRuntimeCollections();
            if (
                runtimeConfig == null ||
                directorState == null ||
                planner == null
            )
            {
                initialized = false;
                if (initializeOnStart)
                {
                    TryInitializeFromSerialized();
                }
                return;
            }

            ProcessActiveEnemies(deltaSeconds);
            if (
                playerTarget == null ||
                !directorState.IsEnabled ||
                directorState.IsPaused
            )
            {
                return;
            }

            ProcessPendingSpawns(deltaSeconds);
            if (directorState.Advance(deltaSeconds))
            {
                RunSpawnCycle();
            }
        }

        public SpawnPlanResult ForceSpawnCycle()
        {
            if (
                !initialized ||
                playerTarget == null ||
                !directorState.IsEnabled ||
                directorState.IsPaused ||
                directorState.CapacityRemaining <= 0
            )
            {
                LastPlanResult = default;
                return LastPlanResult;
            }

            return RunSpawnCycle();
        }

        /// <summary>
        /// Immediately spawns up to <paramref name="requestedCount"/> eligible
        /// configured enemies, independent of the automatic spawner's enabled
        /// or paused state. The active cap and all spawn fairness checks still
        /// apply. Returns the number actually activated.
        /// </summary>
        public int SpawnRandom(int requestedCount)
        {
            return SpawnImmediate(null, requestedCount);
        }

        /// <summary>
        /// Immediately spawns one configured archetype by its stable id. Id
        /// matching is case-insensitive. Returns the number actually activated.
        /// </summary>
        public int SpawnArchetype(string archetypeId, int requestedCount)
        {
            if (string.IsNullOrWhiteSpace(archetypeId))
            {
                return 0;
            }

            return SpawnImmediate(archetypeId.Trim(), requestedCount);
        }

        public void SetDirectorEnabled(bool isEnabled)
        {
            if (initialized)
            {
                directorState.SetEnabled(isEnabled);
            }
        }

        public void SetPaused(bool isPaused)
        {
            if (initialized)
            {
                directorState.SetPaused(isPaused);
            }
        }

        public int SetActiveEnemyCap(int value)
        {
            activeEnemyCap = Mathf.Clamp(
                value,
                MinimumActiveEnemyCap,
                MaximumActiveEnemyCap
            );
            if (initialized)
            {
                directorState.SetActiveEnemyCap(activeEnemyCap);
                planner.SetActiveEnemyCap(activeEnemyCap);
                if (activeEnemies.Length < activeEnemyCap)
                {
                    Array.Resize(ref activeEnemies, activeEnemyCap);
                }
                if (pendingSpawns.Length < activeEnemyCap)
                {
                    Array.Resize(ref pendingSpawns, activeEnemyCap);
                }
            }
            return activeEnemyCap;
        }

        public float SetSpawnIntervalSeconds(float value)
        {
            spawnIntervalSeconds = ClampFinite(
                value,
                MinimumSpawnIntervalSeconds,
                MaximumSpawnIntervalSeconds,
                spawnIntervalSeconds
            );
            if (initialized)
            {
                directorState.SetSpawnIntervalSeconds(spawnIntervalSeconds);
            }
            return spawnIntervalSeconds;
        }

        public float SetEnemyHealthMultiplier(float value)
        {
            enemyHealthMultiplier = ClampFinite(
                value,
                EnemyArchetypeController.MinimumHealthMultiplier,
                EnemyArchetypeController.MaximumHealthMultiplier,
                enemyHealthMultiplier
            );
            ApplyRuntimeMultipliersToActiveEnemies();
            return enemyHealthMultiplier;
        }

        public float SetEnemyDamageMultiplier(float value)
        {
            enemyDamageMultiplier = ClampFinite(
                value,
                EnemyArchetypeController.MinimumDamageMultiplier,
                EnemyArchetypeController.MaximumDamageMultiplier,
                enemyDamageMultiplier
            );
            ApplyRuntimeMultipliersToActiveEnemies();
            return enemyDamageMultiplier;
        }

        public float SetEnemySpeedMultiplier(float value)
        {
            enemySpeedMultiplier = ClampFinite(
                value,
                EnemyArchetypeController.MinimumSpeedMultiplier,
                EnemyArchetypeController.MaximumSpeedMultiplier,
                enemySpeedMultiplier
            );
            ApplyRuntimeMultipliersToActiveEnemies();
            return enemySpeedMultiplier;
        }

        public int KillAllActiveEnemies()
        {
            int killed = 0;
            for (int index = 0; index < activeCount; index++)
            {
                EnemyArchetypeController controller =
                    activeEnemies[index].Controller;
                if (controller == null || controller.IsDead)
                {
                    continue;
                }

                controller.MarkDead();
                killed++;
            }
            return killed;
        }

        public int DespawnAllEnemies()
        {
            int removed = activeCount + pendingCount;
            ClearActiveEnemies();
            return removed;
        }

        public string GetSpawnArchetypeId(int index)
        {
            if (index < 0 || index >= runtimeSources.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return runtimeSources[index].RuntimeEntry.Archetype.ArchetypeId;
        }

        private int SpawnImmediate(string archetypeId, int requestedCount)
        {
            if (!initialized || playerTarget == null || requestedCount <= 0)
            {
                return 0;
            }

            int targetCount = Mathf.Clamp(
                requestedCount,
                0,
                MaximumActiveEnemyCap
            );
            RefreshCandidateSnapshot();
            if (candidateView.Count == 0)
            {
                return 0;
            }

            int spawned = 0;
            int sourceOffset = runtimeSources.Length > 0
                ? Mathf.Abs(TotalSpawned) % runtimeSources.Length
                : 0;
            int candidateOffset = Mathf.Abs(TotalSpawned) % candidateView.Count;
            while (
                spawned < targetCount &&
                directorState.CapacityRemaining > 0
            )
            {
                bool activated = false;
                for (
                    int sourceStep = 0;
                    sourceStep < runtimeSources.Length && !activated;
                    sourceStep++
                )
                {
                    RuntimeSpawnSource source = runtimeSources[
                        (sourceOffset + sourceStep) % runtimeSources.Length
                    ];
                    EnemySpawnEntry entry = source.RuntimeEntry;
                    if (
                        !entry.IsEnabled ||
                        (
                            archetypeId != null &&
                            !string.Equals(
                                entry.Archetype.ArchetypeId,
                                archetypeId,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    )
                    {
                        continue;
                    }

                    for (
                        int candidateStep = 0;
                        candidateStep < candidateView.Count;
                        candidateStep++
                    )
                    {
                        int candidateIndex =
                            (candidateOffset + spawned + candidateStep) %
                            candidateView.Count;
                        SpawnPointCandidate candidate =
                            candidateView[candidateIndex];
                        if (
                            SpawnEligibility.Evaluate(
                                entry.Archetype,
                                candidate,
                                ToCombatVector(playerTarget.position),
                                runtimeConfig.PlayerSafeRadius,
                                runtimeConfig.AvoidCameraView
                            ) != SpawnEligibilityFailure.None
                        )
                        {
                            continue;
                        }

                        if (
                            directorState.ReserveSpawnSlots(
                                1,
                                ignoreLifecycleState: true
                            ) != 1
                        )
                        {
                            return spawned;
                        }

                        int activeBefore = activeCount;
                        ActivatePendingSpawn(
                            new PendingSpawn
                            {
                                Request = new SpawnRequest(
                                    entry.Archetype,
                                    candidate,
                                    candidateIndex,
                                    runtimeConfig.MaximumInitialAttackStaggerSeconds *
                                        ((spawned % 5) / 5f),
                                    runtimeConfig.SpawnProtectionSeconds
                                ),
                                Source = source,
                                ActivationDelayRemaining = 0f
                            }
                        );
                        activated = activeCount > activeBefore;
                        if (activated)
                        {
                            spawned++;
                            sourceOffset =
                                (sourceOffset + sourceStep + 1) %
                                runtimeSources.Length;
                        }
                        break;
                    }
                }

                if (!activated)
                {
                    break;
                }
            }

            return spawned;
        }

        public void SetGroupActivationSpacing(float seconds)
        {
            RequireFiniteNonNegative(seconds, nameof(seconds));
            groupActivationSpacingSeconds = seconds;
        }

        public void SetDeathRecycleDelay(float seconds)
        {
            RequireFiniteNonNegative(seconds, nameof(seconds));
            deathRecycleDelaySeconds = seconds;
        }

        public void SetPlayerTarget(Transform explicitTarget)
        {
            playerTarget = explicitTarget;
            for (int index = 0; index < activeCount; index++)
            {
                EnemyArchetypeController controller = activeEnemies[index].Controller;
                if (controller != null)
                {
                    controller.SetTarget(explicitTarget);
                }
            }
        }

        public void ResetDirector(
            bool clearExistingEnemies = true,
            bool shouldSpawnImmediately = true
        )
        {
            if (!initialized)
            {
                return;
            }

            int preservedReservations = activeCount + pendingCount;
            if (clearExistingEnemies)
            {
                ClearActiveEnemies();
                preservedReservations = 0;
            }

            planner.Reset();
            directorState.Reset(isEnabled: true, spawnImmediately: shouldSpawnImmediately);
            if (preservedReservations > 0)
            {
                directorState.ReserveSpawnSlots(preservedReservations);
            }

            LastPlanResult = default;
        }

        public void ResetDirectorWithSeed(
            uint seed,
            bool clearExistingEnemies = true,
            bool shouldSpawnImmediately = true
        )
        {
            ResetDirector(clearExistingEnemies, shouldSpawnImmediately);
            if (initialized)
            {
                planner.Reset(seed);
            }
        }

        public void ClearActiveEnemies()
        {
            if (!initialized && !isDestroying)
            {
                return;
            }

            NormalizeRuntimeCollections();

            for (int index = activeCount - 1; index >= 0; index--)
            {
                ActiveEnemy active = activeEnemies[index];
                if (active.Instance != null)
                {
                    CombatFeedbackPool.Recycle(active.Instance);
                }

                if (active.Controller != null)
                {
                    EnemyRecycled?.Invoke(active.Controller);
                }

                activeEnemies[index] = default;
            }

            activeCount = 0;
            for (int index = 0; index < pendingCount; index++)
            {
                pendingSpawns[index] = default;
            }
            pendingCount = 0;
            if (directorState != null)
            {
                directorState.ClearActiveEnemies();
            }
        }

        public EnemyArchetypeController GetActiveEnemy(int index)
        {
            NormalizeRuntimeCollections();
            if (index < 0 || index >= activeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return activeEnemies[index].Controller;
        }

        public SpawnPointCandidate GetCandidate(int index)
        {
            return candidateView[index];
        }

        private SpawnPlanResult RunSpawnCycle()
        {
            RefreshCandidateSnapshot();
            if (candidateView.Count == 0)
            {
                LastPlanResult = default;
                SpawnCyclePlanned?.Invoke(LastPlanResult);
                return LastPlanResult;
            }

            SpawnPlanResult plan = planner.FillPlan(
                runtimeEntries,
                candidateView,
                ToCombatVector(playerTarget.position),
                directorState.ActiveEnemyCount,
                planBuffer
            );
            LastPlanResult = plan;
            SpawnCyclePlanned?.Invoke(plan);
            if (!plan.HasSpawns)
            {
                return plan;
            }

            int reserved = directorState.ReserveSpawnSlots(plan.Count);
            int queued = 0;
            for (int index = 0; index < reserved; index++)
            {
                RuntimeSpawnSource source = FindRuntimeSource(planBuffer[index].Archetype);
                if (source.Prefab == null || pendingCount >= pendingSpawns.Length)
                {
                    directorState.RegisterDespawned();
                    continue;
                }

                pendingSpawns[pendingCount] = new PendingSpawn
                {
                    Request = planBuffer[index],
                    Source = source,
                    ActivationDelayRemaining = groupActivationSpacingSeconds * index
                };
                pendingCount++;
                queued++;
            }

            // The first group member activates on the planning frame.
            if (queued > 0)
            {
                ProcessPendingSpawns(0f);
            }

            return plan;
        }

        private void ProcessPendingSpawns(float deltaSeconds)
        {
            int index = 0;
            while (index < pendingCount)
            {
                PendingSpawn pending = pendingSpawns[index];
                pending.ActivationDelayRemaining = Mathf.Max(
                    0f,
                    pending.ActivationDelayRemaining - deltaSeconds
                );
                pendingSpawns[index] = pending;
                if (pending.ActivationDelayRemaining > 0.00001f)
                {
                    index++;
                    continue;
                }

                RemovePendingAt(index);
                ActivatePendingSpawn(pending);
            }
        }

        private void ActivatePendingSpawn(PendingSpawn pending)
        {
            Vector3 position = ToUnityVector(pending.Request.Point.Position);
            Quaternion rotation = CreateSpawnRotation(position);
            GameObject instance = CombatFeedbackPool.Spawn(
                pending.Source.Prefab,
                position,
                rotation
            );
            if (instance == null)
            {
                directorState.RegisterDespawned();
                return;
            }

            EnemyArchetypeController controller =
                instance.GetComponent<EnemyArchetypeController>();
            if (controller == null || activeCount >= activeEnemies.Length)
            {
                CombatFeedbackPool.Recycle(instance);
                directorState.RegisterDespawned();
                return;
            }

            try
            {
                controller.Initialize(
                    pending.Request.Archetype,
                    playerTarget,
                    pending.Request.SpawnProtectionSeconds,
                    pending.Request.InitialAttackDelaySeconds
                );
                controller.SetRuntimeMultipliers(
                    enemyHealthMultiplier,
                    enemyDamageMultiplier,
                    enemySpeedMultiplier
                );
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Enemy spawn initialization failed: " + exception.Message,
                    this
                );
                CombatFeedbackPool.Recycle(instance);
                directorState.RegisterDespawned();
                return;
            }

            activeEnemies[activeCount] = new ActiveEnemy
            {
                Instance = instance,
                Controller = controller,
                DeathRecycleRemaining = deathRecycleDelaySeconds,
                IsAwaitingRecycle = false
            };
            activeCount++;
            TotalSpawned++;
            EnemySpawned?.Invoke(controller);
        }

        private void ProcessActiveEnemies(float deltaSeconds)
        {
            NormalizeRuntimeCollections();
            int index = 0;
            while (index < activeCount)
            {
                ActiveEnemy active = activeEnemies[index];
                if (
                    active.Instance == null ||
                    !active.Instance.activeInHierarchy ||
                    active.Controller == null ||
                    !active.Controller.IsInitialized
                )
                {
                    if (active.Instance != null)
                    {
                        CombatFeedbackPool.Recycle(active.Instance);
                    }

                    RemoveActiveAt(index, notify: true);
                    continue;
                }

                if (!active.Controller.IsDead)
                {
                    index++;
                    continue;
                }

                if (!active.IsAwaitingRecycle)
                {
                    active.IsAwaitingRecycle = true;
                    active.DeathRecycleRemaining = deathRecycleDelaySeconds;
                }

                active.DeathRecycleRemaining = Mathf.Max(
                    0f,
                    active.DeathRecycleRemaining - deltaSeconds
                );
                if (active.DeathRecycleRemaining > 0.00001f)
                {
                    activeEnemies[index] = active;
                    index++;
                    continue;
                }

                if (active.Instance != null)
                {
                    CombatFeedbackPool.Recycle(active.Instance);
                }

                RemoveActiveAt(index, notify: true);
            }
        }

        private void RemoveActiveAt(int index, bool notify)
        {
            NormalizeRuntimeCollections();
            if (index < 0 || index >= activeCount)
            {
                return;
            }

            EnemyArchetypeController controller = activeEnemies[index].Controller;
            int lastIndex = activeCount - 1;
            activeEnemies[index] = activeEnemies[lastIndex];
            activeEnemies[lastIndex] = default;
            activeCount--;
            directorState.RegisterDespawned();
            if (notify && controller != null)
            {
                EnemyRecycled?.Invoke(controller);
            }
        }

        private void RemovePendingAt(int index)
        {
            NormalizeRuntimeCollections();
            if (index < 0 || index >= pendingCount)
            {
                return;
            }

            int elementsAfter = pendingCount - index - 1;
            for (int offset = 0; offset < elementsAfter; offset++)
            {
                pendingSpawns[index + offset] = pendingSpawns[index + offset + 1];
            }

            pendingCount--;
            pendingSpawns[pendingCount] = default;
        }

        private void NormalizeRuntimeCollections()
        {
            activeEnemies ??= Array.Empty<ActiveEnemy>();
            pendingSpawns ??= Array.Empty<PendingSpawn>();
            activeCount = Mathf.Clamp(activeCount, 0, activeEnemies.Length);
            pendingCount = Mathf.Clamp(pendingCount, 0, pendingSpawns.Length);
        }

        private void RefreshCandidateSnapshot()
        {
            int count = 0;
            for (int index = 0; index < spawnZones.Length; index++)
            {
                SpawnZone zone = spawnZones[index];
                if (zone == null || count >= candidateBuffer.Length)
                {
                    continue;
                }

                count += zone.FillCandidates(
                    candidateBuffer,
                    count,
                    visibilityCamera
                );
            }

            candidateView.Set(candidateBuffer, count);
        }

        private void BuildRuntimeSources()
        {
            int validCount = 0;
            string firstError = string.Empty;
            for (int index = 0; index < spawnEntries.Length; index++)
            {
                EnemySpawnPrefabEntry entry = spawnEntries[index];
                string error = entry == null ? "Spawn entry is null." : string.Empty;
                if (
                    entry != null &&
                    entry.TryCreateRuntimeEntry(out _, out error)
                )
                {
                    validCount++;
                }
                else if (string.IsNullOrEmpty(firstError))
                {
                    firstError = error;
                }
            }

            if (!string.IsNullOrEmpty(firstError))
            {
                throw new InvalidOperationException(firstError);
            }

            if (validCount == 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(firstError)
                        ? "At least one valid enemy spawn entry is required."
                        : firstError
                );
            }

            runtimeSources = new RuntimeSpawnSource[validCount];
            runtimeEntries = new EnemySpawnEntry[validCount];
            int destination = 0;
            for (int index = 0; index < spawnEntries.Length; index++)
            {
                EnemySpawnPrefabEntry authored = spawnEntries[index];
                if (
                    authored == null ||
                    !authored.TryCreateRuntimeEntry(
                        out EnemySpawnEntry runtimeEntry,
                        out _
                    )
                )
                {
                    continue;
                }

                RuntimeSpawnSource source = new RuntimeSpawnSource(
                    runtimeEntry,
                    authored.Definition,
                    authored.Prefab,
                    Mathf.Max(0, authored.PrewarmCount)
                );
                runtimeSources[destination] = source;
                runtimeEntries[destination] = runtimeEntry;
                destination++;
            }

            LastValidationError = string.Empty;
        }

        private void BuildBuffers()
        {
            int candidateCapacity = 0;
            for (int index = 0; index < spawnZones.Length; index++)
            {
                SpawnZone zone = spawnZones[index];
                if (zone != null)
                {
                    candidateCapacity += zone.CandidateCapacity;
                }
            }

            if (candidateCapacity <= 0)
            {
                throw new InvalidOperationException(
                    "At least one SpawnZone candidate is required."
                );
            }

            candidateBuffer = new SpawnPointCandidate[candidateCapacity];
            candidateView.Set(candidateBuffer, 0);
            planBuffer = new SpawnRequest[runtimeConfig.MaximumGroupSize];
            pendingSpawns = new PendingSpawn[runtimeConfig.ActiveEnemyCap];
            activeEnemies = new ActiveEnemy[runtimeConfig.ActiveEnemyCap];
        }

        private void PrewarmConfiguredPools()
        {
            for (int index = 0; index < runtimeSources.Length; index++)
            {
                RuntimeSpawnSource source = runtimeSources[index];
                if (source.RuntimeEntry.IsEnabled && source.PrewarmCount > 0)
                {
                    CombatFeedbackPool.Prewarm(source.Prefab, source.PrewarmCount);
                }
            }
        }

        private RuntimeSpawnSource FindRuntimeSource(EnemyArchetypeConfig archetype)
        {
            for (int index = 0; index < runtimeSources.Length; index++)
            {
                RuntimeSpawnSource source = runtimeSources[index];
                if (ReferenceEquals(source.RuntimeEntry.Archetype, archetype))
                {
                    return source;
                }
            }

            return default;
        }

        private void ApplyRuntimeMultipliersToActiveEnemies()
        {
            for (int index = 0; index < activeCount; index++)
            {
                EnemyArchetypeController controller =
                    activeEnemies[index].Controller;
                if (controller != null)
                {
                    controller.SetRuntimeMultipliers(
                        enemyHealthMultiplier,
                        enemyDamageMultiplier,
                        enemySpeedMultiplier
                    );
                }
            }
        }

        private Quaternion CreateSpawnRotation(Vector3 spawnPosition)
        {
            if (playerTarget == null)
            {
                return Quaternion.identity;
            }

            Vector3 facing = Vector3.ProjectOnPlane(
                playerTarget.position - spawnPosition,
                Vector3.up
            );
            return facing.sqrMagnitude > 0.00001f
                ? Quaternion.LookRotation(facing.normalized, Vector3.up)
                : Quaternion.identity;
        }

        private static Vector3 ToUnityVector(CombatVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        private static CombatVector3 ToCombatVector(Vector3 value)
        {
            return new CombatVector3(value.x, value.y, value.z);
        }

        private static void RequireFiniteNonNegative(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static float ClampFinite(
            float value,
            float minimum,
            float maximum,
            float fallback
        )
        {
            if (float.IsNaN(value))
            {
                return fallback;
            }

            if (float.IsPositiveInfinity(value))
            {
                return maximum;
            }

            if (float.IsNegativeInfinity(value))
            {
                return minimum;
            }

            return Mathf.Clamp(value, minimum, maximum);
        }

        private void OnValidate()
        {
            activeEnemyCap = Mathf.Clamp(
                activeEnemyCap,
                MinimumActiveEnemyCap,
                MaximumActiveEnemyCap
            );
            spawnIntervalSeconds = ClampFinite(
                spawnIntervalSeconds,
                MinimumSpawnIntervalSeconds,
                MaximumSpawnIntervalSeconds,
                4f
            );
            minimumGroupSize = Mathf.Clamp(minimumGroupSize, 1, activeEnemyCap);
            maximumGroupSize = Mathf.Clamp(
                maximumGroupSize,
                minimumGroupSize,
                activeEnemyCap
            );
            groupThreatBudget = Mathf.Max(0.01f, groupThreatBudget);
            groupActivationSpacingSeconds = Mathf.Max(0f, groupActivationSpacingSeconds);
            playerSafeRadius = Mathf.Max(0f, playerSafeRadius);
            spawnProtectionSeconds = Mathf.Max(0f, spawnProtectionSeconds);
            maximumInitialAttackStaggerSeconds = Mathf.Max(
                0f,
                maximumInitialAttackStaggerSeconds
            );
            deathRecycleDelaySeconds = Mathf.Max(0f, deathRecycleDelaySeconds);
        }
    }
}
