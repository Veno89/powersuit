using Powersuit.Enemies;
using UnityEngine;

namespace Powersuit.Enemies.UnityAdapters
{
    public readonly struct EnemyTelegraphSignal
    {
        public EnemyTelegraphSignal(
            EnemyAttackProfile profile,
            Vector3 origin,
            Vector3 intendedTarget,
            float durationSeconds
        )
        {
            Profile = profile;
            Origin = origin;
            IntendedTarget = intendedTarget;
            DurationSeconds = durationSeconds;
        }

        public EnemyAttackProfile Profile { get; }
        public Vector3 Origin { get; }
        public Vector3 IntendedTarget { get; }
        public float DurationSeconds { get; }
    }

    /// <summary>
    /// The authoritative attack boundary. A projectile/VFX adapter subscribes
    /// here so warning presentation always precedes gameplay emission.
    /// </summary>
    public readonly struct EnemyAttackSignal
    {
        public EnemyAttackSignal(
            EnemyAttackProfile profile,
            Vector3 origin,
            Vector3 direction,
            int burstShotIndex
        )
        {
            Profile = profile;
            Origin = origin;
            Direction = direction;
            BurstShotIndex = burstShotIndex;
        }

        public EnemyAttackProfile Profile { get; }
        public Vector3 Origin { get; }
        public Vector3 Direction { get; }
        public int BurstShotIndex { get; }
    }
}
