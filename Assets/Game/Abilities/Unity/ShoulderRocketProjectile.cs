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

        [Header("Area Readability")]
        [SerializeField, Min(0.05f)] private float impactVisibleSeconds = 0.8f;
        [SerializeField] private AbilityAreaEffectPresentation areaPresentation;

        private RaycastHit[] hitBuffer = new RaycastHit[InitialHitCapacity];
        private AbilityAreaEffectExecutor areaExecutor;
        private ShoulderRocketLaunchCommand command;
        private Transform sourceRoot;
        private float age;
        private bool initialized;
        private bool displayingImpact;
        private float impactAge;
        private TrailRenderer trail;
        private Renderer[] projectileRenderers;

        public event Action<AbilityAreaEffectExecutionResult> ExplosionResolved;

        public bool IsInitialized => initialized;
        public bool IsDisplayingImpact => displayingImpact;
        public AbilityAreaEffectPresentation AreaPresentation => areaPresentation;

        private void Awake()
        {
            CacheProjectilePresentation();
            EnsureAreaPresentation();
        }

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
            impactAge = 0f;
            displayingImpact = false;
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

            SetProjectileVisible(true);
            EnsureAreaPresentation();
            areaPresentation.ResetPresentation();

            EnsureExecutor();
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }


            if (displayingImpact)
            {
                impactAge += Time.deltaTime;
                if (impactAge >= impactVisibleSeconds)
                {
                    RecycleSelf();
                }
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


            displayingImpact = true;
            impactAge = 0f;
            if (trail != null)
            {
                trail.emitting = false;
            }
            SetProjectileVisible(false);
            transform.rotation = Quaternion.FromToRotation(
                Vector3.up,
                effect.SurfaceNormal
            );
            EnsureAreaPresentation();
            areaPresentation.PlayImpact(
                effect.Radius,
                impactVisibleSeconds,
                AbilityAreaPresentationStyle.Rocket
            );

            ExplosionResolved?.Invoke(result);
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

        private void EnsureAreaPresentation()
        {
            if (areaPresentation == null)
            {
                areaPresentation = GetComponent<AbilityAreaEffectPresentation>();
            }
            if (areaPresentation == null)
            {
                areaPresentation = gameObject.AddComponent<
                    AbilityAreaEffectPresentation
                >();
            }
        }

        private void CacheProjectilePresentation()
        {
            if (projectileRenderers == null || projectileRenderers.Length == 0)
            {
                projectileRenderers = GetComponents<Renderer>();
            }
            if (trail == null)
            {
                trail = GetComponentInChildren<TrailRenderer>(true);
            }
        }

        private void SetProjectileVisible(bool isVisible)
        {
            CacheProjectilePresentation();
            foreach (Renderer projectileRenderer in projectileRenderers)
            {
                if (projectileRenderer != null && projectileRenderer != trail)
                {
                    projectileRenderer.enabled = isVisible;
                }
            }
        }

        public void OnPoolSpawned()
        {
            initialized = false;
            age = 0f;
            impactAge = 0f;
            displayingImpact = false;
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
            SetProjectileVisible(true);
            EnsureAreaPresentation();
            areaPresentation.ResetPresentation();
        }

        public void OnPoolRecycled()
        {
            initialized = false;
            age = 0f;
            impactAge = 0f;
            displayingImpact = false;
            sourceRoot = null;
            command = default;
            ExplosionResolved = null;
            if (trail != null)
            {
                trail.Clear();
                trail.emitting = false;
            }
            SetProjectileVisible(true);
            if (areaPresentation != null)
            {
                areaPresentation.ResetPresentation();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            collisionRadius = Mathf.Max(0.01f, collisionRadius);
            areaQueryCapacity = Mathf.Max(1, areaQueryCapacity);
            impactVisibleSeconds = Mathf.Max(0.05f, impactVisibleSeconds);
        }
#endif
    }
}
