using System;
using System.Collections.Generic;
using Powersuit.Combat;
using UnityEngine;

namespace Powersuit.Abilities.UnityAdapters
{
    public readonly struct AbilityAreaEffectExecutionResult
    {
        public AbilityAreaEffectExecutionResult(
            int colliderCount,
            int logicalTargetCount,
            int damagedTargetCount,
            int killedTargetCount,
            int forcedTargetCount,
            float totalAppliedDamage,
            bool queryCapacityReached
        )
        {
            ColliderCount = colliderCount;
            LogicalTargetCount = logicalTargetCount;
            DamagedTargetCount = damagedTargetCount;
            KilledTargetCount = killedTargetCount;
            ForcedTargetCount = forcedTargetCount;
            TotalAppliedDamage = totalAppliedDamage;
            QueryCapacityReached = queryCapacityReached;
        }

        public int ColliderCount { get; }
        public int LogicalTargetCount { get; }
        public int DamagedTargetCount { get; }
        public int KilledTargetCount { get; }
        public int ForcedTargetCount { get; }
        public float TotalAppliedDamage { get; }

        /// <summary>
        /// True when the fixed collider buffer was completely filled. The
        /// result may be complete, but callers should treat it as potentially
        /// truncated and increase capacity for representative encounter load.
        /// </summary>
        public bool QueryCapacityReached { get; }
    }

    /// <summary>
    /// Reusable fixed-capacity area query. It performs no managed allocation
    /// during Execute, deduplicates multi-collider targets by the transform that
    /// owns their damage/force receiver, and applies each effect once per root.
    /// </summary>
    public sealed class AbilityAreaEffectExecutor
    {
        private readonly Collider[] colliderBuffer;
        private readonly LogicalTarget[] logicalTargets;
        private readonly Dictionary<EntityId, int> targetIndexByRootId;

        public AbilityAreaEffectExecutor(int capacity = 64)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    "Area-effect query capacity must be greater than zero."
                );
            }

            colliderBuffer = new Collider[capacity];
            logicalTargets = new LogicalTarget[capacity];
            targetIndexByRootId = new Dictionary<EntityId, int>(capacity);
        }

        public int Capacity => colliderBuffer.Length;

        public AbilityAreaEffectExecutionResult Execute(
            AbilityAreaEffect effect,
            int layerMask = Physics.DefaultRaycastLayers,
            QueryTriggerInteraction triggerInteraction =
                QueryTriggerInteraction.Ignore,
            CombatFaction forceOnlyRecipientFaction = CombatFaction.None
        )
        {
            targetIndexByRootId.Clear();
            int logicalTargetCount = 0;
            int colliderCount = Physics.OverlapSphereNonAlloc(
                effect.Center,
                effect.Radius,
                colliderBuffer,
                layerMask,
                triggerInteraction
            );
            bool capacityReached = colliderCount >= colliderBuffer.Length;

            for (int index = 0; index < colliderCount; index++)
            {
                Collider collider = colliderBuffer[index];
                colliderBuffer[index] = null;
                if (collider == null)
                {
                    continue;
                }

                IDamageReceiver damageReceiver =
                    collider.GetComponentInParent<IDamageReceiver>();
                IExternalForceReceiver forceReceiver =
                    collider.GetComponentInParent<IExternalForceReceiver>();
                Transform logicalRoot = ResolveLogicalRoot(
                    collider,
                    damageReceiver,
                    forceReceiver
                );
                if (
                    logicalRoot == null ||
                    (damageReceiver == null && forceReceiver == null)
                )
                {
                    continue;
                }

                EntityId rootId = logicalRoot.GetEntityId();
                Vector3 recipientPosition = collider.ClosestPoint(effect.Center);
                float squaredDistance =
                    (recipientPosition - effect.Center).sqrMagnitude;
                if (
                    targetIndexByRootId.TryGetValue(
                        rootId,
                        out int targetIndex
                    )
                )
                {
                    LogicalTarget existing = logicalTargets[targetIndex];
                    if (squaredDistance < existing.SquaredDistance)
                    {
                        existing.Position = recipientPosition;
                        existing.SquaredDistance = squaredDistance;
                    }

                    if (existing.DamageReceiver == null)
                    {
                        existing.DamageReceiver = damageReceiver;
                    }

                    if (existing.ForceReceiver == null)
                    {
                        existing.ForceReceiver = forceReceiver;
                    }

                    logicalTargets[targetIndex] = existing;
                    continue;
                }

                if (logicalTargetCount >= logicalTargets.Length)
                {
                    capacityReached = true;
                    continue;
                }

                targetIndexByRootId.Add(rootId, logicalTargetCount);
                logicalTargets[logicalTargetCount] = new LogicalTarget
                {
                    DamageReceiver = damageReceiver,
                    ForceReceiver = forceReceiver,
                    Position = recipientPosition,
                    SquaredDistance = squaredDistance
                };
                logicalTargetCount++;
            }

            int damagedTargetCount = 0;
            int killedTargetCount = 0;
            int forcedTargetCount = 0;
            float totalAppliedDamage = 0f;

            for (int index = 0; index < logicalTargetCount; index++)
            {
                LogicalTarget target = logicalTargets[index];
                logicalTargets[index] = default;

                DamageResult damageResult = effect.ApplyDamage(
                    target.DamageReceiver,
                    target.Position
                );
                if (damageResult.WasApplied)
                {
                    damagedTargetCount++;
                    totalAppliedDamage += damageResult.AppliedAmount;
                    if (damageResult.WasKilled)
                    {
                        killedTargetCount++;
                    }
                }

                bool forceApplied = false;
                if (target.ForceReceiver != null)
                {
                    CombatFaction recipientFaction =
                        target.DamageReceiver != null
                            ? target.DamageReceiver.Faction
                            : forceOnlyRecipientFaction;
                    forceApplied = effect.ApplyExternalForce(
                        target.ForceReceiver,
                        target.Position,
                        recipientFaction
                    );
                }

                if (forceApplied)
                {
                    forcedTargetCount++;
                }
            }

            return new AbilityAreaEffectExecutionResult(
                colliderCount,
                logicalTargetCount,
                damagedTargetCount,
                killedTargetCount,
                forcedTargetCount,
                totalAppliedDamage,
                capacityReached
            );
        }

        private static Transform ResolveLogicalRoot(
            Collider collider,
            IDamageReceiver damageReceiver,
            IExternalForceReceiver forceReceiver
        )
        {
            if (damageReceiver is Component damageComponent)
            {
                return damageComponent.transform;
            }

            if (forceReceiver is Component forceComponent)
            {
                return forceComponent.transform;
            }

            return collider.attachedRigidbody != null
                ? collider.attachedRigidbody.transform
                : collider.transform;
        }

        private struct LogicalTarget
        {
            public IDamageReceiver DamageReceiver;
            public IExternalForceReceiver ForceReceiver;
            public Vector3 Position;
            public float SquaredDistance;
        }
    }
}
