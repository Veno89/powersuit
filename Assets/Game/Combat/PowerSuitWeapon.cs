using System;
using Powersuit.Combat;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class PowerSuitWeapon : MonoBehaviour
{
    [Header("Weapon Configuration")]
    [SerializeField] private Transform muzzleTransform;
    [SerializeField] private PlayerProjectile projectilePrefab;
    [SerializeField] private WeaponDefinition weaponDefinition;

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

    private PowerSuitController controller;
    private PowerSuitWeaponAnimationDriver weaponAnimationDriver;
    private Camera playerCamera;
    private Light muzzleFlashLight;
    private float muzzleLightTimer;
    private WeaponRuntimeConfig activeConfiguration;
    private WeaponRuntimeState runtimeState;
    private GUIStyle ammoHudStyle;
    private GUIStyle ammoCountStyle;
    private GUIStyle ammoStatusStyle;
    private bool hipFireQueuedForForwardPose;

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

            weaponDefinition = value;
            if (Application.isPlaying)
            {
                RebuildRuntimeState();
            }
        }
    }

    public WeaponRuntimeConfig ActiveConfiguration => activeConfiguration;
    public WeaponRuntimeState RuntimeState => runtimeState;
    public int CurrentMagazineAmmo => runtimeState?.CurrentMagazineAmmo ?? 0;
    public int ReserveAmmo => runtimeState?.CurrentReserveAmmo ?? 0;
    public bool IsReloading => runtimeState != null && runtimeState.IsReloading;
    public bool IsCycling => runtimeState != null && runtimeState.IsManualCycleInProgress;
    public bool CanFire => CurrentFireBlockReason == WeaponFireBlockReason.None;

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

    private void Awake()
    {
        RebuildRuntimeState();

        controller = GetComponent<PowerSuitController>();
        weaponAnimationDriver = GetComponent<PowerSuitWeaponAnimationDriver>();
        playerCamera = Camera.main;

        if (playerCamera == null)
        {
            Debug.LogError("No Main Camera found.", this);
            enabled = false;
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        EnsureMuzzleFlashLight();
    }

    private void OnDestroy()
    {
        DetachRuntimeEvents();
    }

    private void Update()
    {
        runtimeState?.Advance(Time.deltaTime);

        if (IsReloadPressed())
        {
            TryStartReload();
        }

        bool fireRequested = IsFireRequested();
        if (hipFireQueuedForForwardPose)
        {
            hipFireQueuedForForwardPose = false;
            TryFireWeapon();
        }
        else if (fireRequested)
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
        hipFireQueuedForForwardPose = false;
    }

    /// <summary>
    /// Requests a gameplay shot. Non-aim fire is staged for one Animator
    /// evaluation when necessary so projectile and muzzle feedback sample the
    /// forward firing pose rather than the diagonal carry pose. Returns true
    /// when the request fired immediately or was accepted for staging.
    /// </summary>
    public bool RequestFire()
    {
        if (hipFireQueuedForForwardPose)
        {
            return false;
        }

        bool queuedForForwardPose =
            controller != null &&
            !controller.IsAiming &&
            CanFire &&
            weaponAnimationDriver != null &&
            !weaponAnimationDriver.RequiresForwardWeaponPose &&
            weaponAnimationDriver.PrepareForwardWeaponPose();
        if (!queuedForForwardPose)
        {
            return TryFireWeapon().Fired;
        }

        controller.FaceCameraForWeaponFire();
        hipFireQueuedForForwardPose = true;
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
            return result;
        }

        controller?.FaceCameraForWeaponFire();
        FireProjectileAndFeedback(result.Damage);
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

        return runtimeState.TryStartReload();
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

    private void FireProjectileAndFeedback(float resolvedDamage)
    {
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

        if (projectilePrefab != null)
        {
            PlayerProjectile proj = Instantiate(
                projectilePrefab,
                muzzlePos,
                Quaternion.LookRotation(fireDirection)
            );

            proj.Initialize(
                fireDirection,
                activeConfiguration.ProjectileSpeed,
                resolvedDamage,
                activeConfiguration.ProjectileLifetimeSeconds,
                activeConfiguration.ProjectileRadius,
                transform
            );
        }
        else
        {
            SpawnFallbackProjectile(muzzlePos, fireDirection, resolvedDamage);
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
        }

        if (muzzleFlashLight != null)
        {
            muzzleFlashLight.transform.position = position;
            muzzleFlashLight.enabled = true;
            muzzleLightTimer = flashDuration;
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
        float resolvedDamage
    )
    {
        GameObject projObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projObj.name = "Player Projectile";
        projObj.transform.position = position;
        projObj.transform.rotation = Quaternion.LookRotation(direction);
        projObj.transform.localScale =
            Vector3.one * (activeConfiguration.ProjectileRadius * 2f);

        SphereCollider col = projObj.GetComponent<SphereCollider>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        Renderer rend = projObj.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material.color = aimingReticleColor;
        }

        PlayerProjectile proj = projObj.AddComponent<PlayerProjectile>();
        proj.Initialize(
            direction,
            activeConfiguration.ProjectileSpeed,
            resolvedDamage,
            activeConfiguration.ProjectileLifetimeSeconds,
            activeConfiguration.ProjectileRadius,
            transform
        );
    }

    private bool IsFireRequested()
    {
        bool semiAutomatic =
            activeConfiguration?.TriggerMode == WeaponTriggerMode.SemiAutomatic;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null)
        {
            return false;
        }

        return semiAutomatic
            ? Mouse.current.leftButton.wasPressedThisFrame
            : Mouse.current.leftButton.isPressed;
#else
        return semiAutomatic ? Input.GetMouseButtonDown(0) : Input.GetMouseButton(0);
#endif
    }

    private static bool IsReloadPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.R);
#endif
    }

    private void OnGUI()
    {
        if (controller == null)
        {
            DrawAmmoHud();
            return;
        }

        bool isAiming = controller.IsAiming;
        Vector2 reticlePos = isAiming
            ? controller.ReticleScreenPosition
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        float guiX = reticlePos.x;
        float guiY = Screen.height - reticlePos.y;

        Color savedColor = GUI.color;
        GUI.color = isAiming ? aimingReticleColor : normalCrosshairColor;

        if (isAiming)
        {
            const float size = 12f;
            const float thickness = 2f;
            const float gap = 4f;

            GUI.DrawTexture(
                new Rect(guiX - thickness * 0.5f, guiY - gap - size, thickness, size),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(guiX - thickness * 0.5f, guiY + gap, thickness, size),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(guiX - gap - size, guiY - thickness * 0.5f, size, thickness),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(guiX + gap, guiY - thickness * 0.5f, size, thickness),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(guiX - 1.5f, guiY - 1.5f, 3f, 3f),
                Texture2D.whiteTexture
            );
        }
        else
        {
            const float size = 8f;
            const float thickness = 2f;

            GUI.DrawTexture(
                new Rect(guiX - size, guiY - thickness * 0.5f, size * 2f, thickness),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(guiX - thickness * 0.5f, guiY - size, thickness, size * 2f),
                Texture2D.whiteTexture
            );
        }

        GUI.color = savedColor;
        DrawAmmoHud();
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
