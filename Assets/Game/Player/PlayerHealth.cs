using System.Collections;
using Powersuit.Combat;
using Powersuit.Combat.UnityAdapters;
using UnityEngine;

public sealed class PlayerHealth : MonoBehaviour, IDamageReceiver
{
    public const float MinimumMaximumHealth = 1f;
    public const float MaximumMaximumHealth = 1000000f;

    [SerializeField] private float maximumHealth = 100f;
    [SerializeField] private float respawnDelay = 2f;
    [SerializeField] private CombatFaction faction = CombatFaction.Player;
    [SerializeField] private bool allowFriendlyFire;
    [SerializeField] private bool invulnerable;

    [Header("Legacy HUD")]
    [Tooltip(
        "Draws the old immediate-mode health label. Disable when a " +
        "PowerSuitHudPresenter owns the screen-space HUD."
    )]
    [SerializeField] private bool showLegacyHealthHud = true;

    private float currentHealth;
    private Vector3 startingPosition;
    private Quaternion startingRotation;
    private bool defeated;
    private bool godMode;

    private PowerSuitController movement;
    private PowerSuitAnimationDriver animationDriver;
    private PowerSuitWeapon weapon;
    private PowerSuitWeaponPresentation weaponPresentation;
    private CharacterController characterController;

    private void Awake()
    {
        maximumHealth = ClampFinite(
            maximumHealth,
            MinimumMaximumHealth,
            MaximumMaximumHealth,
            100f
        );
        respawnDelay = ClampFinite(respawnDelay, 0f, 3600f, 2f);
        currentHealth = maximumHealth;
        startingPosition = transform.position;
        startingRotation = transform.rotation;

        movement = GetComponent<PowerSuitController>();
        animationDriver = GetComponent<PowerSuitAnimationDriver>();
        weapon = GetComponent<PowerSuitWeapon>();
        weaponPresentation = GetComponent<PowerSuitWeaponPresentation>();
        characterController = GetComponent<CharacterController>();
    }

    public float CurrentHealth => currentHealth;
    public float MaximumHealth => maximumHealth;
    public bool IsDefeated => defeated;
    public bool IsInvulnerable => invulnerable;
    public bool IsGodMode => godMode;
    public bool ShowLegacyHealthHud
    {
        get => showLegacyHealthHud;
        set => showLegacyHealthHud = value;
    }
    public CombatFaction Faction =>
        faction == CombatFaction.None ? CombatFaction.Player : faction;
    public bool CanReceiveDamage => !defeated && !invulnerable && !godMode;
    public event System.Action<float, float> OnHealthChanged;
    public event System.Action<float, float> OnHealthRestored;
    public event System.Action OnDefeated;
    public event System.Action OnRespawned;

    public void TakeDamage(float damage)
    {
        if (!CanReceiveDamage)
        {
            return;
        }

        ApplyDamageInternal(
            damage,
            transform.position + Vector3.up * 1.8f
        );
    }

    public DamageResult ApplyDamage(DamageInfo damage)
    {
        if (
            !CanReceiveDamage ||
            !CombatFactionPolicy.CanDamage(
                damage.Faction,
                Faction,
                allowFriendlyFire
            )
        )
        {
            return DamageResult.Ignored;
        }

        return ApplyDamageInternal(
            damage.Amount,
            CombatVectorConversion.ToUnity(damage.Position)
        );
    }

    private DamageResult ApplyDamageInternal(float damage, Vector3 hitPoint)
    {
        float requestedDamage = Mathf.Max(0f, damage);
        if (defeated || requestedDamage <= 0f)
        {
            return DamageResult.Ignored;
        }

        float healthBefore = currentHealth;
        currentHealth = Mathf.Max(0f, currentHealth - requestedDamage);
        float appliedDamage = healthBefore - currentHealth;
        DamageNumberManager.SpawnDamageNumber(
            hitPoint,
            appliedDamage,
            isPlayerDamage: true
        );
        OnHealthChanged?.Invoke(currentHealth, maximumHealth);

        Debug.Log(
            $"Player took {appliedDamage} damage. " +
            $"{currentHealth} health remains."
        );

        if (currentHealth <= 0f)
        {
            StartCoroutine(Respawn());
        }

        return DamageResult.Applied(appliedDamage, currentHealth <= 0f);
    }

    /// <summary>
    /// Prevents ordinary damage while preserving the current health value.
    /// This is intentionally independent from god mode so console tooling can
    /// report and change both switches without hidden coupling.
    /// </summary>
    public void SetInvulnerable(bool isEnabled)
    {
        invulnerable = isEnabled;
    }

    /// <summary>
    /// Toggles god mode. Enabling it also restores health so a nearly defeated
    /// player cannot immediately die when the mode is later disabled.
    /// </summary>
    public void SetGodMode(bool isEnabled)
    {
        godMode = isEnabled;
        if (isEnabled)
        {
            HealToFull();
        }
    }

    /// <returns>The amount of health restored.</returns>
    public float HealToFull()
    {
        if (defeated)
        {
            return 0f;
        }

        float previous = currentHealth;
        currentHealth = maximumHealth;
        RaiseHealthRestoredIfChanged(previous);
        return currentHealth - previous;
    }

    /// <summary>
    /// Sets current health through the same defeat lifecycle as combat damage.
    /// NaN keeps the previous value; infinities and out-of-range values clamp.
    /// </summary>
    public float SetCurrentHealth(float value)
    {
        if (defeated)
        {
            return currentHealth;
        }

        float previous = currentHealth;
        currentHealth = ClampFinite(value, 0f, maximumHealth, currentHealth);
        if (Mathf.Approximately(previous, currentHealth))
        {
            return currentHealth;
        }

        OnHealthChanged?.Invoke(currentHealth, maximumHealth);
        if (currentHealth > previous)
        {
            OnHealthRestored?.Invoke(currentHealth, maximumHealth);
        }

        if (currentHealth <= 0f)
        {
            StartCoroutine(Respawn());
        }

        return currentHealth;
    }

    /// <summary>
    /// Changes the health ceiling without implicitly granting health. Current
    /// health is clamped down when necessary. Returns the effective maximum.
    /// </summary>
    public float SetMaximumHealth(float value)
    {
        float previousMaximum = maximumHealth;
        float previousHealth = currentHealth;
        maximumHealth = ClampFinite(
            value,
            MinimumMaximumHealth,
            MaximumMaximumHealth,
            maximumHealth
        );
        currentHealth = Mathf.Min(currentHealth, maximumHealth);
        if (
            !Mathf.Approximately(previousMaximum, maximumHealth) ||
            !Mathf.Approximately(previousHealth, currentHealth)
        )
        {
            OnHealthChanged?.Invoke(currentHealth, maximumHealth);
        }

        return maximumHealth;
    }

    /// <summary>
    /// Defeats the player even when a damage-prevention mode is active.
    /// </summary>
    public bool Kill()
    {
        if (defeated)
        {
            return false;
        }

        return SetCurrentHealth(0f) <= 0f;
    }

    private void RaiseHealthRestoredIfChanged(float previous)
    {
        if (Mathf.Approximately(previous, currentHealth))
        {
            return;
        }

        OnHealthChanged?.Invoke(currentHealth, maximumHealth);
        OnHealthRestored?.Invoke(currentHealth, maximumHealth);
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

    private IEnumerator Respawn()
    {
        defeated = true;
        OnDefeated?.Invoke();

        movement?.ResetForRespawn();
        animationDriver?.ResetForRespawn();
        weapon?.ResetForRespawn();
        weaponPresentation?.ResetForRespawn();

        if (movement != null)
        {
            movement.enabled = false;
        }

        if (weapon != null)
        {
            weapon.enabled = false;
        }

        if (weaponPresentation != null)
        {
            weaponPresentation.enabled = false;
        }

        yield return new WaitForSeconds(respawnDelay);

        if (characterController != null)
        {
            characterController.enabled = false;
        }

        transform.SetPositionAndRotation(
            startingPosition,
            startingRotation
        );

        if (characterController != null)
        {
            characterController.enabled = true;
        }

        // Camera heading is based on the newly restored spawn rotation.
        movement?.ResetForRespawn();
        animationDriver?.ResetForRespawn();
        weapon?.ResetForRespawn();
        weaponPresentation?.ResetForRespawn();

        currentHealth = maximumHealth;
        defeated = false;
        OnHealthChanged?.Invoke(currentHealth, maximumHealth);
        OnHealthRestored?.Invoke(currentHealth, maximumHealth);

        if (movement != null)
        {
            movement.enabled = true;
        }

        if (weapon != null)
        {
            weapon.enabled = true;
        }

        if (weaponPresentation != null)
        {
            weaponPresentation.enabled = true;
        }

        OnRespawned?.Invoke();
    }

    private void OnGUI()
    {
        if (!showLegacyHealthHud)
        {
            return;
        }

        GUI.Label(
            new Rect(20f, 20f, 250f, 30f),
            defeated
                ? "POWER SUIT DISABLED"
                : $"Health: {Mathf.CeilToInt(currentHealth)}"
        );
    }
}
