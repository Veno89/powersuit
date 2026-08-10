using System;
using UnityEngine;

namespace Powersuit.Enemies.UnityAdapters
{
    /// <summary>
    /// Presentation adapter between EnemyArchetypeController's fair attack
    /// boundary and one shared pooled physical projectile prefab.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyArchetypeController))]
    public sealed class EnemyAttackEmitter : MonoBehaviour
    {
        [SerializeField] private EnemyAttackProjectile projectilePrefab;
        [SerializeField, Min(0)] private int prewarmCount = 6;

        private EnemyArchetypeController controller;

        public EnemyAttackProjectile ProjectilePrefab
        {
            get => projectilePrefab;
            set => projectilePrefab = value;
        }

        public event Action<EnemyTelegraphSignal> TelegraphStarted;
        public event Action<EnemyAttackSignal> ProjectileEmitted;

        private void Awake()
        {
            controller = GetComponent<EnemyArchetypeController>();
            if (projectilePrefab != null)
            {
                CombatFeedbackPool.Prewarm(
                    projectilePrefab.gameObject,
                    prewarmCount
                );
            }
        }

        private void OnEnable()
        {
            controller ??= GetComponent<EnemyArchetypeController>();
            controller.AttackTelegraphStarted += HandleTelegraph;
            controller.AttackRequested += HandleAttack;
        }

        private void OnDisable()
        {
            if (controller == null)
            {
                return;
            }
            controller.AttackTelegraphStarted -= HandleTelegraph;
            controller.AttackRequested -= HandleAttack;
        }

        private void HandleTelegraph(EnemyTelegraphSignal signal)
        {
            TelegraphStarted?.Invoke(signal);
        }

        private void HandleAttack(EnemyAttackSignal signal)
        {
            if (projectilePrefab == null)
            {
                return;
            }

            GameObject spawned = CombatFeedbackPool.Spawn(
                projectilePrefab.gameObject,
                signal.Origin,
                Quaternion.LookRotation(signal.Direction, Vector3.up)
            );
            EnemyAttackProjectile projectile = spawned != null
                ? spawned.GetComponent<EnemyAttackProjectile>()
                : null;
            if (projectile == null)
            {
                if (spawned != null)
                {
                    CombatFeedbackPool.Recycle(spawned);
                }
                return;
            }

            projectile.Initialize(
                signal.Profile,
                signal.Origin,
                signal.Direction,
                transform,
                controller != null
                    ? controller.OutgoingDamageMultiplier
                    : 1f
            );
            ProjectileEmitted?.Invoke(signal);
        }

        private void OnValidate()
        {
            prewarmCount = Mathf.Max(0, prewarmCount);
        }
    }
}
