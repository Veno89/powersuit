using System;
using Powersuit.Combat;
using UnityEngine;

namespace Powersuit.Enemies.UnityAdapters
{
    /// <summary>
    /// Shared Unity adapter for all six enemy roles. The plain-C# runtime owns
    /// state selection and timers; this component supplies observations,
    /// locomotion, health, pooling reset, and a fair attack event boundary.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyArchetypeController : MonoBehaviour,
        IDamageReceiver,
        IExternalForceReceiver,
        ICombatPoolable
    {
        private enum AttackPhase
        {
            None,
            Telegraph,
            Burst
        }

        private const float TimeEpsilon = 0.00001f;
        private const int MaximumBurstShotsPerTick = 32;
        public const float MinimumHealthMultiplier = 0.01f;
        public const float MaximumHealthMultiplier = 100f;
        public const float MinimumDamageMultiplier = 0f;
        public const float MaximumDamageMultiplier = 100f;
        public const float MinimumSpeedMultiplier = 0f;
        public const float MaximumSpeedMultiplier = 10f;

        [Header("Configuration")]
        [SerializeField] private EnemyArchetypeDefinition definition;
        [SerializeField] private Transform target;
        [SerializeField] private Transform eyePoint;
        [SerializeField] private Transform attackOrigin;
        [SerializeField] private CharacterController characterController;

        [Header("Sensing")]
        [SerializeField] private LayerMask lineOfSightMask = ~0;
        [SerializeField] private Vector3 targetAimOffset = new Vector3(0f, 1.1f, 0f);

        [Header("Response")]
        [Min(0f)] [SerializeField] private float staggerDamageThreshold = 28f;
        [Min(0f)] [SerializeField] private float damageStaggerSeconds = 0.28f;
        [Min(0f)] [SerializeField] private float externalForceDamping = 7f;
        [Min(0.01f)] [SerializeField] private float maximumExternalSpeed = 24f;
        [SerializeField] private bool allowFriendlyFire;

        [Header("Ground movement")]
        [Min(0f)] [SerializeField] private float gravity = 28f;
        [Min(0f)] [SerializeField] private float groundedStickSpeed = 2f;

        [Header("Flying presentation")]
        [SerializeField] private Transform bankVisual;
        [Range(0f, 45f)] [SerializeField] private float maximumBankDegrees = 18f;
        [Min(0f)] [SerializeField] private float bankResponsiveness = 9f;

        private readonly EnemyRuntimeState runtimeState = new EnemyRuntimeState();
        private readonly RaycastHit[] lineOfSightHits = new RaycastHit[12];

        private EnemyArchetypeConfig config;
        private Vector3 spawnAnchor;
        private Vector3 velocity;
        private Vector3 externalVelocity;
        private Quaternion bankNeutralRotation;
        private float verticalSpeed;
        private float locomotionClock;
        private float attackPhaseRemaining;
        private float spawnProtectionOnNextReset;
        private float attackDelayOnNextReset;
        private float currentHealth;
        private float healthMultiplier = 1f;
        private float outgoingDamageMultiplier = 1f;
        private float speedMultiplier = 1f;
        private int burstShotIndex;
        private int strafeSign = 1;
        private bool initialized;
        private bool hasAcquiredTarget;
        private Transform authoredTarget;
        private AttackPhase attackPhase;

        public EnemyArchetypeDefinition Definition => definition;
        public EnemyArchetypeConfig Config => config;
        public EnemyRuntimeState RuntimeState => runtimeState;
        public EnemyState CurrentState => initialized
            && runtimeState.IsConfigured
            ? runtimeState.CurrentState
            : EnemyState.Idle;
        public Transform Target => target;
        public float CurrentHealth => currentHealth;
        public float MaximumHealth => config != null
            ? Mathf.Min(1000000f, config.MaximumHealth * healthMultiplier)
            : 0f;
        public float HealthFraction => MaximumHealth > 0f
            ? currentHealth / MaximumHealth
            : 0f;
        public Vector3 Velocity => velocity + externalVelocity + Vector3.up * verticalSpeed;
        public Vector3 ExternalVelocity => externalVelocity;
        public bool IsInitialized =>
            initialized && config != null && runtimeState.IsConfigured;
        public bool IsDead => IsInitialized && !runtimeState.IsAlive;
        public bool IsTelegraphing => attackPhase == AttackPhase.Telegraph;
        public CombatFaction Faction => CombatFaction.Enemy;
        public bool CanReceiveDamage =>
            IsInitialized && runtimeState.IsAlive && !runtimeState.IsSpawnProtected;
        public bool CanReceiveExternalForce => IsInitialized && runtimeState.IsAlive;
        public float HealthMultiplier => healthMultiplier;
        public float OutgoingDamageMultiplier => outgoingDamageMultiplier;
        public float SpeedMultiplier => speedMultiplier;

        public float SetHealthMultiplier(float value)
        {
            float previousMaximum = MaximumHealth;
            float healthFraction = previousMaximum > 0f
                ? currentHealth / previousMaximum
                : 1f;
            healthMultiplier = ClampFinite(
                value,
                MinimumHealthMultiplier,
                MaximumHealthMultiplier,
                healthMultiplier
            );
            if (IsInitialized && runtimeState.IsAlive)
            {
                currentHealth = MaximumHealth * Mathf.Clamp01(healthFraction);
                HealthChanged?.Invoke(currentHealth, MaximumHealth);
            }
            return healthMultiplier;
        }

        public float SetOutgoingDamageMultiplier(float value)
        {
            outgoingDamageMultiplier = ClampFinite(
                value,
                MinimumDamageMultiplier,
                MaximumDamageMultiplier,
                outgoingDamageMultiplier
            );
            return outgoingDamageMultiplier;
        }

        public float SetSpeedMultiplier(float value)
        {
            speedMultiplier = ClampFinite(
                value,
                MinimumSpeedMultiplier,
                MaximumSpeedMultiplier,
                speedMultiplier
            );
            return speedMultiplier;
        }

        public void SetRuntimeMultipliers(
            float health,
            float outgoingDamage,
            float speed
        )
        {
            SetHealthMultiplier(health);
            SetOutgoingDamageMultiplier(outgoingDamage);
            SetSpeedMultiplier(speed);
        }

        public event Action<EnemyState, EnemyState> StateChanged;
        public event Action<float, float> HealthChanged;
        public event Action<EnemyTelegraphSignal> AttackTelegraphStarted;
        public event Action<EnemyAttackSignal> AttackRequested;
        public event Action<float> StaggerStarted;
        public event Action Died;

        private void Awake()
        {
            authoredTarget = target;
            CacheReferences();
        }

        private void Start()
        {
            if (!IsInitialized && definition != null)
            {
                Initialize(
                    definition,
                    target,
                    spawnProtectionOnNextReset,
                    attackDelayOnNextReset
                );
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Initialize(
            EnemyArchetypeDefinition archetypeDefinition,
            Transform explicitTarget,
            float spawnProtectionSeconds = 0f,
            float initialAttackDelaySeconds = 0f
        )
        {
            if (archetypeDefinition == null)
            {
                throw new ArgumentNullException(nameof(archetypeDefinition));
            }

            definition = archetypeDefinition;
            Initialize(
                archetypeDefinition.CreateRuntimeConfig(),
                explicitTarget,
                spawnProtectionSeconds,
                initialAttackDelaySeconds
            );
        }

        public void Initialize(
            EnemyArchetypeConfig runtimeConfig,
            Transform explicitTarget,
            float spawnProtectionSeconds = 0f,
            float initialAttackDelaySeconds = 0f
        )
        {
            if (runtimeConfig == null)
            {
                throw new ArgumentNullException(nameof(runtimeConfig));
            }

            RequireFiniteNonNegative(spawnProtectionSeconds, nameof(spawnProtectionSeconds));
            RequireFiniteNonNegative(initialAttackDelaySeconds, nameof(initialAttackDelaySeconds));

            CacheReferences();
            config = runtimeConfig;
            target = explicitTarget;
            spawnProtectionOnNextReset = spawnProtectionSeconds;
            attackDelayOnNextReset = initialAttackDelaySeconds;
            ResetInstance();
        }

        public void SetTarget(Transform explicitTarget)
        {
            target = explicitTarget;
            hasAcquiredTarget = false;
        }

        public void Tick(float deltaSeconds)
        {
            RequireFiniteNonNegative(deltaSeconds, nameof(deltaSeconds));
            if (!EnsureRuntimeReady())
            {
                return;
            }

            EnemyState previousState = runtimeState.CurrentState;
            runtimeState.Advance(deltaSeconds);

            bool targetAvailable = UpdateTargetAcquisition(out float targetDistance);
            bool hasLineOfSight = targetAvailable && HasLineOfSightToTarget();

            EnemyDecisionContext context = new EnemyDecisionContext(
                runtimeState.IsAlive,
                runtimeState.CurrentState == EnemyState.Staggered,
                targetAvailable,
                hasLineOfSight,
                targetDistance
            );
            EnemyState nextState = runtimeState.Evaluate(context);
            if (nextState != previousState)
            {
                StateChanged?.Invoke(previousState, nextState);
            }

            if (!runtimeState.IsAlive)
            {
                CancelAttack();
                velocity = Vector3.zero;
                externalVelocity = Vector3.zero;
                verticalSpeed = 0f;
                UpdateBankVisual(deltaSeconds);
                return;
            }

            if (runtimeState.CurrentState == EnemyState.Staggered)
            {
                CancelAttack();
            }
            else if (attackPhase == AttackPhase.None)
            {
                TryBeginAttack();
            }

            AdvanceAttack(deltaSeconds, hasLineOfSight);
            AdvanceLocomotion(deltaSeconds, targetAvailable, hasLineOfSight);
        }

        public DamageResult ApplyDamage(DamageInfo damageInfo)
        {
            if (
                !CanReceiveDamage ||
                !CombatFactionPolicy.CanDamage(
                    damageInfo.Faction,
                    Faction,
                    allowFriendlyFire
                ) ||
                damageInfo.Amount <= 0f
            )
            {
                return DamageResult.Ignored;
            }

            float healthBefore = currentHealth;
            currentHealth = Mathf.Max(0f, currentHealth - damageInfo.Amount);
            float appliedAmount = healthBefore - currentHealth;
            if (currentHealth <= 0f)
            {
                MarkDead();
            }
            else
            {
                HealthChanged?.Invoke(currentHealth, MaximumHealth);
                if (
                    staggerDamageThreshold > 0f &&
                    appliedAmount >= staggerDamageThreshold
                )
                {
                    TryApplyStagger(damageStaggerSeconds);
                }
            }

            return DamageResult.Applied(appliedAmount, IsDead);
        }

        public bool TryApplyStagger(float durationSeconds)
        {
            RequireFiniteNonNegative(durationSeconds, nameof(durationSeconds));
            if (!IsInitialized)
            {
                return false;
            }

            EnemyState previousState = runtimeState.CurrentState;
            if (!runtimeState.ApplyStagger(durationSeconds))
            {
                return false;
            }

            CancelAttack();
            if (previousState != EnemyState.Staggered)
            {
                StateChanged?.Invoke(previousState, EnemyState.Staggered);
            }
            StaggerStarted?.Invoke(durationSeconds);
            return true;
        }

        public void MarkDead()
        {
            if (!IsInitialized || !runtimeState.IsAlive)
            {
                return;
            }

            EnemyState previousState = runtimeState.CurrentState;
            currentHealth = 0f;
            runtimeState.MarkDead();
            CancelAttack();
            velocity = Vector3.zero;
            externalVelocity = Vector3.zero;
            verticalSpeed = 0f;
            HealthChanged?.Invoke(currentHealth, MaximumHealth);
            StateChanged?.Invoke(previousState, EnemyState.Dead);
            Died?.Invoke();
        }

        public void ApplyExternalForce(CombatVector3 force, object source)
        {
            if (!CanReceiveExternalForce)
            {
                return;
            }

            Vector3 impulse = new Vector3(force.X, force.Y, force.Z);
            impulse *= 1f - config.AbilityResistance;
            externalVelocity = Vector3.ClampMagnitude(
                externalVelocity + impulse,
                maximumExternalSpeed
            );
        }

        public void OnPoolSpawned()
        {
            if (config == null && definition != null)
            {
                config = definition.CreateRuntimeConfig();
            }

            if (config != null)
            {
                ResetInstance();
            }
        }

        public void OnPoolRecycled()
        {
            CancelAttack();
            target = authoredTarget;
            velocity = Vector3.zero;
            externalVelocity = Vector3.zero;
            verticalSpeed = 0f;
            locomotionClock = 0f;
            hasAcquiredTarget = false;
            initialized = false;
            healthMultiplier = 1f;
            outgoingDamageMultiplier = 1f;
            speedMultiplier = 1f;
            ResetBankVisualImmediate();
        }

        private bool EnsureRuntimeReady()
        {
            if (IsInitialized)
            {
                return true;
            }

            initialized = false;
            if (
                config == null ||
                string.IsNullOrWhiteSpace(config.ArchetypeId)
            )
            {
                if (definition == null)
                {
                    config = null;
                    return false;
                }

                config = definition.CreateRuntimeConfig();
            }

            // Unity hot reload can restore the adapter's ordinary fields while
            // reconstructing the plain runtime state. Re-establish the complete
            // reset boundary before any timer/state access.
            ResetInstance();
            return true;
        }

        private void ResetInstance()
        {
            spawnAnchor = transform.position;
            velocity = Vector3.zero;
            externalVelocity = Vector3.zero;
            verticalSpeed = 0f;
            locomotionClock = 0f;
            attackPhaseRemaining = 0f;
            burstShotIndex = 0;
            hasAcquiredTarget = false;
            attackPhase = AttackPhase.None;
            currentHealth = MaximumHealth;
            strafeSign = StableSign(config.ArchetypeId);
            runtimeState.Reset(
                config,
                ToCombatVector(spawnAnchor),
                spawnProtectionOnNextReset,
                attackDelayOnNextReset
            );
            initialized = true;
            HealthChanged?.Invoke(currentHealth, MaximumHealth);
            ResetBankVisualImmediate();
        }

        private bool UpdateTargetAcquisition(out float targetDistance)
        {
            if (target == null)
            {
                hasAcquiredTarget = false;
                targetDistance = 0f;
                return false;
            }

            Vector3 delta = GetTargetPoint() - transform.position;
            targetDistance = config.IsFlying
                ? delta.magnitude
                : new Vector2(delta.x, delta.z).magnitude;

            if (hasAcquiredTarget)
            {
                if (targetDistance > config.LoseTargetRange)
                {
                    hasAcquiredTarget = false;
                }

                return hasAcquiredTarget;
            }

            if (
                targetDistance <= config.AggroRange &&
                IsInsideFieldOfView(delta) &&
                HasLineOfSightToTarget()
            )
            {
                hasAcquiredTarget = true;
            }

            return hasAcquiredTarget;
        }

        private bool IsInsideFieldOfView(Vector3 targetDelta)
        {
            if (config.FieldOfViewDegrees >= 359.9f)
            {
                return true;
            }

            Vector3 planarDelta = Vector3.ProjectOnPlane(targetDelta, Vector3.up);
            if (planarDelta.sqrMagnitude <= TimeEpsilon)
            {
                return true;
            }

            float minimumDot = Mathf.Cos(config.FieldOfViewDegrees * 0.5f * Mathf.Deg2Rad);
            return Vector3.Dot(transform.forward, planarDelta.normalized) >= minimumDot;
        }

        private bool HasLineOfSightToTarget()
        {
            if (target == null)
            {
                return false;
            }

            Vector3 origin = eyePoint != null
                ? eyePoint.position
                : transform.position + Vector3.up * 1.2f;
            Vector3 targetPoint = GetTargetPoint();
            Vector3 delta = targetPoint - origin;
            float distance = delta.magnitude;
            if (distance <= TimeEpsilon)
            {
                return true;
            }

            int hitCount = Physics.RaycastNonAlloc(
                origin,
                delta / distance,
                lineOfSightHits,
                distance,
                lineOfSightMask,
                QueryTriggerInteraction.Ignore
            );

            float nearestBlockingDistance = float.PositiveInfinity;
            bool targetHit = false;
            float nearestTargetDistance = float.PositiveInfinity;

            for (int index = 0; index < hitCount; index++)
            {
                Collider hitCollider = lineOfSightHits[index].collider;
                if (hitCollider == null)
                {
                    continue;
                }

                Transform hitTransform = hitCollider.transform;
                if (hitTransform == transform || hitTransform.IsChildOf(transform))
                {
                    continue;
                }

                float hitDistance = lineOfSightHits[index].distance;
                if (hitTransform == target || hitTransform.IsChildOf(target))
                {
                    targetHit = true;
                    nearestTargetDistance = Mathf.Min(nearestTargetDistance, hitDistance);
                }
                else
                {
                    nearestBlockingDistance = Mathf.Min(nearestBlockingDistance, hitDistance);
                }
            }

            // A target without a collider is visible when no obstacle blocks the ray.
            return targetHit
                ? nearestTargetDistance <= nearestBlockingDistance
                : float.IsPositiveInfinity(nearestBlockingDistance);
        }

        private void TryBeginAttack()
        {
            if (
                runtimeState.CurrentState != EnemyState.Attack ||
                !runtimeState.TryBeginAttack()
            )
            {
                return;
            }

            EnemyAttackProfile profile = config.AttackProfile;
            attackPhase = AttackPhase.Telegraph;
            attackPhaseRemaining = profile.TelegraphSeconds;
            burstShotIndex = 0;
            AttackTelegraphStarted?.Invoke(
                new EnemyTelegraphSignal(
                    profile,
                    GetAttackOrigin(),
                    GetTargetPoint(),
                    profile.TelegraphSeconds
                )
            );
        }

        private void AdvanceAttack(float deltaSeconds, bool hasLineOfSight)
        {
            if (attackPhase == AttackPhase.None)
            {
                return;
            }

            if (
                target == null ||
                (config.AttackProfile.RequiresLineOfSight && !hasLineOfSight)
            )
            {
                CancelAttack();
                return;
            }

            float availableTime = deltaSeconds;
            if (attackPhase == AttackPhase.Telegraph)
            {
                attackPhaseRemaining -= availableTime;
                if (attackPhaseRemaining > TimeEpsilon)
                {
                    return;
                }

                availableTime = -attackPhaseRemaining;
                EmitBurstShot();
                if (attackPhase == AttackPhase.None)
                {
                    return;
                }

                attackPhase = AttackPhase.Burst;
                attackPhaseRemaining = config.AttackProfile.BurstShotIntervalSeconds;
            }

            int guard = 0;
            attackPhaseRemaining -= availableTime;
            while (
                attackPhase == AttackPhase.Burst &&
                attackPhaseRemaining <= TimeEpsilon &&
                guard++ < MaximumBurstShotsPerTick
            )
            {
                EmitBurstShot();
                if (attackPhase != AttackPhase.Burst)
                {
                    break;
                }

                attackPhaseRemaining += config.AttackProfile.BurstShotIntervalSeconds;
            }
        }

        private void EmitBurstShot()
        {
            if (!runtimeState.TryConsumeBurstShot())
            {
                CancelAttack();
                return;
            }

            Vector3 origin = GetAttackOrigin();
            Vector3 direction = GetTargetPoint() - origin;
            if (direction.sqrMagnitude <= TimeEpsilon)
            {
                direction = transform.forward;
            }
            else
            {
                direction.Normalize();
            }

            direction = ApplyDeterministicSpread(
                direction,
                config.AttackProfile.SpreadDegrees,
                runtimeState.AttacksStarted,
                burstShotIndex
            );
            AttackRequested?.Invoke(
                new EnemyAttackSignal(
                    config.AttackProfile,
                    origin,
                    direction,
                    burstShotIndex
                )
            );
            burstShotIndex++;

            if (runtimeState.BurstShotsRemaining <= 0)
            {
                CancelAttack();
            }
        }

        private void AdvanceLocomotion(
            float deltaSeconds,
            bool targetAvailable,
            bool hasLineOfSight
        )
        {
            locomotionClock += deltaSeconds;

            bool suppressRoleMovement =
                runtimeState.CurrentState == EnemyState.Dead ||
                runtimeState.CurrentState == EnemyState.Staggered ||
                attackPhase != AttackPhase.None;
            Vector3 desiredDirection = suppressRoleMovement
                ? Vector3.zero
                : GetDesiredMovement(targetAvailable, hasLineOfSight);
            float desiredSpeed = desiredDirection.sqrMagnitude > TimeEpsilon
                ? config.MovementSpeed * speedMultiplier
                : 0f;
            Vector3 desiredVelocity = desiredDirection.sqrMagnitude > TimeEpsilon
                ? desiredDirection.normalized * desiredSpeed
                : Vector3.zero;
            velocity = Vector3.MoveTowards(
                velocity,
                desiredVelocity,
                config.Acceleration * speedMultiplier * deltaSeconds
            );
            externalVelocity = Vector3.MoveTowards(
                externalVelocity,
                Vector3.zero,
                externalForceDamping * deltaSeconds
            );

            Vector3 facingDirection = targetAvailable
                ? GetTargetPoint() - transform.position
                : velocity;
            RotateToward(facingDirection, deltaSeconds);

            if (config.IsFlying)
            {
                verticalSpeed = 0f;
            }
            else if (characterController != null)
            {
                verticalSpeed = characterController.isGrounded
                    ? -groundedStickSpeed
                    : verticalSpeed - gravity * deltaSeconds;
            }
            else
            {
                verticalSpeed = 0f;
            }

            Vector3 displacementVelocity = velocity + externalVelocity;
            if (!config.IsFlying)
            {
                displacementVelocity.y = verticalSpeed + externalVelocity.y;
            }

            if (characterController != null && characterController.enabled)
            {
                characterController.Move(displacementVelocity * deltaSeconds);
            }
            else
            {
                transform.position += displacementVelocity * deltaSeconds;
            }

            UpdateBankVisual(deltaSeconds);
        }

        private Vector3 GetDesiredMovement(bool targetAvailable, bool hasLineOfSight)
        {
            switch (config.MovementMode)
            {
                case EnemyMovementMode.Stationary:
                    return Vector3.zero;
                case EnemyMovementMode.GroundPatrol:
                    if (!targetAvailable)
                    {
                        return DirectionToPatrolPoint();
                    }
                    return DirectionForGroundCombat(hasLineOfSight, allowLateral: true);
                case EnemyMovementMode.GroundPursuit:
                    return targetAvailable
                        ? DirectionForGroundCombat(hasLineOfSight, allowLateral: false)
                        : Vector3.zero;
                case EnemyMovementMode.GroundSkirmish:
                    return targetAvailable
                        ? DirectionForGroundCombat(hasLineOfSight, allowLateral: true)
                        : Vector3.zero;
                case EnemyMovementMode.Flying:
                    return DirectionForFlight(targetAvailable, hasLineOfSight);
                default:
                    return Vector3.zero;
            }
        }

        private Vector3 DirectionToPatrolPoint()
        {
            float angle = locomotionClock * 0.45f + (strafeSign < 0 ? Mathf.PI : 0f);
            Vector3 patrolPoint = spawnAnchor + new Vector3(
                Mathf.Cos(angle) * config.PatrolRadius,
                0f,
                Mathf.Sin(angle) * config.PatrolRadius
            );
            return Vector3.ProjectOnPlane(patrolPoint - transform.position, Vector3.up);
        }

        private Vector3 DirectionForGroundCombat(bool hasLineOfSight, bool allowLateral)
        {
            Vector3 toTarget = Vector3.ProjectOnPlane(
                GetTargetPoint() - transform.position,
                Vector3.up
            );
            float distance = toTarget.magnitude;
            if (distance <= TimeEpsilon)
            {
                return Vector3.zero;
            }

            Vector3 forward = toTarget / distance;
            if (!hasLineOfSight || distance > config.PreferredMaximumDistance)
            {
                Vector3 lateral = allowLateral
                    ? Vector3.Cross(Vector3.up, forward) * strafeSign * config.LateralMovementStrength
                    : Vector3.zero;
                return (forward + lateral).normalized;
            }

            if (distance < config.PreferredMinimumDistance)
            {
                return -forward;
            }

            if (allowLateral && config.LateralMovementStrength > 0f)
            {
                return Vector3.Cross(Vector3.up, forward) * strafeSign;
            }

            return Vector3.zero;
        }

        private Vector3 DirectionForFlight(bool targetAvailable, bool hasLineOfSight)
        {
            float midpointAltitude =
                (config.MinimumFlightAltitude + config.MaximumFlightAltitude) * 0.5f;
            if (!targetAvailable)
            {
                float angle = locomotionClock * 0.38f + (strafeSign < 0 ? Mathf.PI : 0f);
                Vector3 homePoint = spawnAnchor + new Vector3(
                    Mathf.Cos(angle) * Mathf.Max(2f, config.PreferredMinimumDistance * 0.35f),
                    midpointAltitude,
                    Mathf.Sin(angle) * Mathf.Max(2f, config.PreferredMinimumDistance * 0.35f)
                );
                return homePoint - transform.position;
            }

            Vector3 targetPoint = GetTargetPoint();
            float desiredAltitude = Mathf.Clamp(
                targetPoint.y + midpointAltitude,
                spawnAnchor.y + config.MinimumFlightAltitude,
                spawnAnchor.y + config.MaximumFlightAltitude
            );
            Vector3 planarDelta = Vector3.ProjectOnPlane(
                targetPoint - transform.position,
                Vector3.up
            );
            float planarDistance = planarDelta.magnitude;
            Vector3 planarDirection = planarDistance > TimeEpsilon
                ? planarDelta / planarDistance
                : transform.forward;
            Vector3 result = Vector3.up * (desiredAltitude - transform.position.y);

            if (!hasLineOfSight || planarDistance > config.PreferredMaximumDistance)
            {
                result += planarDirection;
            }
            else if (planarDistance < config.PreferredMinimumDistance)
            {
                result -= planarDirection;
            }

            result += Vector3.Cross(Vector3.up, planarDirection)
                * strafeSign
                * config.LateralMovementStrength;
            return result;
        }

        private void RotateToward(Vector3 direction, float deltaSeconds)
        {
            Vector3 planar = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (planar.sqrMagnitude <= TimeEpsilon)
            {
                return;
            }

            Quaternion desiredRotation = Quaternion.LookRotation(planar.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                desiredRotation,
                config.TurnSpeedDegrees * deltaSeconds
            );
        }

        private void UpdateBankVisual(float deltaSeconds)
        {
            if (bankVisual == null)
            {
                return;
            }

            float bank = 0f;
            float effectiveMovementSpeed = config != null
                ? config.MovementSpeed * speedMultiplier
                : 0f;
            if (config != null && config.IsFlying && effectiveMovementSpeed > TimeEpsilon)
            {
                float lateralSpeed = Vector3.Dot(velocity, transform.right);
                bank = Mathf.Clamp(
                    -lateralSpeed / effectiveMovementSpeed,
                    -1f,
                    1f
                ) * maximumBankDegrees;
            }

            Quaternion desired = bankNeutralRotation * Quaternion.Euler(0f, 0f, bank);
            float blend = 1f - Mathf.Exp(-bankResponsiveness * deltaSeconds);
            bankVisual.localRotation = Quaternion.Slerp(
                bankVisual.localRotation,
                desired,
                blend
            );
        }

        private void ResetBankVisualImmediate()
        {
            if (bankVisual != null)
            {
                bankVisual.localRotation = bankNeutralRotation;
            }
        }

        private void CancelAttack()
        {
            attackPhase = AttackPhase.None;
            attackPhaseRemaining = 0f;
            burstShotIndex = 0;
        }

        private void CacheReferences()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }

            if (bankVisual != null)
            {
                bankNeutralRotation = bankVisual.localRotation;
            }
        }

        private Vector3 GetTargetPoint()
        {
            return target != null ? target.position + targetAimOffset : transform.position;
        }

        private Vector3 GetAttackOrigin()
        {
            return attackOrigin != null
                ? attackOrigin.position
                : transform.position + Vector3.up * 1.1f;
        }

        private static Vector3 ApplyDeterministicSpread(
            Vector3 direction,
            float maximumDegrees,
            int attackSequence,
            int shotIndex
        )
        {
            if (maximumDegrees <= TimeEpsilon)
            {
                return direction;
            }

            uint hash = unchecked((uint)(attackSequence * 73856093 ^ shotIndex * 19349663));
            float horizontal = ((hash & 1023u) / 1023f * 2f - 1f) * maximumDegrees;
            float vertical = (((hash >> 10) & 1023u) / 1023f * 2f - 1f)
                * maximumDegrees;
            return (Quaternion.AngleAxis(horizontal, Vector3.up)
                * Quaternion.AngleAxis(vertical, Vector3.right)
                * direction).normalized;
        }

        private static int StableSign(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 1;
            }

            uint hash = 2166136261u;
            for (int index = 0; index < value.Length; index++)
            {
                hash = (hash ^ value[index]) * 16777619u;
            }

            return (hash & 1u) == 0u ? 1 : -1;
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
            staggerDamageThreshold = Mathf.Max(0f, staggerDamageThreshold);
            damageStaggerSeconds = Mathf.Max(0f, damageStaggerSeconds);
            externalForceDamping = Mathf.Max(0f, externalForceDamping);
            maximumExternalSpeed = Mathf.Max(0.01f, maximumExternalSpeed);
            gravity = Mathf.Max(0f, gravity);
            groundedStickSpeed = Mathf.Max(0f, groundedStickSpeed);
            maximumBankDegrees = Mathf.Clamp(maximumBankDegrees, 0f, 45f);
            bankResponsiveness = Mathf.Max(0f, bankResponsiveness);
        }
    }
}
