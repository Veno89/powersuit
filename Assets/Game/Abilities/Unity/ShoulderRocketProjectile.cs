using System;
using UnityEngine;

namespace Powersuit.Abilities.UnityAdapters
{
    /// <summary>
    /// Pooled physical micro-rocket. It travels from the authored shoulder
    /// hardpoint, ignores its source hierarchy, and resolves one faction-safe
    /// radial damage/force transaction on the first valid collision.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShoulderRocketProjectile :
        MonoBehaviour,
        ICombatProjectilePoolable
    {
        private const int InitialHitCapacity = 16;
        private const int MaximumHitCapacity = 128;

        [SerializeField, Min(0.01f)] private float collisionRadius = 0.12f;
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField, Min(1)] private int areaQueryCapacity = 64;
        [SerializeField] private GameObject impactEffectPrefab;

        private RaycastHit[] hitBuffer = new RaycastHit[InitialHitCapacity];
        private AbilityAreaEffectExecutor areaExecutor;
        private ShoulderRocketLaunchCommand command;
        private Transform sourceRoot;
        private float age;
        private bool initialized;
        private TrailRenderer trail;

        public event Action<AbilityAreaEffectExecutionResult> ExplosionResolved;

        public bool IsInitialized => initialized;

        public void Initialize(
            ShoulderRocketLaunchCommand launchCommand,
            Transform sourceTransform
        )
        {
            if (
                !AbilityAreaEffect.IsFinite(launchCommand.Origin) ||
                !AbilityAreaEffect.IsFinite(launchCommand.Direction) ||
                launchCommand.Direction.sqrMagnitude <= 0.000001f ||
                !AbilityAreaEffect.IsFinite(launchCommand.ProjectileSpeed) ||
                launchCommand.ProjectileSpeed <= 0f ||
                !AbilityAreaEffect.IsFinite(launchCommand.ProjectileLifetime) ||
                launchCommand.ProjectileLifetime <= 0f
            )
            {
                throw new ArgumentOutOfRangeException(nameof(launchCommand));
            }

            command = launchCommand;
            sourceRoot = sourceTransform;
            age = 0f;
            initialized = true;
            transform.position = launchCommand.Origin;
            transform.rotation = Quaternion.LookRotation(
                launchCommand.Direction.normalized,
                Vector3.up
            );

            if (trail == null)
            {
                trail = GetComponentInChildren<TrailRenderer>(true);
            }
            if (trail != null)
            {
                trail.Clear();
                trail.emitting = true;
            }

            EnsureExecutor();
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            age += deltaTime;
            if (age >= command.ProjectileLifetime)
            {
                RecycleSelf();
                return;
            }

            float distance = command.ProjectileSpeed * deltaTime;
            if (distance <= 0f)
            {
                return;
            }

            Vector3 direction = transform.forward;
            int hitCount = CastStep(transform.position, direction, distance);
            RaycastHit nearest = default;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = hitBuffer[index];
                if (
                    IsValidHit(hit.collider) &&
                    hit.distance < nearestDistance
                )
                {
                    nearest = hit;
                    nearestDistance = hit.distance;
                }
            }

            if (nearestDistance < float.PositiveInfinity)
            {
                ResolveImpact(nearest);
                return;
            }

            transform.position += direction * distance;
        }

        private int CastStep(Vector3 origin, Vector3 direction, float distance)
        {
            while (true)
            {
                int hitCount = Physics.SphereCastNonAlloc(
                    origin,
                    collisionRadius,
                    direction,
                    hitBuffer,
                    distance,
                    collisionMask,
                    QueryTriggerInteraction.Ignore
                );
                if (
                    hitCount < hitBuffer.Length ||
                    hitBuffer.Length >= MaximumHitCapacity
                )
                {
                    return hitCount;
                }

                hitBuffer = new RaycastHit[Mathf.Min(
                    MaximumHitCapacity,
                    hitBuffer.Length * 2
                )];
            }
        }

        private bool IsValidHit(Collider collider)
        {
            if (collider == null)
            {
                return false;
            }

            Transform hit = collider.transform;
            return
                sourceRoot == null ||
                (hit != sourceRoot && !hit.IsChildOf(sourceRoot));
        }

        private void ResolveImpact(RaycastHit hit)
        {
            transform.position = hit.point;
            EnsureExecutor();
            AbilityAreaEffect effect = command.CreateExplosion(
                hit.point,
                hit.normal.sqrMagnitude > 0.000001f
                    ? hit.normal
                    : -transform.forward
            );
            AbilityAreaEffectExecutionResult result = areaExecutor.Execute(
                effect,
                collisionMask,
                QueryTriggerInteraction.Ignore
            );

            if (impactEffectPrefab != null)
            {
                CombatFeedbackPool.Spawn(
                    impactEffectPrefab,
                    hit.point,
                    Quaternion.LookRotation(effect.SurfaceNormal)
                );
            }

            ExplosionResolved?.Invoke(result);
            RecycleSelf();
        }

        private void EnsureExecutor()
        {
            int capacity = Mathf.Max(1, areaQueryCapacity);
            if (areaExecutor == null || areaExecutor.Capacity != capacity)
            {
                areaExecutor = new AbilityAreaEffectExecutor(capacity);
            }
        }

        private void RecycleSelf()
        {
            initialized = false;
            CombatFeedbackPool.Recycle(gameObject);
        }

        public void OnPoolSpawned()
        {
            initialized = false;
            age = 0f;
            sourceRoot = null;
            if (trail == null)
            {
                trail = GetComponentInChildren<TrailRenderer>(true);
            }
            if (trail != null)
            {
                trail.Clear();
                trail.emitting = false;
            }
        }

        public void OnPoolRecycled()
        {
            initialized = false;
            age = 0f;
            sourceRoot = null;
            command = default;
            ExplosionResolved = null;
            if (trail != null)
            {
                trail.Clear();
                trail.emitting = false;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            collisionRadius = Mathf.Max(0.01f, collisionRadius);
            areaQueryCapacity = Mathf.Max(1, areaQueryCapacity);
        }
#endif
    }
}
