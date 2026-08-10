using Powersuit.Abilities.UnityAdapters;
using Powersuit.UI.HUD;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PowerSuitHudPresenter : MonoBehaviour
{
    [Header("Gameplay Sources")]
    [SerializeField] private PlayerHealth healthSource;
    [SerializeField] private PowerSuitWeapon weaponSource;
    [SerializeField] private ShoulderRocketAbility shoulderRocketSource;
    [SerializeField] private LightningStrikeAbility lightningSource;
    [SerializeField] private VoidUltimateAbility ultimateSource;

    [Header("Health")]
    [SerializeField] private GameObject healthRoot;
    [SerializeField] private Image healthFill;
    [SerializeField] private Text healthLabel;

    [Header("Ammunition and Reload")]
    [SerializeField] private GameObject ammunitionRoot;
    [SerializeField] private Text ammunitionLabel;
    [SerializeField] private GameObject reloadRoot;
    [SerializeField] private Image reloadFill;
    [SerializeField] private Text reloadLabel;

    [Header("Shoulder Rocket")]
    [SerializeField] private GameObject shoulderRocketRoot;
    [SerializeField] private Image shoulderRocketFill;
    [SerializeField] private Text shoulderRocketLabel;

    [Header("Lightning Strike")]
    [SerializeField] private GameObject lightningRoot;
    [SerializeField] private Image lightningFill;
    [SerializeField] private Text lightningLabel;

    [Header("Void Ultimate")]
    [SerializeField] private GameObject ultimateRoot;
    [SerializeField] private Image ultimateFill;
    [SerializeField] private Text ultimateLabel;

    private readonly PowerSuitHudChangeDetector changeDetector =
        new PowerSuitHudChangeDetector();
    private readonly PowerSuitHudTextChangeDetector textChangeDetector =
        new PowerSuitHudTextChangeDetector();

    public PlayerHealth HealthSource => healthSource;
    public PowerSuitWeapon WeaponSource => weaponSource;
    public ShoulderRocketAbility ShoulderRocketSource => shoulderRocketSource;
    public LightningStrikeAbility LightningSource => lightningSource;
    public VoidUltimateAbility UltimateSource => ultimateSource;
    public PowerSuitHudSnapshot CurrentSnapshot { get; private set; }
    public PowerSuitHudDirtyFlags LastDirtyFlags { get; private set; }
    public PowerSuitHudDirtyFlags LastTextDirtyFlags { get; private set; }

    private void OnEnable()
    {
        changeDetector.Reset();
        textChangeDetector.Reset();
        RefreshNow();
    }

    private void LateUpdate()
    {
        RefreshNow();
    }

    private void OnDisable()
    {
        changeDetector.Reset();
        textChangeDetector.Reset();
        LastDirtyFlags = PowerSuitHudDirtyFlags.None;
        LastTextDirtyFlags = PowerSuitHudDirtyFlags.None;
    }

    public void BindSources(
        PlayerHealth health,
        PowerSuitWeapon weapon,
        ShoulderRocketAbility shoulderRocket,
        LightningStrikeAbility lightning,
        VoidUltimateAbility ultimate
    )
    {
        healthSource = health;
        weaponSource = weapon;
        shoulderRocketSource = shoulderRocket;
        lightningSource = lightning;
        ultimateSource = ultimate;
        changeDetector.Reset();
        textChangeDetector.Reset();
        RefreshNow();
    }

    public PowerSuitHudDirtyFlags RefreshNow()
    {
        CurrentSnapshot = CaptureSnapshot();
        LastDirtyFlags = changeDetector.Capture(CurrentSnapshot);
        LastTextDirtyFlags = textChangeDetector.Capture(CurrentSnapshot);
        if (LastDirtyFlags == PowerSuitHudDirtyFlags.None)
        {
            return LastDirtyFlags;
        }

        ApplySnapshot(CurrentSnapshot, LastDirtyFlags, LastTextDirtyFlags);
        return LastDirtyFlags;
    }

    private PowerSuitHudSnapshot CaptureSnapshot()
    {
        HudHealthState health = healthSource != null
            ? new HudHealthState(
                true,
                healthSource.CurrentHealth,
                healthSource.MaximumHealth,
                healthSource.IsDefeated
            )
            : HudHealthState.Missing;

        HudWeaponState weapon = CaptureWeapon();
        HudAbilityState shoulderRocket = shoulderRocketSource != null
            ? new HudAbilityState(
                true,
                shoulderRocketSource.CooldownRemaining,
                shoulderRocketSource.CooldownNormalized,
                shoulderRocketSource.CanLaunch,
                false
            )
            : HudAbilityState.Missing;
        HudAbilityState lightning = lightningSource != null
            ? new HudAbilityState(
                true,
                lightningSource.CooldownRemaining,
                lightningSource.CooldownNormalized,
                lightningSource.CanBeginTargeting,
                lightningSource.IsTargeting
            )
            : HudAbilityState.Missing;
        HudUltimateState ultimate = ultimateSource != null
            ? new HudUltimateState(
                true,
                ultimateSource.MeterNormalized,
                ultimateSource.IsMeterFull,
                ultimateSource.IsActive
            )
            : HudUltimateState.Missing;

        return new PowerSuitHudSnapshot(
            health,
            weapon,
            shoulderRocket,
            lightning,
            ultimate
        );
    }

    private HudWeaponState CaptureWeapon()
    {
        if (weaponSource == null)
        {
            return HudWeaponState.Missing;
        }

        Powersuit.Combat.WeaponRuntimeConfig configuration =
            weaponSource.ActiveConfiguration;
        Powersuit.Combat.WeaponRuntimeState runtime = weaponSource.RuntimeState;
        return new HudWeaponState(
            true,
            weaponSource.CurrentMagazineAmmo,
            configuration?.MagazineCapacity ?? 0,
            weaponSource.ReserveAmmo,
            configuration?.UsesInfiniteAmmo ?? false,
            weaponSource.IsReloading,
            runtime?.ReloadNormalizedTime ?? 0f
        );
    }

    private void ApplySnapshot(
        PowerSuitHudSnapshot snapshot,
        PowerSuitHudDirtyFlags dirty,
        PowerSuitHudDirtyFlags textDirty
    )
    {
        if ((dirty & PowerSuitHudDirtyFlags.Health) != 0)
        {
            SetVisible(healthRoot, snapshot.Health.IsAvailable);
            SetFill(healthFill, snapshot.Health.Normalized);
            if ((textDirty & PowerSuitHudDirtyFlags.Health) != 0)
            {
                SetText(
                    healthLabel,
                    PowerSuitHudFormatter.FormatHealth(snapshot.Health)
                );
            }
        }

        if ((dirty & PowerSuitHudDirtyFlags.Ammunition) != 0)
        {
            SetVisible(ammunitionRoot, snapshot.Weapon.IsAvailable);
            if ((textDirty & PowerSuitHudDirtyFlags.Ammunition) != 0)
            {
                SetText(
                    ammunitionLabel,
                    PowerSuitHudFormatter.FormatAmmunition(snapshot.Weapon)
                );
            }
        }

        if ((dirty & PowerSuitHudDirtyFlags.Reload) != 0)
        {
            SetVisible(
                reloadRoot,
                snapshot.Weapon.IsAvailable && snapshot.Weapon.IsReloading
            );
            SetFill(reloadFill, snapshot.Weapon.ReloadNormalized);
            if ((textDirty & PowerSuitHudDirtyFlags.Reload) != 0)
            {
                SetText(
                    reloadLabel,
                    PowerSuitHudFormatter.FormatReload(snapshot.Weapon)
                );
            }
        }

        if ((dirty & PowerSuitHudDirtyFlags.ShoulderRocket) != 0)
        {
            SetVisible(shoulderRocketRoot, snapshot.ShoulderRocket.IsAvailable);
            SetFill(shoulderRocketFill, snapshot.ShoulderRocket.ReadyNormalized);
            if ((textDirty & PowerSuitHudDirtyFlags.ShoulderRocket) != 0)
            {
                SetText(
                    shoulderRocketLabel,
                    PowerSuitHudFormatter.FormatAbility(
                        "ROCKET",
                        snapshot.ShoulderRocket
                    )
                );
            }
        }

        if ((dirty & PowerSuitHudDirtyFlags.Lightning) != 0)
        {
            SetVisible(lightningRoot, snapshot.Lightning.IsAvailable);
            SetFill(lightningFill, snapshot.Lightning.ReadyNormalized);
            if ((textDirty & PowerSuitHudDirtyFlags.Lightning) != 0)
            {
                SetText(
                    lightningLabel,
                    PowerSuitHudFormatter.FormatAbility(
                        "LIGHTNING",
                        snapshot.Lightning
                    )
                );
            }
        }

        if ((dirty & PowerSuitHudDirtyFlags.Ultimate) != 0)
        {
            SetVisible(ultimateRoot, snapshot.Ultimate.IsAvailable);
            SetFill(ultimateFill, snapshot.Ultimate.MeterNormalized);
            if ((textDirty & PowerSuitHudDirtyFlags.Ultimate) != 0)
            {
                SetText(
                    ultimateLabel,
                    PowerSuitHudFormatter.FormatUltimate(snapshot.Ultimate)
                );
            }
        }
    }

    private static void SetVisible(GameObject target, bool visible)
    {
        if (target != null && target.activeSelf != visible)
        {
            target.SetActive(visible);
        }
    }

    private static void SetFill(Image image, float fill)
    {
        if (image != null && !Mathf.Approximately(image.fillAmount, fill))
        {
            image.fillAmount = fill;
        }
    }

    private static void SetText(Text label, string value)
    {
        if (label != null && label.text != value)
        {
            label.text = value;
        }
    }
}
