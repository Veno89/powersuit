using Powersuit.Combat;
using Powersuit.Enemies;
using UnityEngine;

namespace Powersuit.Enemies.UnityAdapters
{
    /// <summary>
    /// Shared pooled physical projectile for every ranged enemy archetype.
    /// The attack profile supplies speed, damage, and damage type, so rapid
    /// sentry rounds and slow artillery shells use the same lifecycle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyAttackProjectile :
        MonoBehaviour,
        ICombatProjectilePoolable
    {
        private const int HitCapacity = 16;

        [SerializeField, Min(0.01f)] private float collisionRadius = 0.12f;
        [SerializeField, Min(0.01f)] private float maximumLifetime = 8f;
        [SerializeField] private LayerMask collisionMask = ~0;

        private readonly RaycastHit[] hits = new RaycastHit[HitCapacity];
        private EnemyAttackProfile profile;
        private Transform sourceRoot;
        private float age;
        private float damageMultiplier = 1f;
        private bool initialized;
        private TrailRenderer trail;

        public bool IsInitialized => initialized;
        public float DamageMultiplier => damageMultiplier;

        public void Initialize(
            EnemyAttackProfile attackProfile,
            Vector3 origin,
            Vector3 direction,
            Transform source
        )
        {
            Initialize(
                attackProfile,
                origin,
                direction,
                source,
                1f
            );
        }

        public void Initialize(
            EnemyAttackProfile attackProfile,
            Vector3 origin,
            Vector3 direction,
            Transform source,
            float outgoingDamageMultiplier
        )
        {
            profile = attackProfile ??
                throw new System.ArgumentNullException(nameof(attackProfile));
            sourceRoot = source;
            age = 0f;
            damageMultiplier = ClampDamageMultiplier(
                outgoingDamageMultiplier
            );
            initialized = true;
            transform.position = origin;
            transform.rotation = Quaternion.LookRotation(
                direction.sqrMagnitude > 0.000001f
                    ? direction.normalized
                    : Vector3.forward,
                Vector3.up
            );
            trail ??= GetComponentInChildren<TrailRenderer>(true);
            if (trail != null)
            {
                trail.Clear();
                trail.emitting = true;
            }
        }

        private void Update()
        {
            if (!initialized || profile == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            age += deltaTime;
            if (age >= maximumLifetime)
            {
                RecycleSelf();
                return;
            }

            float distance = profile.ProjectileSpeed * deltaTime;
            Vector3 direction = transform.forward;
            int hitCount = Physics.SphereCastNonAlloc(
                transform.position,
                collisionRadius,
                direction,
                hits,
                distance,
                collisionMask,
                QueryTriggerInteraction.Ignore
            );
            RaycastHit nearest = default;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit candidate = hits[index];
                if (
                    candidate.collider == null ||
                    IsSource(candidate.transform) ||
                    candidate.distance >= nearestDistance
                )
                {
                    continue;
                }

                nearest = candidate;
                nearestDistance = candidate.distance;
            }

            if (nearestDistance < float.PositiveInfinity)
            {
                ResolveHit(nearest, direction);
                return;
            }

            transform.position += direction * distance;
        }

        private void ResolveHit(RaycastHit hit, Vector3 direction)
        {
            transform.position = hit.point;
            IDamageReceiver receiver =
                hit.collider.GetComponentInParent<IDamageReceiver>();
            if (receiver != null)
            {
                receiver.ApplyDamage(
                    new DamageInfo(
                        sourceRoot != null
                            ? sourceRoot.gameObject
                            : gameObject,
                        CombatFaction.Enemy,
                        profile.DamageType,
                        Mathf.Min(
                            1000000f,
                            profile.Damage * damageMultiplier
                        ),
                        new CombatVector3(
                            hit.point.x,
                            hit.point.y,
                            hit.point.z
                        ),
                        new CombatVector3(
                            direction.x,
                            direction.y,
                            direction.z
                        )
                    )
                );
            }
            RecycleSelf();
        }

        private bool IsSource(Transform hit)
        {
            return sourceRoot != null &&
                (hit == sourceRoot || hit.IsChildOf(sourceRoot));
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
            profile = null;
            damageMultiplier = 1f;
            trail ??= GetComponentInChildren<TrailRenderer>(true);
            if (trail != null)
            {
                trail.Clear();
                trail.emitting = false;
            }
        }

        public void OnPoolRecycled()
        {
            OnPoolSpawned();
        }

        private void OnValidate()
        {
            collisionRadius = Mathf.Max(0.01f, collisionRadius);
            maximumLifetime = Mathf.Max(0.01f, maximumLifetime);
        }

        private static float ClampDamageMultiplier(float value)
        {
            if (float.IsNaN(value))
            {
                return 1f;
            }

            if (float.IsNegativeInfinity(value))
            {
                return 0f;
            }

            if (float.IsPositiveInfinity(value))
            {
                return EnemyArchetypeController.MaximumDamageMultiplier;
            }

            return Mathf.Clamp(
                value,
                EnemyArchetypeController.MinimumDamageMultiplier,
                EnemyArchetypeController.MaximumDamageMultiplier
            );
        }
    }
}
