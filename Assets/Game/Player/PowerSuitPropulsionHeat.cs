using System;
using UnityEngine;

public enum PowerSuitPropulsionLoad
{
    None = 0,
    Sprint = 1,
    Flight = 2,
    Boost = 3
}

[Serializable]
public sealed class PowerSuitPropulsionHeatSettings
{
    [SerializeField, Min(1f)] private float maximumHeat = 100f;
    [SerializeField, Min(0f)] private float sprintHeatPerSecond = 8f;
    [SerializeField, Min(0f)] private float flightHeatPerSecond = 5f;
    [SerializeField, Min(0f)] private float boostHeatPerSecond = 14f;
    [SerializeField, Min(0f)] private float coolingPerSecond = 26f;
    [SerializeField, Min(0f)] private float coolingDelaySeconds = 1f;
    [SerializeField, Range(0f, 1f)] private float recoveryThreshold = 0.35f;

    public float MaximumHeat => maximumHeat;
    public float SprintHeatPerSecond => sprintHeatPerSecond;
    public float FlightHeatPerSecond => flightHeatPerSecond;
    public float BoostHeatPerSecond => boostHeatPerSecond;
    public float CoolingPerSecond => coolingPerSecond;
    public float CoolingDelaySeconds => coolingDelaySeconds;
    public float RecoveryThreshold => recoveryThreshold;

    public void Sanitize()
    {
        maximumHeat = FinitePositiveOr(maximumHeat, 100f);
        sprintHeatPerSecond = FiniteNonNegativeOr(sprintHeatPerSecond, 8f);
        flightHeatPerSecond = FiniteNonNegativeOr(flightHeatPerSecond, 5f);
        boostHeatPerSecond = FiniteNonNegativeOr(boostHeatPerSecond, 14f);
        coolingPerSecond = FiniteNonNegativeOr(coolingPerSecond, 26f);
        coolingDelaySeconds = FiniteNonNegativeOr(coolingDelaySeconds, 1f);
        recoveryThreshold = float.IsNaN(recoveryThreshold) ||
            float.IsInfinity(recoveryThreshold)
                ? 0.35f
                : Mathf.Clamp01(recoveryThreshold);
    }

    public PowerSuitPropulsionHeatState CreateState()
    {
        Sanitize();
        return new PowerSuitPropulsionHeatState(
            maximumHeat,
            sprintHeatPerSecond,
            flightHeatPerSecond,
            boostHeatPerSecond,
            coolingPerSecond,
            coolingDelaySeconds,
            recoveryThreshold
        );
    }

    private static float FinitePositiveOr(float value, float fallback)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f
            ? value
            : fallback;
    }

    private static float FiniteNonNegativeOr(float value, float fallback)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f
            ? value
            : fallback;
    }
}

/// <summary>
/// Engine-independent shared heat budget for powered sprint and flight. An
/// overheated suit remains locked until it cools beneath the recovery
/// threshold, preventing a held input from rapidly toggling propulsion.
/// </summary>
public sealed class PowerSuitPropulsionHeatState
{
    private readonly float maximumHeat;
    private readonly float sprintHeatPerSecond;
    private readonly float flightHeatPerSecond;
    private readonly float boostHeatPerSecond;
    private readonly float coolingPerSecond;
    private readonly float coolingDelaySeconds;
    private readonly float recoveryHeat;

    private float heat;
    private float coolingDelayRemaining;
    private bool isOverheated;

    public PowerSuitPropulsionHeatState(
        float maximumHeat,
        float sprintHeatPerSecond,
        float flightHeatPerSecond,
        float boostHeatPerSecond,
        float coolingPerSecond,
        float coolingDelaySeconds,
        float recoveryThreshold
    )
    {
        ValidateFinitePositive(maximumHeat, nameof(maximumHeat));
        ValidateFiniteNonNegative(sprintHeatPerSecond, nameof(sprintHeatPerSecond));
        ValidateFiniteNonNegative(flightHeatPerSecond, nameof(flightHeatPerSecond));
        ValidateFiniteNonNegative(boostHeatPerSecond, nameof(boostHeatPerSecond));
        ValidateFiniteNonNegative(coolingPerSecond, nameof(coolingPerSecond));
        ValidateFiniteNonNegative(coolingDelaySeconds, nameof(coolingDelaySeconds));
        if (
            float.IsNaN(recoveryThreshold) ||
            float.IsInfinity(recoveryThreshold) ||
            recoveryThreshold < 0f ||
            recoveryThreshold > 1f
        )
        {
            throw new ArgumentOutOfRangeException(nameof(recoveryThreshold));
        }

        this.maximumHeat = maximumHeat;
        this.sprintHeatPerSecond = sprintHeatPerSecond;
        this.flightHeatPerSecond = flightHeatPerSecond;
        this.boostHeatPerSecond = boostHeatPerSecond;
        this.coolingPerSecond = coolingPerSecond;
        this.coolingDelaySeconds = coolingDelaySeconds;
        recoveryHeat = maximumHeat * recoveryThreshold;
    }

    public float Heat => heat;
    public float MaximumHeat => maximumHeat;
    public float NormalizedHeat => heat / maximumHeat;
    public float CoolingDelayRemaining => coolingDelayRemaining;
    public bool IsOverheated => isOverheated;
    public bool CanUsePropulsion => !isOverheated;

    public bool Advance(PowerSuitPropulsionLoad requestedLoad, float deltaSeconds)
    {
        if (
            float.IsNaN(deltaSeconds) ||
            float.IsInfinity(deltaSeconds) ||
            deltaSeconds < 0f
        )
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        bool wasOverheated = isOverheated;
        float generationRate = isOverheated
            ? 0f
            : GetGenerationRate(requestedLoad);
        if (generationRate > 0f)
        {
            heat = Math.Min(maximumHeat, heat + generationRate * deltaSeconds);
            coolingDelayRemaining = coolingDelaySeconds;
            if (heat >= maximumHeat)
            {
                heat = maximumHeat;
                isOverheated = true;
            }
        }
        else
        {
            float coolingSeconds = deltaSeconds;
            if (coolingDelayRemaining > 0f)
            {
                float delayConsumed = Math.Min(
                    coolingDelayRemaining,
                    coolingSeconds
                );
                coolingDelayRemaining -= delayConsumed;
                coolingSeconds -= delayConsumed;
            }

            if (coolingSeconds > 0f && coolingPerSecond > 0f)
            {
                heat = Math.Max(0f, heat - coolingPerSecond * coolingSeconds);
            }

            if (isOverheated && heat <= recoveryHeat)
            {
                isOverheated = false;
            }
        }

        return wasOverheated != isOverheated;
    }

    public void Reset(float normalizedHeat = 0f)
    {
        if (float.IsNaN(normalizedHeat) || float.IsInfinity(normalizedHeat))
        {
            normalizedHeat = 0f;
        }

        heat = maximumHeat * Math.Max(0f, Math.Min(1f, normalizedHeat));
        coolingDelayRemaining = 0f;
        isOverheated = heat >= maximumHeat;
    }

    private float GetGenerationRate(PowerSuitPropulsionLoad load)
    {
        switch (load)
        {
            case PowerSuitPropulsionLoad.Sprint:
                return sprintHeatPerSecond;
            case PowerSuitPropulsionLoad.Flight:
                return flightHeatPerSecond;
            case PowerSuitPropulsionLoad.Boost:
                return boostHeatPerSecond;
            default:
                return 0f;
        }
    }

    private static void ValidateFinitePositive(float value, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateFiniteNonNegative(float value, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(-150)]
public sealed class PowerSuitPropulsionHeat : MonoBehaviour
{
    [SerializeField] private PowerSuitController controller;
    [SerializeField] private PowerSuitPropulsionHeatSettings settings =
        new PowerSuitPropulsionHeatSettings();

    private PowerSuitPropulsionHeatState state;

    public float Heat => State.Heat;
    public float MaximumHeat => State.MaximumHeat;
    public float NormalizedHeat => State.NormalizedHeat;
    public float CoolingDelayRemaining => State.CoolingDelayRemaining;
    public bool IsOverheated => State.IsOverheated;
    public bool CanUsePropulsion => State.CanUsePropulsion;
    public PowerSuitPropulsionHeatState State
    {
        get
        {
            if (state == null)
            {
                state = Settings.CreateState();
            }

            return state;
        }
    }

    public event Action<bool> OverheatStateChanged;

    private PowerSuitPropulsionHeatSettings Settings
    {
        get
        {
            if (settings == null)
            {
                settings = new PowerSuitPropulsionHeatSettings();
            }

            return settings;
        }
    }

    private void Awake()
    {
        ResolveController();
        state = Settings.CreateState();
    }

    private void OnEnable()
    {
        ResolveController();
    }

    private void Update()
    {
        PowerSuitPropulsionLoad load = ResolveCurrentLoad();
        bool changed = State.Advance(load, Time.deltaTime);
        if (changed)
        {
            OverheatStateChanged?.Invoke(State.IsOverheated);
        }
    }

    public void ResetHeat(float normalizedHeat = 0f)
    {
        bool wasOverheated = State.IsOverheated;
        State.Reset(normalizedHeat);
        if (wasOverheated != State.IsOverheated)
        {
            OverheatStateChanged?.Invoke(State.IsOverheated);
        }
    }

    public PowerSuitPropulsionLoad ResolveCurrentLoad()
    {
        if (controller == null)
        {
            ResolveController();
        }

        if (controller == null)
        {
            return PowerSuitPropulsionLoad.None;
        }

        if (controller.IsFlying)
        {
            return controller.IsBoosting
                ? PowerSuitPropulsionLoad.Boost
                : PowerSuitPropulsionLoad.Flight;
        }

        return controller.IsRunning
            ? PowerSuitPropulsionLoad.Sprint
            : PowerSuitPropulsionLoad.None;
    }

    private void ResolveController()
    {
        controller ??= GetComponent<PowerSuitController>();
    }

    private void OnValidate()
    {
        Settings.Sanitize();
    }
}
