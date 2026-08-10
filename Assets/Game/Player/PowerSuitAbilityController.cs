using Powersuit.Abilities;
using Powersuit.Abilities.UnityAdapters;
using Powersuit.Combat;
using UnityEngine;

/// <summary>
/// Thin player adapter which turns the shared input snapshot and camera ray
/// into the three authored ability transactions. Ability timing, cooldowns,
/// targeting validity, damage, and meter ownership remain in their testable
/// runtime/adaptor types; this component owns only player/world presentation.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class PowerSuitAbilityController : MonoBehaviour
{
    private const int TargetHitCapacity = 32;

    [Header("Player References")]
    [SerializeField] private PowerSuitController controller;
    [SerializeField] private PowerSuitInputRouter inputRouter;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PowerSuitWeapon weapon;

    [Header("Ability State Adapters")]
    [SerializeField] private ShoulderRocketAbility shoulderRocket;
    [SerializeField] private LightningStrikeAbility lightningStrike;
    [SerializeField] private VoidUltimateAbility voidUltimate;

    [Header("Presentation and Spawned Actors")]
    [SerializeField] private Transform shoulderMuzzle;
    [SerializeField] private AbilityTargetIndicator targetIndicator;
    [SerializeField] private ShoulderRocketProjectile rocketProjectilePrefab;
    [SerializeField] private LightningStrikeActor lightningActorPrefab;
    [SerializeField] private VoidOrbFieldActor voidFieldPrefab;
    [SerializeField, Min(0)] private int rocketPrewarmCount = 4;
    [SerializeField, Min(0)] private int lightningPrewarmCount = 2;
    [SerializeField, Min(0)] private int voidPrewarmCount = 1;

    [Header("Targeting")]
    [SerializeField] private LayerMask targetingMask = ~0;
    [SerializeField, Range(-1f, 1f)] private float minimumSurfaceUpDot = 0.2f;

    [Header("Ultimate Contribution")]
    [SerializeField, Min(0f)] private float meterGainPerDamage = 0.15f;
    [SerializeField, Min(0f)] private float meterGainPerKill = 20f;

    private readonly RaycastHit[] targetHits = new RaycastHit[TargetHitCapacity];
    private VoidOrbFieldActor activeVoidField;
    private int fallbackInputFrame = -1;
    private PowerSuitInputSnapshot fallbackInput;

    public ShoulderRocketAbility ShoulderRocket => shoulderRocket;
    public LightningStrikeAbility LightningStrike => lightningStrike;
    public VoidUltimateAbility VoidUltimate => voidUltimate;
    public bool CooldownsEnabled =>
        (shoulderRocket == null || shoulderRocket.CooldownsEnabled) &&
        (lightningStrike == null || lightningStrike.CooldownsEnabled);
    public bool IsTargeting =>
        lightningStrike != null && lightningStrike.IsTargeting;
    public AbilityTargetIndicator TargetIndicator => targetIndicator;
    public ShoulderRocketProjectile RocketProjectilePrefab =>
        rocketProjectilePrefab;
    public LightningStrikeActor LightningActorPrefab => lightningActorPrefab;
    public VoidOrbFieldActor VoidFieldPrefab => voidFieldPrefab;

    public Transform ShoulderMuzzle
    {
        get => shoulderMuzzle;
        set
        {
            shoulderMuzzle = value;
            if (shoulderRocket != null)
            {
                shoulderRocket.LaunchPoint = value;
            }
        }
    }

    private void Awake()
    {
        CacheReferences();
        if (shoulderRocket != null)
        {
            shoulderRocket.LaunchPoint = shoulderMuzzle;
        }
        if (targetIndicator != null)
        {
            targetIndicator.SetVisible(false);
        }
        PrewarmActors();
    }

    private void OnEnable()
    {
        CacheReferences();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        CancelTargeting();
    }

    private void Update()
    {
        if (playerHealth != null && playerHealth.IsDefeated)
        {
            CancelTargeting();
            return;
        }

        PowerSuitInputSnapshot input = ReadInput();
        if (input.CancelPressed)
        {
            CancelTargeting();
        }

        if (
            input.ShoulderRocketPressed &&
            shoulderRocket != null &&
            rocketProjectilePrefab != null
        )
        {
            Vector3 origin = shoulderMuzzle != null
                ? shoulderMuzzle.position
                : transform.position + Vector3.up * 1.5f;
            Vector3 aimPoint = controller != null
                ? controller.GetAimPoint(origin)
                : origin + transform.forward * 100f;
            shoulderRocket.TryLaunch(aimPoint);
        }

        if (
            input.LightningPressed &&
            lightningStrike != null &&
            lightningActorPrefab != null
        )
        {
            lightningStrike.TryBeginTargeting();
        }

        if (lightningStrike != null && lightningStrike.IsTargeting)
        {
            TargetProbe probe = ProbeTarget(lightningStrike.MaximumRange);
            AbilityTargetValidation validation = lightningStrike.UpdateTarget(
                probe.Origin,
                probe.Point,
                probe.Normal,
                probe.HasSurface,
                probe.IsObstructed
            );
            if (targetIndicator != null)
            {
                targetIndicator.SetTarget(
                    probe.Point,
                    probe.Normal,
                    lightningStrike.Radius,
                    validation.IsValid
                );
            }

            if (input.LightningReleased)
            {
                lightningStrike.ReleaseTargeting();
            }
        }

        if (
            input.UltimatePressed &&
            voidUltimate != null &&
            voidFieldPrefab != null
        )
        {
            TargetProbe probe = ProbeTarget(voidUltimate.MaximumRange);
            voidUltimate.TryActivate(
                probe.Origin,
                probe.Point,
                probe.Normal,
                probe.HasSurface,
                probe.IsObstructed
            );
        }
    }

    public void ResetAbilities(bool clearUltimateMeter = true)
    {
        shoulderRocket?.ResetAbility();
        lightningStrike?.ResetAbility();
        voidUltimate?.ResetAbility(clearUltimateMeter);
        if (activeVoidField != null)
        {
            activeVoidField.Cancel();
            activeVoidField = null;
        }
        if (targetIndicator != null)
        {
            targetIndicator.SetVisible(false);
        }
    }

    public void Configure(
        PowerSuitController playerController,
        PowerSuitInputRouter router,
        PlayerHealth health,
        PowerSuitWeapon playerWeapon,
        ShoulderRocketAbility rocket,
        LightningStrikeAbility lightning,
        VoidUltimateAbility ultimate,
        Transform rocketMuzzle,
        AbilityTargetIndicator indicator,
        ShoulderRocketProjectile rocketPrefab,
        LightningStrikeActor lightningPrefab,
        VoidOrbFieldActor voidPrefab
    )
    {
        controller = playerController;
        inputRouter = router;
        playerHealth = health;
        weapon = playerWeapon;
        shoulderRocket = rocket;
        lightningStrike = lightning;
        voidUltimate = ultimate;
        shoulderMuzzle = rocketMuzzle;
        targetIndicator = indicator;
        rocketProjectilePrefab = rocketPrefab;
        lightningActorPrefab = lightningPrefab;
        voidFieldPrefab = voidPrefab;
        if (shoulderRocket != null)
        {
            shoulderRocket.LaunchPoint = shoulderMuzzle;
        }
    }

    private void PrewarmActors()
    {
        if (rocketProjectilePrefab != null)
        {
            CombatFeedbackPool.Prewarm(
                rocketProjectilePrefab.gameObject,
                rocketPrewarmCount
            );
        }
        if (lightningActorPrefab != null)
        {
            CombatFeedbackPool.Prewarm(
                lightningActorPrefab.gameObject,
                lightningPrewarmCount
            );
        }
        if (voidFieldPrefab != null)
        {
            CombatFeedbackPool.Prewarm(
                voidFieldPrefab.gameObject,
                voidPrewarmCount
            );
        }
    }

    private void CacheReferences()
    {
        controller ??= GetComponent<PowerSuitController>();
        inputRouter ??= GetComponent<PowerSuitInputRouter>();
        playerHealth ??= GetComponent<PlayerHealth>();
        weapon ??= GetComponent<PowerSuitWeapon>();
        shoulderRocket ??= GetComponent<ShoulderRocketAbility>();
        lightningStrike ??= GetComponent<LightningStrikeAbility>();
        voidUltimate ??= GetComponent<VoidUltimateAbility>();
    }

    private void Subscribe()
    {
        Unsubscribe();
        if (shoulderRocket != null)
        {
            shoulderRocket.LaunchRequested += SpawnRocket;
        }
        if (lightningStrike != null)
        {
            lightningStrike.CastRequested += SpawnLightning;
            lightningStrike.TargetingCancelled += HideTargetIndicator;
        }
        if (voidUltimate != null)
        {
            voidUltimate.Activated += SpawnVoidField;
            voidUltimate.TickRequested += ApplyVoidTick;
            voidUltimate.FinalBurstRequested += ApplyVoidBurst;
            voidUltimate.Cancelled += CancelVoidField;
        }
        if (weapon != null)
        {
            weapon.DamageResolved += AwardWeaponContribution;
        }
        if (playerHealth != null)
        {
            playerHealth.OnDefeated += HandlePlayerDefeated;
            playerHealth.OnRespawned += HandlePlayerRespawned;
        }
    }

    private void Unsubscribe()
    {
        if (shoulderRocket != null)
        {
            shoulderRocket.LaunchRequested -= SpawnRocket;
        }
        if (lightningStrike != null)
        {
            lightningStrike.CastRequested -= SpawnLightning;
            lightningStrike.TargetingCancelled -= HideTargetIndicator;
        }
        if (voidUltimate != null)
        {
            voidUltimate.Activated -= SpawnVoidField;
            voidUltimate.TickRequested -= ApplyVoidTick;
            voidUltimate.FinalBurstRequested -= ApplyVoidBurst;
            voidUltimate.Cancelled -= CancelVoidField;
        }
        if (weapon != null)
        {
            weapon.DamageResolved -= AwardWeaponContribution;
        }
        if (playerHealth != null)
        {
            playerHealth.OnDefeated -= HandlePlayerDefeated;
            playerHealth.OnRespawned -= HandlePlayerRespawned;
        }
    }

    private void SpawnRocket(ShoulderRocketLaunchCommand command)
    {
        GameObject spawned = CombatFeedbackPool.Spawn(
            rocketProjectilePrefab.gameObject,
            command.Origin,
            Quaternion.LookRotation(command.Direction, Vector3.up)
        );
        ShoulderRocketProjectile actor = spawned != null
            ? spawned.GetComponent<ShoulderRocketProjectile>()
            : null;
        if (actor == null)
        {
            if (spawned != null)
            {
                CombatFeedbackPool.Recycle(spawned);
            }
            return;
        }

        actor.ExplosionResolved += AwardAreaContribution;
        actor.Initialize(command, transform);
    }

    private void SpawnLightning(LightningAreaCastCommand command)
    {
        HideTargetIndicator();
        GameObject spawned = CombatFeedbackPool.Spawn(
            lightningActorPrefab.gameObject,
            command.Center,
            Quaternion.identity
        );
        LightningStrikeActor actor = spawned != null
            ? spawned.GetComponent<LightningStrikeActor>()
            : null;
        if (actor == null)
        {
            if (spawned != null)
            {
                CombatFeedbackPool.Recycle(spawned);
            }
            return;
        }

        actor.StrikeResolved += AwardAreaContribution;
        actor.Initialize(command);
    }

    private void SpawnVoidField(VoidUltimateActivationCommand command)
    {
        CancelVoidField();
        GameObject spawned = CombatFeedbackPool.Spawn(
            voidFieldPrefab.gameObject,
            command.Center,
            Quaternion.identity
        );
        activeVoidField = spawned != null
            ? spawned.GetComponent<VoidOrbFieldActor>()
            : null;
        if (activeVoidField == null)
        {
            if (spawned != null)
            {
                CombatFeedbackPool.Recycle(spawned);
            }
            return;
        }

        activeVoidField.TickResolved += HandleVoidTickResolved;
        activeVoidField.BurstResolved += AwardAreaContribution;
        activeVoidField.Initialize(command);
    }

    private void ApplyVoidTick(VoidUltimateTickCommand command)
    {
        activeVoidField?.ApplyTick(command);
    }

    private void ApplyVoidBurst(VoidUltimateBurstCommand command)
    {
        if (activeVoidField == null)
        {
            return;
        }

        activeVoidField.ApplyFinalBurst(command);
        activeVoidField = null;
    }

    private void HandleVoidTickResolved(
        VoidUltimateTickCommand command,
        AbilityAreaEffectExecutionResult result
    )
    {
        AwardAreaContribution(result);
    }

    private void CancelVoidField()
    {
        if (activeVoidField != null)
        {
            activeVoidField.Cancel();
            activeVoidField = null;
        }
    }

    private void AwardWeaponContribution(DamageResult result)
    {
        if (!result.WasApplied)
        {
            return;
        }

        AwardContribution(result.AppliedAmount, result.WasKilled ? 1 : 0);
    }

    private void AwardAreaContribution(AbilityAreaEffectExecutionResult result)
    {
        AwardContribution(result.TotalAppliedDamage, result.KilledTargetCount);
    }

    private void AwardContribution(float damage, int kills)
    {
        if (voidUltimate == null)
        {
            return;
        }

        voidUltimate.GainMeter(
            Mathf.Max(0f, damage) * meterGainPerDamage +
            Mathf.Max(0, kills) * meterGainPerKill
        );
    }

    private void HandlePlayerDefeated()
    {
        ResetAbilities(clearUltimateMeter: true);
    }

    private void HandlePlayerRespawned()
    {
        ResetAbilities(clearUltimateMeter: true);
    }

    private void CancelTargeting()
    {
        lightningStrike?.CancelTargeting();
        HideTargetIndicator();
    }

    /// <summary>
    /// Enables or bypasses cooldown consumption for every cooldown-based suit
    /// ability. Disabling also clears any cooldown already in progress.
    /// </summary>
    public void SetCooldownsEnabled(bool isEnabled)
    {
        shoulderRocket?.SetCooldownsEnabled(isEnabled);
        lightningStrike?.SetCooldownsEnabled(isEnabled);
    }

    private void HideTargetIndicator()
    {
        if (targetIndicator != null)
        {
            targetIndicator.SetVisible(false);
        }
    }

    private TargetProbe ProbeTarget(float maximumRange)
    {
        Ray ray = controller != null
            ? controller.GetAimRay()
            : new Ray(
                transform.position + Vector3.up * 1.5f,
                transform.forward
            );
        Vector3 origin = transform.position + Vector3.up * 1.25f;
        int hitCount = Physics.RaycastNonAlloc(
            ray,
            targetHits,
            maximumRange,
            targetingMask,
            QueryTriggerInteraction.Ignore
        );
        float nearestDistance = float.PositiveInfinity;
        RaycastHit nearest = default;
        for (int index = 0; index < hitCount; index++)
        {
            RaycastHit hit = targetHits[index];
            if (
                hit.collider == null ||
                hit.transform == transform ||
                hit.transform.IsChildOf(transform) ||
                hit.distance >= nearestDistance
            )
            {
                continue;
            }

            nearest = hit;
            nearestDistance = hit.distance;
        }

        if (nearestDistance == float.PositiveInfinity)
        {
            return new TargetProbe(
                origin,
                ray.origin + ray.direction * maximumRange,
                Vector3.up,
                hasSurface: false,
                isObstructed: false
            );
        }

        Vector3 normal = nearest.normal.sqrMagnitude > 0.000001f
            ? nearest.normal.normalized
            : Vector3.zero;
        bool unsupportedSurface =
            normal.sqrMagnitude <= 0.000001f ||
            Vector3.Dot(normal, Vector3.up) < minimumSurfaceUpDot;
        return new TargetProbe(
            origin,
            nearest.point,
            normal,
            hasSurface: true,
            isObstructed: unsupportedSurface
        );
    }

    private PowerSuitInputSnapshot ReadInput()
    {
        if (
            inputRouter != null &&
            inputRouter.TryGetCurrentSnapshot(out PowerSuitInputSnapshot input)
        )
        {
            return input;
        }

        if (fallbackInputFrame != Time.frameCount)
        {
            fallbackInput = PowerSuitInputRouter.ReadFallbackSnapshot();
            fallbackInputFrame = Time.frameCount;
        }
        return fallbackInput;
    }

    private void OnValidate()
    {
        minimumSurfaceUpDot = Mathf.Clamp(minimumSurfaceUpDot, -1f, 1f);
        meterGainPerDamage = Mathf.Max(0f, meterGainPerDamage);
        meterGainPerKill = Mathf.Max(0f, meterGainPerKill);
        rocketPrewarmCount = Mathf.Max(0, rocketPrewarmCount);
        lightningPrewarmCount = Mathf.Max(0, lightningPrewarmCount);
        voidPrewarmCount = Mathf.Max(0, voidPrewarmCount);
    }

    private readonly struct TargetProbe
    {
        public TargetProbe(
            Vector3 origin,
            Vector3 point,
            Vector3 normal,
            bool hasSurface,
            bool isObstructed
        )
        {
            Origin = origin;
            Point = point;
            Normal = normal;
            HasSurface = hasSurface;
            IsObstructed = isObstructed;
        }

        public Vector3 Origin { get; }
        public Vector3 Point { get; }
        public Vector3 Normal { get; }
        public bool HasSurface { get; }
        public bool IsObstructed { get; }
    }
}
