using System;

namespace Powersuit.Enemies
{
    /// <summary>
    /// Tracks director lifecycle independently from scene objects. Reserving a
    /// slot before activating a pooled enemy makes cap enforcement atomic from
    /// the adapter's perspective.
    /// </summary>
    public sealed class SpawnDirectorRuntimeState
    {
        private const float TimeEpsilon = 0.00001f;

        private int activeEnemyCap;
        private float spawnIntervalSeconds;
        private float secondsUntilNextSpawn;

        public SpawnDirectorRuntimeState(
            SpawnDirectorConfig config,
            bool spawnImmediately = true
        )
        {
            _ = config ?? throw new ArgumentNullException(nameof(config));
            activeEnemyCap = config.ActiveEnemyCap;
            spawnIntervalSeconds = config.SpawnIntervalSeconds;
            Reset(isEnabled: true, spawnImmediately: spawnImmediately);
        }

        public bool IsEnabled { get; private set; }
        public bool IsPaused { get; private set; }
        public int ActiveEnemyCount { get; private set; }
        public int ActiveEnemyCap => activeEnemyCap;
        public float SpawnIntervalSeconds => spawnIntervalSeconds;
        public int CapacityRemaining =>
            Math.Max(0, activeEnemyCap - ActiveEnemyCount);
        public float SecondsUntilNextSpawn => secondsUntilNextSpawn;

        public bool Advance(float deltaSeconds)
        {
            EnemyAttackProfile.RequireNonNegative(deltaSeconds, nameof(deltaSeconds));

            if (!IsEnabled || IsPaused)
            {
                return false;
            }

            if (CapacityRemaining == 0)
            {
                secondsUntilNextSpawn = spawnIntervalSeconds;
                return false;
            }

            secondsUntilNextSpawn = Math.Max(0f, secondsUntilNextSpawn - deltaSeconds);
            if (secondsUntilNextSpawn > TimeEpsilon)
            {
                return false;
            }

            secondsUntilNextSpawn = spawnIntervalSeconds;
            return true;
        }

        public int ReserveSpawnSlots(
            int requestedCount,
            bool ignoreLifecycleState = false
        )
        {
            if (requestedCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requestedCount));
            }

            if (
                (!ignoreLifecycleState && (!IsEnabled || IsPaused)) ||
                requestedCount == 0
            )
            {
                return 0;
            }

            int reserved = Math.Min(requestedCount, CapacityRemaining);
            ActiveEnemyCount += reserved;
            return reserved;
        }

        public void RegisterDespawned(int count = 1)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            ActiveEnemyCount = Math.Max(0, ActiveEnemyCount - count);
        }

        public void SetEnabled(bool isEnabled)
        {
            IsEnabled = isEnabled;
        }

        public void SetPaused(bool isPaused)
        {
            IsPaused = isPaused;
        }

        public void SetActiveEnemyCap(int value)
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            activeEnemyCap = value;
        }

        public void SetSpawnIntervalSeconds(float value)
        {
            EnemyAttackProfile.RequirePositive(value, nameof(value));
            spawnIntervalSeconds = value;
            secondsUntilNextSpawn = Math.Min(
                secondsUntilNextSpawn,
                spawnIntervalSeconds
            );
        }

        public void ClearActiveEnemies()
        {
            ActiveEnemyCount = 0;
        }

        public void Reset(bool isEnabled = true, bool spawnImmediately = true)
        {
            IsEnabled = isEnabled;
            IsPaused = false;
            ActiveEnemyCount = 0;
            secondsUntilNextSpawn = spawnImmediately
                ? 0f
                : spawnIntervalSeconds;
        }
    }
}
