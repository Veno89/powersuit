using System;
using Powersuit.Combat;
using UnityEngine;

/// <summary>
/// Unity adapter for a compact data-driven player loadout. Definitions and
/// visual selection live here; ammunition, cadence, and selection state remain
/// in engine-independent combat runtime classes.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-75)]
public sealed class PowerSuitWeaponLoadout : MonoBehaviour
{
    [SerializeField] private WeaponDefinition[] weaponDefinitions =
        Array.Empty<WeaponDefinition>();
    [SerializeField, Min(0)] private int startingSlot;
    [SerializeField] private PowerSuitWeapon weapon;
    [SerializeField] private PowerSuitWeaponPresentation presentation;
    [SerializeField] private PowerSuitController controller;
    [SerializeField] private PowerSuitInputRouter inputRouter;
    [SerializeField] private PowerSuitWeaponVisualController visualController;

    private WeaponLoadoutState state;
    private Renderer[] scopeRenderers = Array.Empty<Renderer>();
    private bool[] scopeRendererDefaults = Array.Empty<bool>();
    private int fallbackInputFrame = -1;
    private PowerSuitInputSnapshot fallbackInputSnapshot;
    private bool switchThroughStowed;
    private bool drawAfterSwitch;
    private bool awaitingDrawCompletion;

    public int SlotCount => weaponDefinitions?.Length ?? 0;
    public int EquippedIndex => state?.EquippedIndex ?? -1;
    public int PendingIndex => state?.PendingIndex ?? -1;
    public WeaponDefinition EquippedDefinition =>
        state != null && state.EquippedIndex < SlotCount
            ? weaponDefinitions[state.EquippedIndex]
            : null;
    public int ScopeRendererCount => scopeRenderers.Length;
    public bool IsSwitching =>
        switchThroughStowed ||
        awaitingDrawCompletion ||
        (state != null && state.HasPendingSelection);

    public event Action<int, WeaponDefinition> WeaponChanged;

    private void Awake()
    {
        ResolveDependencies();
    }

    private void Start()
    {
        EnsureInitialized();
    }

    private void Update()
    {
        EnsureInitialized();
        if (state == null)
        {
            return;
        }

        state.AdvanceInactive(Time.deltaTime);
        PowerSuitInputSnapshot input = ReadInputSnapshot();
        if (input.WeaponSlot1Pressed)
        {
            state.RequestSelection(0);
        }
        else if (input.WeaponSlot2Pressed)
        {
            state.RequestSelection(1);
        }
        else if (input.WeaponSlot3Pressed)
        {
            state.RequestSelection(2);
        }
        else if (input.WeaponNextPressed)
        {
            state.RequestNext();
        }

        ProgressPendingSelection();
    }

    private void OnDisable()
    {
        state?.CancelPendingSelection();
        switchThroughStowed = false;
        drawAfterSwitch = false;
        awaitingDrawCompletion = false;
    }

    public WeaponDefinition GetDefinition(int index)
    {
        if (index < 0 || index >= SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return weaponDefinitions[index];
    }

    public void Configure(
        WeaponDefinition[] definitions,
        int initialSlot = 0
    )
    {
        weaponDefinitions = definitions != null
            ? (WeaponDefinition[])definitions.Clone()
            : Array.Empty<WeaponDefinition>();
        startingSlot = initialSlot;
        state = null;
        if (Application.isPlaying)
        {
            EnsureInitialized();
        }
    }

    public WeaponSelectionRequestResult RequestSlot(int index)
    {
        EnsureInitialized();
        if (state == null)
        {
            return WeaponSelectionRequestResult.InvalidSlot;
        }

        WeaponSelectionRequestResult result = state.RequestSelection(index);
        ProgressPendingSelection();
        return result;
    }

    public WeaponSelectionRequestResult RequestNext()
    {
        EnsureInitialized();
        if (state == null)
        {
            return WeaponSelectionRequestResult.InvalidSlot;
        }

        WeaponSelectionRequestResult result = state.RequestNext();
        ProgressPendingSelection();
        return result;
    }

    public void ResetForRespawn()
    {
        state?.ResetTransientStates();
        switchThroughStowed = false;
        drawAfterSwitch = false;
        awaitingDrawCompletion = false;
    }

    private void EnsureInitialized()
    {
        if (state != null)
        {
            return;
        }

        ResolveDependencies();
        if (weapon == null || weaponDefinitions == null || weaponDefinitions.Length == 0)
        {
            return;
        }

        int safeStartingSlot = Mathf.Clamp(
            startingSlot,
            0,
            weaponDefinitions.Length - 1
        );
        WeaponRuntimeConfig[] configurations =
            new WeaponRuntimeConfig[weaponDefinitions.Length];
        int largestProjectilePrewarm = 0;
        for (int index = 0; index < weaponDefinitions.Length; index++)
        {
            WeaponDefinition definition = weaponDefinitions[index];
            if (definition == null)
            {
                Debug.LogError(
                    "PowerSuitWeaponLoadout contains an empty weapon slot.",
                    this
                );
                enabled = false;
                return;
            }

            configurations[index] = definition.CreateRuntimeConfig();
            configurations[index].ValidateOrThrow();
            largestProjectilePrewarm = Mathf.Max(
                largestProjectilePrewarm,
                definition.ProjectilePrewarmCount
            );
            if (definition.ProjectilePrefabOverride != null)
            {
                weapon.PrewarmProjectiles(
                    definition.ProjectilePrefabOverride,
                    definition.ProjectilePrewarmCount
                );
            }
            for (int prior = 0; prior < index; prior++)
            {
                if (
                    configurations[prior].WeaponId ==
                    configurations[index].WeaponId
                )
                {
                    Debug.LogError(
                        "PowerSuitWeaponLoadout weapon IDs must be unique.",
                        this
                    );
                    enabled = false;
                    return;
                }
            }
        }

        state = new WeaponLoadoutState(configurations, safeStartingSlot);
        CacheScopeRenderers();
        weapon.PrewarmProjectiles(largestProjectilePrewarm);
        EquipCurrentState();
    }

    private void ResolveDependencies()
    {
        weapon ??= GetComponent<PowerSuitWeapon>();
        presentation ??= GetComponent<PowerSuitWeaponPresentation>();
        controller ??= GetComponent<PowerSuitController>();
        inputRouter ??= GetComponent<PowerSuitInputRouter>();
        visualController ??= GetComponent<PowerSuitWeaponVisualController>();
    }

    private void ProgressPendingSelection()
    {
        if (state == null)
        {
            return;
        }

        if (awaitingDrawCompletion)
        {
            if (
                presentation == null ||
                (
                    !presentation.IsTransitioning &&
                    presentation.State == PowerSuitWeaponPresentationState.Ready
                )
            )
            {
                awaitingDrawCompletion = false;
            }

            if (!state.HasPendingSelection)
            {
                return;
            }
        }

        if (!state.HasPendingSelection)
        {
            if (
                switchThroughStowed &&
                drawAfterSwitch &&
                presentation != null &&
                !presentation.IsTransitioning &&
                presentation.State == PowerSuitWeaponPresentationState.Stowed
            )
            {
                awaitingDrawCompletion = presentation.RequestDraw();
            }
            switchThroughStowed = false;
            drawAfterSwitch = false;
            return;
        }

        if (presentation == null)
        {
            CommitPendingSelection();
            return;
        }

        if (presentation.IsTransitioning)
        {
            return;
        }

        if (!switchThroughStowed)
        {
            if (presentation.State == PowerSuitWeaponPresentationState.Ready)
            {
                if (presentation.RequestSheathe())
                {
                    switchThroughStowed = true;
                    drawAfterSwitch = true;
                }
                return;
            }

            if (presentation.State == PowerSuitWeaponPresentationState.Stowed)
            {
                CommitPendingSelection();
            }
            return;
        }

        if (presentation.State != PowerSuitWeaponPresentationState.Stowed)
        {
            return;
        }

        CommitPendingSelection();
        bool shouldDraw = drawAfterSwitch;
        switchThroughStowed = false;
        drawAfterSwitch = false;
        if (shouldDraw)
        {
            awaitingDrawCompletion = presentation.RequestDraw();
        }
    }

    private void CommitPendingSelection()
    {
        if (state.TryCommitPendingSelection(canSwitch: true))
        {
            EquipCurrentState();
        }
    }

    private void EquipCurrentState()
    {
        WeaponDefinition definition = weaponDefinitions[state.EquippedIndex];
        weapon.EquipLoadoutWeapon(definition, state.EquippedWeapon);
        visualController?.ApplyWeaponVisual(definition);
        ApplyScopeRendererState(definition.SupportsScope);
        WeaponChanged?.Invoke(state.EquippedIndex, definition);
    }

    private void CacheScopeRenderers()
    {
        Transform scopePoint = controller != null ? controller.ScopePoint : null;
        Transform rifleRoot = scopePoint;
        while (
            rifleRoot != null &&
            !rifleRoot.name.Equals(
                "RifleRoot",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            rifleRoot = rifleRoot.parent;
        }

        if (rifleRoot == null)
        {
            scopeRenderers = Array.Empty<Renderer>();
            scopeRendererDefaults = Array.Empty<bool>();
            return;
        }

        Renderer[] allRenderers = rifleRoot.GetComponentsInChildren<Renderer>(true);
        int count = 0;
        for (int index = 0; index < allRenderers.Length; index++)
        {
            if (IsScopeRenderer(allRenderers[index]))
            {
                count++;
            }
        }

        scopeRenderers = new Renderer[count];
        scopeRendererDefaults = new bool[count];
        int write = 0;
        for (int index = 0; index < allRenderers.Length; index++)
        {
            Renderer candidate = allRenderers[index];
            if (!IsScopeRenderer(candidate))
            {
                continue;
            }

            scopeRenderers[write] = candidate;
            scopeRendererDefaults[write] = candidate.enabled;
            write++;
        }
    }

    private void ApplyScopeRendererState(bool showScope)
    {
        // EquipLoadoutWeapon rebinds the scope presenter first, restoring any
        // renderer states it owned during scoped ADS. Apply the selected
        // receiver's authored optic visibility afterward.
        for (int index = 0; index < scopeRenderers.Length; index++)
        {
            Renderer scopeRenderer = scopeRenderers[index];
            if (scopeRenderer != null)
            {
                scopeRenderer.enabled =
                    showScope && scopeRendererDefaults[index];
            }
        }
    }

    private static bool IsScopeRenderer(Renderer renderer)
    {
        return renderer != null &&
            renderer.name.StartsWith(
                "Rifle_Scope",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private PowerSuitInputSnapshot ReadInputSnapshot()
    {
        if (
            inputRouter != null &&
            inputRouter.TryGetCurrentSnapshot(out PowerSuitInputSnapshot routed)
        )
        {
            return routed;
        }

        int frame = Time.frameCount;
        if (fallbackInputFrame != frame)
        {
            fallbackInputSnapshot = PowerSuitInputRouter.ReadFallbackSnapshot();
            fallbackInputFrame = frame;
        }

        return fallbackInputSnapshot;
    }

    private void OnValidate()
    {
        startingSlot = Mathf.Max(0, startingSlot);
    }
}
