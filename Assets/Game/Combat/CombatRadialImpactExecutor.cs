using System;
using System.Collections.Generic;
using Powersuit.Combat;
using Powersuit.Combat.UnityAdapters;
using UnityEngine;

public readonly struct CombatRadialImpactResult
{
    public CombatRadialImpactResult(
        int logicalTargets,
        int damagedTargets,
        int killedTargets,
        int staggeredTargets,
        int forcedTargets,
        float totalAppliedDamage,
        bool capacityReached
    )
    {
        LogicalTargets = logicalTargets;
        DamagedTargets = damagedTargets;
        KilledTargets = killedTargets;
        StaggeredTargets = staggeredTargets;
        ForcedTargets = forcedTargets;
        TotalAppliedDamage = totalAppliedDamage;
        CapacityReached = capacityReached;
    }

    public int LogicalTargets { get; }
    public int DamagedTargets { get; }
    public int KilledTargets { get; }
    public int StaggeredTargets { get; }
    public int ForcedTargets { get; }
    public float TotalAppliedDamage { get; }
    public bool CapacityReached { get; }
}

/// <summary>
/// Fixed-capacity radial weapon impact query. Multi-collider enemies are
/// deduplicated by their receiver root and receive damage, stagger, and force
/// at most once per projectile impact.
/// </summary>
public sealed class CombatRadialImpactExecutor
{
    private readonly Collider[] colliderBuffer;
    private readonly LogicalTarget[] logicalTargets;
    private readonly Dictionary<EntityId, int> targetIndexByRootId;

    public CombatRadialImpactExecutor(int capacity = 64)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        colliderBuffer = new Collider[capacity];
        logicalTargets = new LogicalTarget[capacity];
        targetIndexByRootId = new Dictionary<EntityId, int>(capacity);
    }

    public int Capacity => colliderBuffer.Length;

    public CombatRadialImpactResult Execute(
        Vector3 center,
        Vector3 surfaceNormal,
        float radius,
        float damage,
        float minimumDamageMultiplier,
        float impulse,
        float staggerSeconds,
        object source,
        Transform sourceRoot,
        CombatFaction sourceFaction,
        DamageType damageType,
        bool isCritical,
        int layerMask = Physics.DefaultRaycastLayers
    )
    {
        radius = Mathf.Max(0.01f, radius);
        damage = Mathf.Max(0f, damage);
        minimumDamageMultiplier = Mathf.Clamp01(minimumDamageMultiplier);
        impulse = Mathf.Max(0f, impulse);
        staggerSeconds = Mathf.Max(0f, staggerSeconds);
        Vector3 fallbackDirection = surfaceNormal.sqrMagnitude > 0.000001f
            ? surfaceNormal.normalized
            : Vector3.up;

        targetIndexByRootId.Clear();
        int logicalCount = 0;
        int colliderCount = Physics.OverlapSphereNonAlloc(
            center,
            radius,
            colliderBuffer,
            layerMask,
            QueryTriggerInteraction.Ignore
        );
        bool capacityReached = colliderCount >= colliderBuffer.Length;

        for (int index = 0; index < colliderCount; index++)
        {
            Collider collider = colliderBuffer[index];
            colliderBuffer[index] = null;
            if (collider == null || IsSourceCollider(collider, sourceRoot))
            {
                continue;
            }

            IDamageReceiver damageReceiver =
                collider.GetComponentInParent<IDamageReceiver>();
            IExternalForceReceiver forceReceiver =
                collider.GetComponentInParent<IExternalForceReceiver>();
            IStaggerReceiver staggerReceiver =
                collider.GetComponentInParent<IStaggerReceiver>();
            Transform logicalRoot = ResolveRoot(
                collider,
                damageReceiver,
                forceReceiver,
                staggerReceiver
            );
            if (
                logicalRoot == null ||
                (damageReceiver == null &&
                 forceReceiver == null &&
                 staggerReceiver == null)
            )
            {
                continue;
            }

            Vector3 position = collider.ClosestPoint(center);
            float squaredDistance = (position - center).sqrMagnitude;
            EntityId rootId = logicalRoot.GetEntityId();
            if (targetIndexByRootId.TryGetValue(rootId, out int targetIndex))
            {
                LogicalTarget existing = logicalTargets[targetIndex];
                if (squaredDistance < existing.SquaredDistance)
                {
                    existing.Position = position;
                    existing.SquaredDistance = squaredDistance;
                }
                existing.DamageReceiver ??= damageReceiver;
                existing.ForceReceiver ??= forceReceiver;
                existing.StaggerReceiver ??= staggerReceiver;
                logicalTargets[targetIndex] = existing;
                continue;
            }

            if (logicalCount >= logicalTargets.Length)
            {
                capacityReached = true;
                continue;
            }

            targetIndexByRootId.Add(rootId, logicalCount);
            logicalTargets[logicalCount++] = new LogicalTarget
            {
                DamageReceiver = damageReceiver,
                ForceReceiver = forceReceiver,
                StaggerReceiver = staggerReceiver,
                Position = position,
                SquaredDistance = squaredDistance
            };
        }

        int damaged = 0;
        int killed = 0;
        int staggered = 0;
        int forced = 0;
        float totalDamage = 0f;
        for (int index = 0; index < logicalCount; index++)
        {
            LogicalTarget target = logicalTargets[index];
            logicalTargets[index] = default;
            float normalizedDistance = Mathf.Clamp01(
                Mathf.Sqrt(target.SquaredDistance) / radius
            );
            float falloff = Mathf.Lerp(
                1f,
                minimumDamageMultiplier,
                normalizedDistance
            );
            Vector3 direction = target.Position - center;
            if (direction.sqrMagnitude <= 0.000001f)
            {
                direction = fallbackDirection;
            }
            else
            {
                direction.Normalize();
            }

            DamageResult damageResult = DamageResult.Ignored;
            if (target.DamageReceiver != null)
            {
                damageResult = target.DamageReceiver.ApplyDamage(
                    new DamageInfo(
                        source,
                        sourceFaction,
                        damageType,
                        damage * falloff,
                        CombatVectorConversion.ToCombat(target.Position),
                        CombatVectorConversion.ToCombat(direction),
                        isCritical
                    )
                );
            }

            if (damageResult.WasApplied)
            {
                damaged++;
                totalDamage += damageResult.AppliedAmount;
                if (damageResult.WasKilled)
                {
                    killed++;
                }
                else if (
                    staggerSeconds > 0f &&
                    target.StaggerReceiver != null &&
                    target.StaggerReceiver.CanReceiveStagger &&
                    target.StaggerReceiver.TryApplyStagger(staggerSeconds)
                )
                {
                    staggered++;
                }
            }

            CombatFaction targetFaction = target.DamageReceiver != null
                ? target.DamageReceiver.Faction
                : CombatFaction.None;
            if (
                impulse > 0f &&
                target.ForceReceiver != null &&
                target.ForceReceiver.CanReceiveExternalForce &&
                CombatFactionPolicy.CanDamage(sourceFaction, targetFaction)
            )
            {
                target.ForceReceiver.ApplyExternalForce(
                    CombatVectorConversion.ToCombat(direction * impulse * falloff),
                    source
                );
                forced++;
            }
        }

        return new CombatRadialImpactResult(
            logicalCount,
            damaged,
            killed,
            staggered,
            forced,
            totalDamage,
            capacityReached
        );
    }

    private static bool IsSourceCollider(Collider collider, Transform sourceRoot)
    {
        if (sourceRoot == null)
        {
            return false;
        }

        Transform candidate = collider.transform;
        return candidate == sourceRoot || candidate.IsChildOf(sourceRoot);
    }

    private static Transform ResolveRoot(
        Collider collider,
        IDamageReceiver damageReceiver,
        IExternalForceReceiver forceReceiver,
        IStaggerReceiver staggerReceiver
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
        if (staggerReceiver is Component staggerComponent)
        {
            return staggerComponent.transform;
        }
        return collider.attachedRigidbody != null
            ? collider.attachedRigidbody.transform
            : collider.transform;
    }

    private struct LogicalTarget
    {
        public IDamageReceiver DamageReceiver;
        public IExternalForceReceiver ForceReceiver;
        public IStaggerReceiver StaggerReceiver;
        public Vector3 Position;
        public float SquaredDistance;
    }
}
