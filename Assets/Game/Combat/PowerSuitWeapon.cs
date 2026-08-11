using System;
using System.Collections.Generic;
using Powersuit.Combat;
using UnityEngine;

public sealed class PowerSuitWeapon : MonoBehaviour
{
    public const float MinimumDamageMultiplier = 0f;
    public const float MaximumDamageMultiplier = 100f;
    public const float MaximumResolvedDamage = 1000000f;

    [Header("Weapon Configuration")]
    [SerializeField] private Transform muzzleTransform;
    [SerializeField] private PlayerProjectile projectilePrefab;
    [SerializeField] private WeaponDefinition weaponDefinition;
    [SerializeField, Min(0)] private int projectilePrewarmCount = 8;

    [Header("Legacy Projectile Parameters")]
    [Tooltip("Used only while no Weapon Definition is assigned.")]
    [SerializeField] private float damage = 25f;
    [Tooltip("Used only while no Weapon Definition is assigned.")]
    [SerializeField] private float projectileSpeed = 50f;
    [Tooltip("Used only while no Weapon Definition is assigned.")]
    [SerializeField] private float projectileLifetime = 4f;
    [Tooltip("Used only while no Weapon Definition is assigned.")]
    [SerializeField] private float projectileRadius = 0.15f;
    [Tooltip("Used only while no Weapon Definition is assigned.")]
    [SerializeField] private float shotsPerSecond = 5f;

    [Header("Muzzle Flash Feedback")]
    [SerializeField] private GameObject muzzleFlashPrefab;
    [SerializeField] private Color muzzleFlashColor = new Color(0.3f, 0.85f, 1f, 1f);
    [SerializeField] private float flashLightIntensity = 4f;
    [SerializeField] private float flashDuration = 0.05f;

    [Header("Recoil Settings")]
    [Tooltip("Used only while no Weapon Definition is assigned.")]
    [SerializeField] private float aimSpreadDegrees = 0f;
    [Tooltip("Used only while no Weapon Definition is assigned.")]
    [SerializeField] private float hipSpreadDegrees = 0f;
    [SerializeField] private float aimRecoilPitch = 1.2f;
    [SerializeField] private float aimRecoilYaw = 0.35f;
    [SerializeField] private float hipRecoilPitch = 0.7f;
    [SerializeField] private float hipRecoilYaw = 0.2f;

    [Header("Audio Feedback Hooks")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip fireSound;
    [SerializeField] private float minPitch = 0.95f;
    [SerializeField] private float maxPitch = 1.05f;

    [Header("Reticle Visuals")]
    [SerializeField] private Color normalCrosshairColor = Color.white;
    [SerializeField] private Color aimingReticleColor = new Color(0.2f, 0.9f, 1f, 1f);

    [Header("Legacy HUD")]
    [Tooltip(
        "Draws the old immediate-mode ammunition panel. Disable when a " +
        "PowerSuitHudPresenter owns the screen-space HUD."
    )]
    [SerializeField] private bool showLegacyAmmoHud = true;

    [Header("Runtime Tuning")]
    [SerializeField, Range(MinimumDamageMultiplier, MaximumDamageMultiplier)]
    private float damageMultiplier = 1f;

    private PowerSuitController controller;
    private PowerSuitWeaponAnimationDriver weaponAnimationDriver;
    private PowerSuitInputRouter inputRouter;
    private PowerSuitScopeSight scopeSight;
    private Camera playerCamera;
    private Light muzzleFlashLight;
    private float muzzleLightTimer;
    private WeaponRuntimeConfig activeConfiguration;
    private WeaponRuntimeState runtimeState;
    private GUIStyle ammoHudStyle;
    private GUIStyle ammoCountStyle;
    private GUIStyle ammoStatusStyle;
    private bool fireQueuedForForwardPose;
    private int fireQueuedFrame = -1;
    private int fallbackInputFrame = -1;
    private PowerSuitInputSnapshot fallbackInputSnapshot;
    private GameObject fallbackProjectileTemplate;
    private float reticleShotExpansion;
    private WeaponChargeState chargeState;
    private bool chargeReleaseAuthorized;
    private float queuedChargeDamageMultiplier = 1f;
    private float queuedChargeRadiusMultiplier = 1f;
    private readonly List<ParticleSystem> muzzleParticleBuffer =
        new List<ParticleSystem>(8);
    private readonly List<Light> muzzleLightBuffer = new List<Light>(4);

    public Transform MuzzleTransform
    {
        get => muzzleTransform;
        set => muzzleTransform = value;
    }

    public PlayerProjectile ProjectilePrefab
    {
        get => projectilePrefab;
        set => projectilePrefab = value;
    }

    public GameObject MuzzleFlashPrefab
    {
        get => muzzleFlashPrefab;
        set => muzzleFlashPrefab = value;
    }

    public WeaponDefinition Definition
    {
        get => weaponDefinition;
        set
        {
            if (weaponDefinition == value)
            {
                return;
            }

            if (Application.isPlaying)
            {
                if (value != null)
                {
                    EquipLoadoutWeapon(value, null);
                }
                else
                {
                    PrepareForUnequip();
                    weaponDefinition = null;
                    RebuildRuntimeState();
                    scopeSight?.Bind(controller, this);
                    WeaponEquipped?.Invoke(null);
                }
            }
            else
            {
                weaponDefinition = value;
            }
        }
    }

    public WeaponRuntimeConfig ActiveConfiguration => activeConfiguration;
    public WeaponRuntimeState RuntimeState => runtimeState;
    public int CurrentMagazineAmmo => runtimeState?.CurrentMagazineAmmo ?? 0;
    public int ReserveAmmo => runtimeState?.CurrentReserveAmmo ?? 0;
    public bool IsReloading => runtimeState != null && runtimeState.IsReloading;
    public bool IsCycling => runtimeState != null && runtimeState.IsManualCycleInProgress;
    public bool AutoReloadWhenEmpty =>
        weaponDefinition == null || weaponDefinition.AutoReloadWhenEmpty;
    public bool CanFire => CurrentFireBlockReason == WeaponFireBlockReason.None;
    public float DamageMultiplier => damageMultiplier;
    public int ProjectilePrewarmCount => projectilePrewarmCount;
    public bool ShowLegacyAmmoHud
    {
        get => showLegacyAmmoHud;
        set => showLegacyAmmoHud = value;
    }
    public WeaponReticleStyle CurrentReticleStyle =>
        weaponDefinition != null
            ? weaponDefinition.ReticleStyle
            : WeaponReticleStyle.PrecisionCross;
    public float CurrentReticleGapPixels =>
        Mathf.Max(
            1f,
            ResolveReticleBaseGap() + reticleShotExpansion - ChargeNormalized * 4f
        );
    public bool IsCharging => chargeState != null && chargeState.IsCharging;
    public float ChargeNormalized => chargeState?.NormalizedCharge ?? 0f;

    /// <summary>
    /// Sets the player weapon's outgoing damage multiplier. NaN preserves the
    /// current value and infinities clamp to the documented finite bounds.
    /// </summary>
    public float SetDamageMultiplier(float value)
    {
        damageMultiplier = ClampDamageMultiplier(value, damageMultiplier);
        return damageMultiplier;
    }

    /// <summary>
    /// Resolves authored shot damage through the runtime multiplier. Exposed
    /// so console tooling and tests can preview the exact gameplay result.
    /// </summary>
    public float CalculateOutgoingDamage(float authoredDamage)
    {
        if (
            float.IsNaN(authoredDamage) ||
            float.IsNegativeInfinity(authoredDamage) ||
            authoredDamage <= 0f
        )
        {
            return 0f;
        }

        if (float.IsPositiveInfinity(authoredDamage))
        {
            return damageMultiplier > 0f ? MaximumResolvedDamage : 0f;
        }

        return Mathf.Clamp(
            authoredDamage * damageMultiplier,
            0f,
            MaximumResolvedDamage
        );
    }

    /// <summary>
    /// Presentation systems can close this gate during draw, sheathe, or other states
    /// where gameplay firing would visibly disagree with the character pose.
    /// </summary>
    public bool PresentationAllowsFire { get; set; } = true;
    public bool PresentationAllowsReload { get; set; } = true;

    public WeaponFireBlockReason CurrentFireBlockReason
    {
        get
        {
            if (!PresentationAllowsFire)
            {
                return WeaponFireBlockReason.PresentationLocked;
            }

            return runtimeState?.CurrentFireBlockReason ?? WeaponFireBlockReason.FireCadence;
        }
    }

    public event Action<WeaponFireResult> ShotAccepted;
    public event Action<int, int> AmmunitionChanged;
    public event Action ReloadStarted;
    public event Action<int> ReloadCommitted;
    public event Action ReloadCompleted;
    public event Action ReloadCancelled;
    public event Action CycleStarted;
    public event Action CycleCompleted;
    public event Action CycleCancelled;
    public event Action<DamageResult> DamageResolved;
    public event Action<WeaponDefinition> WeaponEquipped;
    public event Action ChargeStarted;
    public event Action<float> ChargeReleased;
    public event Action ChargeCancelled;

    private void Awake()
    {
        damageMultiplier = ClampDamageMultiplier(damageMultiplier, 1f);
        projectilePrewarmCount = Mathf.Max(0, projectilePrewarmCount);
        RebuildRuntimeState();

        controller = GetComponent<PowerSuitController>();
        weaponAnimationDriver = GetComponent<PowerSuitWeaponAnimationDriver>();
        inputRouter = GetComponent<PowerSuitInputRouter>();
        playerCamera = Camera.main;

        scopeSight = GetComponent<PowerSuitScopeSight>();
        if (scopeSight == null)
        {
            scopeSight = gameObject.AddComponent<PowerSuitScopeSight>();
        }

        scopeSight.Bind(controller, this);

        if (playerCamera == null)
        {
            Debug.LogError("No Main Camera found.", this);
            enabled = false;
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        PlayerProjectile initialProjectilePrefab = ResolveActiveProjectilePrefab();
        if (initialProjectilePrefab != null && projectilePrewarmCount > 0)
        {
            CombatFeedbackPool.Prewarm(
                initialProjectilePrefab.gameObject,
                projectilePrewarmCount
            );
        }

        EnsureMuzzleFlashLight();
    }

    /// <summary>
    /// Equips one loadout slot while preserving the supplied slot's independent
    /// ammo and cadence state. Passing null creates a fresh runtime state and
    /// remains the compatibility path used by the Definition property.
    /// </summary>
    public WeaponRuntimeState EquipLoadoutWeapon(
        WeaponDefinition definition,
        WeaponRuntimeState state
    )
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        WeaponRuntimeConfig definitionConfiguration =
            definition.CreateRuntimeConfig();
        definitionConfiguration.ValidateOrThrow();
        if (
            state != null &&
            (
                state.Configuration.WeaponId !=
                    definitionConfiguration.WeaponId ||
                state.Configuration.WeaponClass !=
                    definitionConfiguration.WeaponClass
            )
        )
        {
            throw new ArgumentException(
                "The supplied runtime state does not match the weapon definition.",
                nameof(state)
            );
        }

        PrepareForUnequip();
        DetachRuntimeEvents();
        weaponDefinition = definition;
        runtimeState = state ?? new WeaponRuntimeState(
            definitionConfiguration,
            new UnityWeaponRandomSource()
        );
        activeConfiguration = runtimeState.Configuration;
        chargeState = definition.CreateChargeState();
        ResetChargeTransaction(cancelState: false);
        AttachRuntimeEvents();
        RaiseAmmunitionChanged();
        reticleShotExpansion = 0f;
        ApplyMuzzleFlashProfile();

        if (scopeSight == null)
        {
            scopeSight = GetComponent<PowerSuitScopeSight>();
        }
        scopeSight?.Bind(controller, this);
        controller?.RefreshAimAvailability();
        WeaponEquipped?.Invoke(definition);
        return runtimeState;
    }

    /// <summary>
    /// Clears queued feedback and cancelable actions before a slot switch while
    /// preserving ammunition and the authored fire cadence.
    /// </summary>
    public void PrepareForUnequip()
    {
        fireQueuedForForwardPose = false;
        fireQueuedFrame = -1;
        CancelCharge();
        runtimeState?.PrepareForUnequip();
        muzzleLightTimer = 0f;
        if (muzzleFlashLight != null)
        {
            muzzleFlashLight.enabled = false;
        }
    }

    public void PrewarmProjectiles(int count)
    {
        PrewarmProjectiles(ResolveActiveProjectilePrefab(), count);
    }

    public void PrewarmProjectiles(PlayerProjectile prefab, int count)
    {
        int requested = Mathf.Max(0, count);
        if (prefab != null && requested > 0)
        {
            CombatFeedbackPool.Prewarm(prefab.gameObject, requested);
        }
    }

    private void OnDestroy()
    {
        DetachRuntimeEvents();
        if (fallbackProjectileTemplate != null)
        {
            Destroy(fallbackProjectileTemplate);
            fallbackProjectileTemplate = null;
        }
    }

    private void Update()
    {
        runtimeState?.Advance(Time.deltaTime);
        PowerSuitInputSnapshot input = ReadInputSnapshot();

        if (reticleShotExpansion > 0f)
        {
            float sharpness = weaponDefinition != null
                ? weaponDefinition.ReticleRecoverySharpness
                : 18f;
            reticleShotExpansion *= Mathf.Exp(-sharpness * Time.deltaTime);
            if (reticleShotExpansion < 0.01f)
            {
                reticleShotExpansion = 0f;
            }
        }

        if (input.ReloadPressed)
        {
            TryStartReload();
        }
        else
        {
            TryStartAutomaticReload();
        }

        if (
            fireQueuedForForwardPose &&
            Time.frameCount > fireQueuedFrame
        )
        {
            fireQueuedForForwardPose = false;
            fireQueuedFrame = -1;
            TryFireWeapon();
        }
        else if (weaponDefinition != null && weaponDefinition.UsesChargeShot)
        {
            HandleChargeInput(input);
        }
        else if (IsFireRequested(input))
        {
            RequestFire();
        }

        if (muzzleFlashLight != null && muzzleLightTimer > 0f)
        {
            muzzleLightTimer -= Time.deltaTime;
            if (muzzleLightTimer <= 0f)
            {
                muzzleFlashLight.enabled = false;
            }
        }
    }

    private void OnDisable()
    {
        fireQueuedForForwardPose = false;
        fireQueuedFrame = -1;
        CancelCharge();
    }

    /// <summary>
    /// Requests a gameplay shot. A shot is staged for one Animator evaluation
    /// whenever the forward pose is not already held, including the first frame
    /// of an aim transition. Projectile and muzzle feedback therefore never
    /// sample the diagonal carry pose. Returns true when the request fired
    /// immediately or was accepted for staging.
    /// </summary>
    public bool RequestFire()
    {
        if (fireQueuedForForwardPose)
        {
            return false;
        }

        if (
            weaponDefinition != null &&
            weaponDefinition.UsesChargeShot &&
            !chargeReleaseAuthorized
        )
        {
            return false;
        }

        bool queuedForForwardPose =
            controller != null &&
            CanFire &&
            weaponAnimationDriver != null &&
            !weaponAnimationDriver.IsForwardWeaponPoseReady(
                controller.IsAiming
            ) &&
            weaponAnimationDriver.PrepareForwardWeaponPose();
        if (!queuedForForwardPose)
        {
            return TryFireWeapon().Fired;
        }

        controller.FaceCameraForWeaponFire();
        fireQueuedForForwardPose = true;
        fireQueuedFrame = Time.frameCount;
        return true;
    }

    /// <summary>
    /// Immediately attempts the gameplay transaction. Callers that have not
    /// already prepared a firing pose should use <see cref="RequestFire"/>.
    /// </summary>
    public WeaponFireResult TryFireWeapon()
    {
        EnsureRuntimeState();

        if (!PresentationAllowsFire)
        {
            return WeaponFireResult.Blocked(
                WeaponFireBlockReason.PresentationLocked,
                CurrentMagazineAmmo
            );
        }

        WeaponFireResult result = runtimeState.TryFire();
        if (!result.Fired)
        {
            ResetChargeTransaction(cancelState: false);
            return result;
        }

        float chargeDamageMultiplier = queuedChargeDamageMultiplier;
        float chargeRadiusMultiplier = queuedChargeRadiusMultiplier;
        ResetChargeTransaction(cancelState: false);
        controller?.FaceCameraForWeaponFire();
        FireProjectileAndFeedback(
            result,
            chargeDamageMultiplier,
            chargeRadiusMultiplier
        );
        if (weaponDefinition != null)
        {
            reticleShotExpansion = Mathf.Max(
                reticleShotExpansion,
                weaponDefinition.ReticleShotExpansionPixels
            );
        }
        ShotAccepted?.Invoke(result);
        return result;
    }

    public WeaponReloadStartResult TryStartReload()
    {
        EnsureRuntimeState();
        if (!PresentationAllowsReload)
        {
            return WeaponReloadStartResult.PresentationLocked;
        }

        WeaponReloadStartResult result = runtimeState.TryStartReload();
        if (result == WeaponReloadStartResult.Started)
        {
            CancelCharge();
        }
        return result;
    }

    private void TryStartAutomaticReload()
    {
        if (
            !AutoReloadWhenEmpty ||
            !PresentationAllowsReload ||
            runtimeState == null ||
            !runtimeState.CanStartAutomaticReload
        )
        {
            return;
        }

        runtimeState.TryStartReload();
    }

    public bool CancelReload()
    {
        return runtimeState != null && runtimeState.CancelReload();
    }

    public bool CommitReloadFromAnimation()
    {
        return runtimeState != null && runtimeState.CommitReload();
    }

    public bool CompleteReloadFromAnimation()
    {
        return runtimeState != null && runtimeState.CompleteReload();
    }

    public bool CompleteCycleFromAnimation()
    {
        return runtimeState != null && runtimeState.CompleteManualCycle();
    }

    public int AddReserveAmmo(int amount)
    {
        EnsureRuntimeState();
        return runtimeState.AddReserveAmmo(amount);
    }

    /// <summary>
    /// Cancels transient combat and feedback state for a clean respawn without
    /// silently refilling or otherwise changing the equipped weapon's ammo.
    /// </summary>
    public void ResetForRespawn()
    {
        fireQueuedForForwardPose = false;
        fireQueuedFrame = -1;
        CancelCharge();
        runtimeState?.ResetTransientState();

        muzzleLightTimer = 0f;
        if (muzzleFlashLight != null)
        {
            muzzleFlashLight.enabled = false;
        }

        PresentationAllowsFire = true;
        PresentationAllowsReload = true;

        // The animation driver owns nonserialized action/hold state. Cycling
        // it through its normal lifecycle clears those values and restores its
        // subscriptions without exposing Animator internals to combat logic.
        if (weaponAnimationDriver != null && weaponAnimationDriver.enabled)
        {
            weaponAnimationDriver.enabled = false;
            weaponAnimationDriver.enabled = true;
        }
    }

    private void FireProjectileAndFeedback(
        WeaponFireResult result,
        float chargeDamageMultiplier = 1f,
        float chargeRadiusMultiplier = 1f
    )
    {
        float resolvedDamage = CalculateOutgoingDamage(
            result.Damage * Mathf.Max(0.01f, chargeDamageMultiplier)
        );
        Vector3 muzzlePos = GetMuzzlePosition();
        Vector3 aimPoint = controller != null
            ? controller.GetAimPoint(muzzlePos)
            : playerCamera != null
                ? playerCamera.transform.position + playerCamera.transform.forward * 100f
                : muzzlePos + transform.forward * 100f;

        Vector3 fireDirection = (aimPoint - muzzlePos).normalized;
        if (fireDirection.sqrMagnitude < 0.001f)
        {
            fireDirection = transform.forward;
        }

        bool isAiming = controller != null && controller.IsAiming;
        fireDirection = ApplySpread(
            fireDirection,
            isAiming
                ? activeConfiguration.AimSpreadDegrees
                : activeConfiguration.HipSpreadDegrees
        );

        Quaternion muzzleRot = Quaternion.LookRotation(fireDirection, Vector3.up);

        PlayerProjectile activeProjectilePrefab = ResolveActiveProjectilePrefab();
        if (activeProjectilePrefab != null)
        {
            GameObject projectileObject = CombatFeedbackPool.Spawn(
                activeProjectilePrefab.gameObject,
                muzzlePos,
                Quaternion.LookRotation(fireDirection)
            );
            PlayerProjectile proj = projectileObject != null
                ? projectileObject.GetComponent<PlayerProjectile>()
                : null;
            if (proj != null)
            {
                proj.DamageResolved += RaiseDamageResolved;
                proj.Initialize(
                    fireDirection,
                    activeConfiguration.ProjectileSpeed,
                    resolvedDamage,
                    activeConfiguration.ProjectileLifetimeSeconds,
                    activeConfiguration.ProjectileRadius,
                    transform,
                    result.IsCritical,
                    weaponDefinition != null
                        ? weaponDefinition.ProjectileDamageType
                        : DamageType.Kinetic,
                    weaponDefinition != null
                        ? weaponDefinition.SplashDamageRadius *
                            Mathf.Max(0.01f, chargeRadiusMultiplier)
                        : 0f,
                    weaponDefinition != null
                        ? weaponDefinition.SplashMinimumDamageMultiplier
                        : 0.35f,
                    weaponDefinition != null
                        ? weaponDefinition.SplashImpulse
                        : 0f,
                    weaponDefinition != null
                        ? weaponDefinition.SplashStaggerSeconds
                        : 0f
                );
            }
            else if (projectileObject != null)
            {
                Debug.LogError(
                    "The configured projectile prefab has no PlayerProjectile component.",
                    projectileObject
                );
                CombatFeedbackPool.Recycle(projectileObject);
            }
        }
        else
        {
            SpawnFallbackProjectile(
                muzzlePos,
                fireDirection,
                resolvedDamage,
                result.IsCritical,
                chargeRadiusMultiplier
            );
        }

        TriggerMuzzleFlash(muzzlePos, muzzleRot);

        if (controller != null)
        {
            float pitch = isAiming
                ? activeConfiguration.AimRecoilPitch
                : activeConfiguration.HipRecoilPitch;
            float yaw = isAiming
                ? activeConfiguration.AimRecoilYaw
                : activeConfiguration.HipRecoilYaw;
            controller.AddRecoil(pitch, yaw);
        }

        PlayFireAudio();
    }

    private void RebuildRuntimeState()
    {
        DetachRuntimeEvents();
        activeConfiguration = ResolveConfiguration();
        runtimeState = new WeaponRuntimeState(
            activeConfiguration,
            new UnityWeaponRandomSource()
        );
        chargeState = weaponDefinition?.CreateChargeState();
        ResetChargeTransaction(cancelState: false);
        AttachRuntimeEvents();
        RaiseAmmunitionChanged();
    }

    private WeaponRuntimeConfig ResolveConfiguration()
    {
        WeaponRuntimeConfig candidate = weaponDefinition != null
            ? weaponDefinition.CreateRuntimeConfig()
            : CreateLegacyConfiguration();

        if (candidate.GetValidationErrors().Count == 0)
        {
            return candidate;
        }

        Debug.LogError(
            $"Weapon Definition '{weaponDefinition?.name ?? "<legacy>"}' is invalid: " +
            string.Join(" ", candidate.GetValidationErrors()) +
            " Falling back to the legacy component values.",
            this
        );

        return CreateLegacyConfiguration();
    }

    private WeaponRuntimeConfig CreateLegacyConfiguration()
    {
        return WeaponRuntimeConfig.CreateLegacyInfiniteAmmo(
            baseDamage: PositiveFiniteOrDefault(damage, 25f),
            shotsPerSecond: PositiveFiniteOrDefault(shotsPerSecond, 5f),
            projectileSpeed: PositiveFiniteOrDefault(projectileSpeed, 50f),
            projectileLifetimeSeconds: PositiveFiniteOrDefault(projectileLifetime, 4f),
            projectileRadius: PositiveFiniteOrDefault(projectileRadius, 0.15f),
            aimSpreadDegrees: NonNegativeFiniteOrDefault(aimSpreadDegrees, 0f),
            hipSpreadDegrees: NonNegativeFiniteOrDefault(hipSpreadDegrees, 0f),
            aimRecoilPitch: NonNegativeFiniteOrDefault(aimRecoilPitch, 1.2f),
            aimRecoilYaw: NonNegativeFiniteOrDefault(aimRecoilYaw, 0.35f),
            hipRecoilPitch: NonNegativeFiniteOrDefault(hipRecoilPitch, 0.7f),
            hipRecoilYaw: NonNegativeFiniteOrDefault(hipRecoilYaw, 0.2f)
        );
    }

    private static float PositiveFiniteOrDefault(float value, float fallback)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f
            ? value
            : fallback;
    }

    private static float NonNegativeFiniteOrDefault(float value, float fallback)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f
            ? value
            : fallback;
    }

    private static float ClampDamageMultiplier(float value, float fallback)
    {
        if (float.IsNaN(value))
        {
            return fallback;
        }

        if (float.IsPositiveInfinity(value))
        {
            return MaximumDamageMultiplier;
        }

        if (float.IsNegativeInfinity(value))
        {
            return MinimumDamageMultiplier;
        }

        return Mathf.Clamp(
            value,
            MinimumDamageMultiplier,
            MaximumDamageMultiplier
        );
    }

    private void EnsureRuntimeState()
    {
        if (runtimeState == null)
        {
            RebuildRuntimeState();
        }
    }

    private void AttachRuntimeEvents()
    {
        if (runtimeState == null)
        {
            return;
        }

        runtimeState.AmmunitionChanged += RaiseAmmunitionChanged;
        runtimeState.ReloadStarted += HandleReloadStarted;
        runtimeState.ReloadAmmoCommitted += HandleReloadCommitted;
        runtimeState.ReloadCompleted += HandleReloadCompleted;
        runtimeState.ReloadCancelled += HandleReloadCancelled;
        runtimeState.ManualCycleStarted += HandleCycleStarted;
        runtimeState.ManualCycleCompleted += HandleCycleCompleted;
        runtimeState.ManualCycleCancelled += HandleCycleCancelled;
    }

    private void DetachRuntimeEvents()
    {
        if (runtimeState == null)
        {
            return;
        }

        runtimeState.AmmunitionChanged -= RaiseAmmunitionChanged;
        runtimeState.ReloadStarted -= HandleReloadStarted;
        runtimeState.ReloadAmmoCommitted -= HandleReloadCommitted;
        runtimeState.ReloadCompleted -= HandleReloadCompleted;
        runtimeState.ReloadCancelled -= HandleReloadCancelled;
        runtimeState.ManualCycleStarted -= HandleCycleStarted;
        runtimeState.ManualCycleCompleted -= HandleCycleCompleted;
        runtimeState.ManualCycleCancelled -= HandleCycleCancelled;
    }

    private void RaiseAmmunitionChanged()
    {
        AmmunitionChanged?.Invoke(CurrentMagazineAmmo, ReserveAmmo);
    }

    private void HandleReloadStarted()
    {
        ReloadStarted?.Invoke();
    }

    private void HandleReloadCommitted(int roundsTransferred)
    {
        ReloadCommitted?.Invoke(roundsTransferred);
    }

    private void HandleReloadCompleted()
    {
        ReloadCompleted?.Invoke();
    }

    private void HandleReloadCancelled()
    {
        ReloadCancelled?.Invoke();
    }

    private void HandleCycleStarted()
    {
        CycleStarted?.Invoke();
    }

    private void HandleCycleCompleted()
    {
        CycleCompleted?.Invoke();
    }

    private void HandleCycleCancelled()
    {
        CycleCancelled?.Invoke();
    }

    private void TriggerMuzzleFlash(Vector3 position, Quaternion rotation)
    {
        if (muzzleFlashPrefab != null)
        {
            GameObject flashObj = CombatFeedbackPool.Spawn(
                muzzleFlashPrefab,
                position,
                rotation
            );
            if (muzzleTransform != null && flashObj != null)
            {
                flashObj.transform.SetParent(muzzleTransform, true);
            }
            ApplyMuzzleFlashColor(flashObj);
        }

        if (muzzleFlashLight != null)
        {
            muzzleFlashLight.transform.position = position;
            ApplyMuzzleFlashProfile();
            muzzleFlashLight.enabled = true;
            muzzleLightTimer = ResolveMuzzleFlashDuration();
        }
    }

    private void EnsureMuzzleFlashLight()
    {
        Transform muzzle = muzzleTransform ?? transform;
        Transform lightTrans = muzzle.Find("MuzzleFlashLight");
        if (lightTrans != null)
        {
            muzzleFlashLight = lightTrans.GetComponent<Light>();
        }

        if (muzzleFlashLight == null)
        {
            GameObject lightObj = new GameObject("MuzzleFlashLight");
            lightObj.transform.SetParent(muzzle, false);
            lightObj.transform.localPosition = Vector3.zero;

            muzzleFlashLight = lightObj.AddComponent<Light>();
            muzzleFlashLight.type = LightType.Point;
            muzzleFlashLight.range = 4f;
            muzzleFlashLight.color = muzzleFlashColor;
            muzzleFlashLight.intensity = flashLightIntensity;
            muzzleFlashLight.enabled = false;
        }

        ApplyMuzzleFlashProfile();
    }

    private void ApplyMuzzleFlashProfile()
    {
        if (muzzleFlashLight == null)
        {
            return;
        }

        muzzleFlashLight.color = weaponDefinition != null
            ? weaponDefinition.MuzzleFlashColor
            : muzzleFlashColor;
        muzzleFlashLight.intensity = weaponDefinition != null
            ? weaponDefinition.MuzzleFlashIntensity
            : flashLightIntensity;
    }

    private void ApplyMuzzleFlashColor(GameObject flashObject)
    {
        if (flashObject == null)
        {
            return;
        }

        Color color = weaponDefinition != null
            ? weaponDefinition.MuzzleFlashColor
            : muzzleFlashColor;
        muzzleParticleBuffer.Clear();
        flashObject.GetComponentsInChildren(true, muzzleParticleBuffer);
        for (int index = 0; index < muzzleParticleBuffer.Count; index++)
        {
            ParticleSystem.MainModule main = muzzleParticleBuffer[index].main;
            main.startColor = color;
        }

        muzzleLightBuffer.Clear();
        flashObject.GetComponentsInChildren(true, muzzleLightBuffer);
        for (int index = 0; index < muzzleLightBuffer.Count; index++)
        {
            muzzleLightBuffer[index].color = color;
        }
    }

    private float ResolveMuzzleFlashDuration()
    {
        return weaponDefinition != null
            ? weaponDefinition.MuzzleFlashDuration
            : Mathf.Max(0.01f, flashDuration);
    }

    private void PlayFireAudio()
    {
        if (audioSource != null && fireSound != null)
        {
            audioSource.pitch = UnityEngine.Random.Range(minPitch, maxPitch);
            audioSource.PlayOneShot(fireSound);
        }
    }

    private static Vector3 ApplySpread(Vector3 direction, float spreadDegrees)
    {
        if (spreadDegrees <= 0f)
        {
            return direction;
        }

        Vector2 spread = UnityEngine.Random.insideUnitCircle * spreadDegrees;
        Quaternion baseRotation = Quaternion.LookRotation(direction, Vector3.up);
        Quaternion spreadRotation = Quaternion.Euler(-spread.y, spread.x, 0f);
        return (baseRotation * spreadRotation * Vector3.forward).normalized;
    }

    private Vector3 GetMuzzlePosition()
    {
        if (muzzleTransform != null)
        {
            return muzzleTransform.position;
        }

        return transform.position + Vector3.up * 1.35f + transform.forward * 0.6f;
    }

    private void SpawnFallbackProjectile(
        Vector3 position,
        Vector3 direction,
        float resolvedDamage,
        bool isCritical,
        float chargeRadiusMultiplier
    )
    {
        GameObject template = GetOrCreateFallbackProjectileTemplate();
        GameObject projObj = CombatFeedbackPool.Spawn(
            template,
            position,
            Quaternion.LookRotation(direction)
        );
        if (projObj == null)
        {
            return;
        }

        projObj.transform.localScale =
            Vector3.one * (activeConfiguration.ProjectileRadius * 2f);

        PlayerProjectile proj = projObj.GetComponent<PlayerProjectile>();
        if (proj == null)
        {
            CombatFeedbackPool.Recycle(projObj);
            return;
        }

        proj.Initialize(
            direction,
            activeConfiguration.ProjectileSpeed,
            resolvedDamage,
            activeConfiguration.ProjectileLifetimeSeconds,
            activeConfiguration.ProjectileRadius,
            transform,
            isCritical,
            weaponDefinition != null
                ? weaponDefinition.ProjectileDamageType
                : DamageType.Kinetic,
            weaponDefinition != null
                ? weaponDefinition.SplashDamageRadius *
                    Mathf.Max(0.01f, chargeRadiusMultiplier)
                : 0f,
            weaponDefinition != null
                ? weaponDefinition.SplashMinimumDamageMultiplier
                : 0.35f,
            weaponDefinition != null
                ? weaponDefinition.SplashImpulse
                : 0f,
            weaponDefinition != null
                ? weaponDefinition.SplashStaggerSeconds
                : 0f
        );
        proj.DamageResolved += RaiseDamageResolved;
    }

    private void RaiseDamageResolved(DamageResult result)
    {
        DamageResolved?.Invoke(result);
    }

    private GameObject GetOrCreateFallbackProjectileTemplate()
    {
        if (fallbackProjectileTemplate != null)
        {
            return fallbackProjectileTemplate;
        }

        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        template.name = "PlayerProjectileFallbackTemplate";
        template.hideFlags = HideFlags.HideAndDontSave;

        Collider collider = template.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
        }

        Renderer rend = template.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material.color = aimingReticleColor;
        }

        template.AddComponent<PlayerProjectile>();
        template.SetActive(false);
        fallbackProjectileTemplate = template;
        return fallbackProjectileTemplate;
    }

    private bool IsFireRequested(PowerSuitInputSnapshot input)
    {
        if (controller != null && controller.IsPrimaryFireSuppressed)
        {
            return false;
        }

        bool semiAutomatic =
            activeConfiguration?.TriggerMode == WeaponTriggerMode.SemiAutomatic;
        return semiAutomatic
            ? input.FirePressed
            : input.FireHeld;
    }

    private void HandleChargeInput(PowerSuitInputSnapshot input)
    {
        if (chargeState == null)
        {
            return;
        }

        if (
            (controller != null && controller.IsPrimaryFireSuppressed) ||
            !PresentationAllowsFire
        )
        {
            CancelCharge();
            return;
        }

        if (!chargeState.IsCharging)
        {
            if (input.FirePressed && CanFire && chargeState.Begin())
            {
                ChargeStarted?.Invoke();
            }
            return;
        }

        if (input.FireHeld)
        {
            chargeState.Advance(Time.deltaTime);
        }

        if (!input.FireReleased && input.FireHeld)
        {
            return;
        }

        WeaponChargeReleaseResult release = chargeState.Release();
        ChargeReleased?.Invoke(release.NormalizedCharge);
        if (!release.ShouldFire)
        {
            ResetChargeTransaction(cancelState: false);
            return;
        }

        queuedChargeDamageMultiplier = release.DamageMultiplier;
        queuedChargeRadiusMultiplier = release.RadiusMultiplier;
        chargeReleaseAuthorized = true;
        if (!RequestFire())
        {
            ResetChargeTransaction(cancelState: false);
        }
    }

    private bool CancelCharge()
    {
        bool cancelled = chargeState != null && chargeState.Cancel();
        ResetChargeTransaction(cancelState: false);
        if (cancelled)
        {
            ChargeCancelled?.Invoke();
        }
        return cancelled;
    }

    private void ResetChargeTransaction(bool cancelState)
    {
        if (cancelState)
        {
            chargeState?.Cancel();
        }
        chargeReleaseAuthorized = false;
        queuedChargeDamageMultiplier = 1f;
        queuedChargeRadiusMultiplier = 1f;
    }

    private PlayerProjectile ResolveActiveProjectilePrefab()
    {
        return weaponDefinition != null &&
            weaponDefinition.ProjectilePrefabOverride != null
                ? weaponDefinition.ProjectilePrefabOverride
                : projectilePrefab;
    }

    private PowerSuitInputSnapshot ReadInputSnapshot()
    {
        if (
            inputRouter != null &&
            inputRouter.TryGetCurrentSnapshot(
                out PowerSuitInputSnapshot routedInput
            )
        )
        {
            return routedInput;
        }

        int frame = Time.frameCount;
        if (fallbackInputFrame != frame)
        {
            fallbackInputSnapshot =
                PowerSuitInputRouter.ReadFallbackSnapshot();
            fallbackInputFrame = frame;
        }

        return fallbackInputSnapshot;
    }

    private void OnGUI()
    {
        if (controller == null)
        {
            if (showLegacyAmmoHud)
            {
                DrawAmmoHud();
            }
            return;
        }

        if (controller.IsScoped || controller.ScopeBlend > 0.01f)
        {
            if (showLegacyAmmoHud)
            {
                DrawAmmoHud();
            }

            return;
        }

        bool isAiming = controller.IsAiming;
        Vector2 reticlePos = isAiming
            ? controller.ReticleScreenPosition
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        float guiX = reticlePos.x;
        float guiY = Screen.height - reticlePos.y;

        Color savedColor = GUI.color;
        GUI.color = ResolveReticleColor(isAiming);
        DrawWeaponReticle(guiX, guiY, isAiming);

        GUI.color = savedColor;
        if (showLegacyAmmoHud)
        {
            DrawAmmoHud();
        }
    }

    private void DrawWeaponReticle(float guiX, float guiY, bool isAiming)
    {
        float thickness = CurrentReticleStyle switch
        {
            WeaponReticleStyle.AssaultDynamic => 2.5f,
            WeaponReticleStyle.HeavyCharge => 3f,
            _ => 2f
        };
        float armLength = weaponDefinition != null
            ? weaponDefinition.ReticleArmLengthPixels
            : (isAiming ? 12f : 8f);
        float gap = CurrentReticleGapPixels;

        GUI.DrawTexture(
            new Rect(
                guiX - thickness * 0.5f,
                guiY - gap - armLength,
                thickness,
                armLength
            ),
            Texture2D.whiteTexture
        );
        GUI.DrawTexture(
            new Rect(guiX - thickness * 0.5f, guiY + gap, thickness, armLength),
            Texture2D.whiteTexture
        );
        GUI.DrawTexture(
            new Rect(
                guiX - gap - armLength,
                guiY - thickness * 0.5f,
                armLength,
                thickness
            ),
            Texture2D.whiteTexture
        );
        GUI.DrawTexture(
            new Rect(guiX + gap, guiY - thickness * 0.5f, armLength, thickness),
            Texture2D.whiteTexture
        );

        float centerSize = CurrentReticleStyle switch
        {
            WeaponReticleStyle.AssaultDynamic => 2f,
            WeaponReticleStyle.HeavyCharge => 4f + ChargeNormalized * 3f,
            _ => 3f
        };
        GUI.DrawTexture(
            new Rect(
                guiX - centerSize * 0.5f,
                guiY - centerSize * 0.5f,
                centerSize,
                centerSize
            ),
            Texture2D.whiteTexture
        );
    }

    private float ResolveReticleBaseGap()
    {
        if (weaponDefinition != null)
        {
            return weaponDefinition.ReticleBaseGapPixels;
        }

        return controller != null && controller.IsAiming ? 4f : 0f;
    }

    private Color ResolveReticleColor(bool isAiming)
    {
        if (weaponDefinition != null)
        {
            return weaponDefinition.ReticleColor;
        }

        return isAiming ? aimingReticleColor : normalCrosshairColor;
    }

    private void DrawAmmoHud()
    {
        if (activeConfiguration == null || activeConfiguration.UsesInfiniteAmmo)
        {
            return;
        }

        EnsureAmmoHudStyles();

        const float width = 190f;
        const float height = 78f;
        const float margin = 18f;
        Rect panel = new Rect(
            Screen.width - width - margin,
            Screen.height - height - margin,
            width,
            height
        );

        string status;
        Color statusColor;
        if (IsReloading)
        {
            status = "RELOADING";
            statusColor = new Color(0.3f, 0.9f, 1f, 1f);
        }
        else if (IsCycling)
        {
            status = "CYCLING";
            statusColor = new Color(1f, 0.8f, 0.3f, 1f);
        }
        else if (CurrentMagazineAmmo <= 0)
        {
            status = "EMPTY - PRESS R";
            statusColor = new Color(1f, 0.35f, 0.25f, 1f);
        }
        else
        {
            status = "READY";
            statusColor = new Color(0.6f, 1f, 0.65f, 1f);
        }

        GUI.Box(panel, activeConfiguration.DisplayName, ammoHudStyle);
        GUI.Label(
            new Rect(panel.x + 10f, panel.y + 22f, panel.width - 20f, 30f),
            $"{CurrentMagazineAmmo} / {ReserveAmmo}",
            ammoCountStyle
        );

        Color previousStatusColor = ammoStatusStyle.normal.textColor;
        ammoStatusStyle.normal.textColor = statusColor;
        GUI.Label(
            new Rect(panel.x + 10f, panel.y + 53f, panel.width - 20f, 18f),
            status,
            ammoStatusStyle
        );
        ammoStatusStyle.normal.textColor = previousStatusColor;
    }

    private void EnsureAmmoHudStyles()
    {
        if (ammoHudStyle != null)
        {
            return;
        }

        ammoHudStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(10, 10, 4, 4)
        };
        ammoHudStyle.normal.textColor = new Color(0.75f, 0.95f, 1f, 1f);

        ammoCountStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize = 22,
            fontStyle = FontStyle.Bold
        };
        ammoCountStyle.normal.textColor = Color.white;

        ammoStatusStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize = 11,
            fontStyle = FontStyle.Bold
        };
    }

    private sealed class UnityWeaponRandomSource : IWeaponRandomSource
    {
        public double NextUnitValue()
        {
            return UnityEngine.Random.value;
        }
    }
}
