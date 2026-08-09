using System;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum PowerSuitWeaponPresentationState
{
    Ready,
    Drawing,
    Stowed,
    Sheathing
}

/// <summary>
/// Deterministic weapon carry state independent of input, animation, and frame
/// timing. Transition requests are accepted only from stable endpoint states.
/// </summary>
public sealed class PowerSuitWeaponPresentationStateMachine
{
    private readonly float drawDuration;
    private readonly float sheatheDuration;
    private float remainingTransitionTime;

    public PowerSuitWeaponPresentationState State { get; private set; }

    public bool CanUseWeapon => State == PowerSuitWeaponPresentationState.Ready;

    public bool IsTransitioning =>
        State == PowerSuitWeaponPresentationState.Drawing ||
        State == PowerSuitWeaponPresentationState.Sheathing;

    public float RemainingTransitionTime => remainingTransitionTime;

    public PowerSuitWeaponPresentationStateMachine(
        float drawDuration,
        float sheatheDuration,
        bool startsStowed = false
    )
    {
        ValidateDuration(drawDuration, nameof(drawDuration));
        ValidateDuration(sheatheDuration, nameof(sheatheDuration));

        this.drawDuration = drawDuration;
        this.sheatheDuration = sheatheDuration;
        State = startsStowed
            ? PowerSuitWeaponPresentationState.Stowed
            : PowerSuitWeaponPresentationState.Ready;
    }

    public bool RequestDraw()
    {
        if (State != PowerSuitWeaponPresentationState.Stowed)
        {
            return false;
        }

        State = PowerSuitWeaponPresentationState.Drawing;
        remainingTransitionTime = drawDuration;
        return true;
    }

    public bool RequestSheathe()
    {
        if (State != PowerSuitWeaponPresentationState.Ready)
        {
            return false;
        }

        State = PowerSuitWeaponPresentationState.Sheathing;
        remainingTransitionTime = sheatheDuration;
        return true;
    }

    public bool Toggle()
    {
        if (State == PowerSuitWeaponPresentationState.Ready)
        {
            return RequestSheathe();
        }

        if (State == PowerSuitWeaponPresentationState.Stowed)
        {
            return RequestDraw();
        }

        return false;
    }

    /// <summary>
    /// Advances the active transition. Returns true only on the tick that a
    /// stable endpoint is reached.
    /// </summary>
    public bool Tick(float deltaTime)
    {
        if (
            deltaTime < 0f ||
            float.IsNaN(deltaTime) ||
            float.IsInfinity(deltaTime)
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaTime),
                "Tick duration must be finite and non-negative."
            );
        }

        if (!IsTransitioning || deltaTime == 0f)
        {
            return false;
        }

        remainingTransitionTime = Mathf.Max(
            0f,
            remainingTransitionTime - deltaTime
        );

        if (remainingTransitionTime > 0f)
        {
            return false;
        }

        State = State == PowerSuitWeaponPresentationState.Drawing
            ? PowerSuitWeaponPresentationState.Ready
            : PowerSuitWeaponPresentationState.Stowed;

        return true;
    }

    private static void ValidateDuration(float duration, string parameterName)
    {
        if (
            duration <= 0f ||
            float.IsNaN(duration) ||
            float.IsInfinity(duration)
        )
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Transition duration must be finite and greater than zero."
            );
        }
    }
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class PowerSuitWeaponPresentation : MonoBehaviour
{
    public const string WeaponStowedParameterName = "WeaponStowed";
    public const string DrawWeaponTriggerName = "DrawWeapon";
    public const string SheatheWeaponTriggerName = "SheatheWeapon";
    public const string StowedLocomotionStateName = "Stowed Locomotion";

    private const float MinimumTransitionDuration = 0.01f;

    [SerializeField] private PowerSuitController controller;
    [SerializeField] private Animator animator;
    [SerializeField] private PowerSuitWeapon weapon;
    [SerializeField] private bool startsStowed;
    [SerializeField, Min(MinimumTransitionDuration)] private float drawDuration = 1f;
    [SerializeField, Min(MinimumTransitionDuration)] private float sheatheDuration = 1f;

    private static readonly int WeaponStowedParameter =
        Animator.StringToHash(WeaponStowedParameterName);

    private static readonly int DrawWeaponTrigger =
        Animator.StringToHash(DrawWeaponTriggerName);

    private static readonly int SheatheWeaponTrigger =
        Animator.StringToHash(SheatheWeaponTriggerName);

    private static readonly int StowedLocomotionState =
        Animator.StringToHash(StowedLocomotionStateName);

    private PowerSuitWeaponPresentationStateMachine stateMachine;
    private bool hasWeaponStowed;
    private bool hasDrawWeapon;
    private bool hasSheatheWeapon;

    public PowerSuitWeaponPresentationState State
    {
        get
        {
            EnsureStateMachine();
            return stateMachine.State;
        }
    }

    public bool CanUseWeapon
    {
        get
        {
            EnsureStateMachine();
            return stateMachine.CanUseWeapon;
        }
    }

    public bool IsTransitioning
    {
        get
        {
            EnsureStateMachine();
            return stateMachine.IsTransitioning;
        }
    }

    private void Awake()
    {
        EnsureStateMachine();

        if (controller == null)
        {
            controller = GetComponent<PowerSuitController>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (weapon == null)
        {
            weapon = GetComponent<PowerSuitWeapon>();
        }

        CacheAnimatorParameters();
        UpdateWeaponStowedParameter();
        if (
            State == PowerSuitWeaponPresentationState.Stowed &&
            animator != null &&
            animator.HasState(0, StowedLocomotionState)
        )
        {
            animator.Play(StowedLocomotionState, 0, 0f);
        }
        UpdateWeaponAvailability();
    }

    private void Update()
    {
        if (WasTogglePressed())
        {
            Toggle();
        }

        if (
            controller != null &&
            controller.IsAiming &&
            State == PowerSuitWeaponPresentationState.Stowed
        )
        {
            RequestDraw();
        }

        if (stateMachine.Tick(Time.deltaTime))
        {
            UpdateWeaponStowedParameter();
        }

        UpdateWeaponAvailability();
    }

    public bool RequestDraw()
    {
        EnsureStateMachine();

        if (IsWeaponBusy() || !stateMachine.RequestDraw())
        {
            return false;
        }

        ResetOptionalTrigger(SheatheWeaponTrigger, hasSheatheWeapon);
        SetOptionalTrigger(DrawWeaponTrigger, hasDrawWeapon);
        UpdateWeaponAvailability();
        return true;
    }

    public bool RequestSheathe()
    {
        EnsureStateMachine();

        if (IsWeaponBusy() || !stateMachine.RequestSheathe())
        {
            return false;
        }

        ResetOptionalTrigger(DrawWeaponTrigger, hasDrawWeapon);
        SetOptionalTrigger(SheatheWeaponTrigger, hasSheatheWeapon);
        UpdateWeaponAvailability();
        return true;
    }

    public bool Toggle()
    {
        EnsureStateMachine();

        if (State == PowerSuitWeaponPresentationState.Ready)
        {
            return RequestSheathe();
        }

        if (State == PowerSuitWeaponPresentationState.Stowed)
        {
            return RequestDraw();
        }

        return false;
    }

    private void EnsureStateMachine()
    {
        if (stateMachine != null)
        {
            return;
        }

        stateMachine = new PowerSuitWeaponPresentationStateMachine(
            Mathf.Max(MinimumTransitionDuration, drawDuration),
            Mathf.Max(MinimumTransitionDuration, sheatheDuration),
            startsStowed
        );
    }

    private void CacheAnimatorParameters()
    {
        if (animator == null)
        {
            return;
        }

        hasWeaponStowed = HasParameter(
            WeaponStowedParameter,
            AnimatorControllerParameterType.Bool
        );

        hasDrawWeapon = HasParameter(
            DrawWeaponTrigger,
            AnimatorControllerParameterType.Trigger
        );

        hasSheatheWeapon = HasParameter(
            SheatheWeaponTrigger,
            AnimatorControllerParameterType.Trigger
        );
    }

    private bool HasParameter(
        int parameterHash,
        AnimatorControllerParameterType expectedType
    )
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (
                parameter.nameHash == parameterHash &&
                parameter.type == expectedType
            )
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateWeaponStowedParameter()
    {
        if (!hasWeaponStowed)
        {
            return;
        }

        bool isStablyStowed =
            State == PowerSuitWeaponPresentationState.Stowed;

        animator.SetBool(
            WeaponStowedParameter,
            isStablyStowed
        );
    }

    private void UpdateWeaponAvailability()
    {
        if (weapon != null)
        {
            weapon.PresentationAllowsFire = CanUseWeapon;
            weapon.PresentationAllowsReload =
                CanUseWeapon &&
                (controller == null || !controller.IsFlying);
        }
    }

    private bool IsWeaponBusy()
    {
        return (controller != null && controller.IsFlying) ||
            (weapon != null && (weapon.IsReloading || weapon.IsCycling));
    }

    private void SetOptionalTrigger(int triggerHash, bool isAvailable)
    {
        if (isAvailable)
        {
            animator.SetTrigger(triggerHash);
        }
    }

    private void ResetOptionalTrigger(int triggerHash, bool isAvailable)
    {
        if (isAvailable)
        {
            animator.ResetTrigger(triggerHash);
        }
    }

    private static bool WasTogglePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return
            Keyboard.current != null &&
            Keyboard.current.qKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Q);
#endif
    }

    private void OnValidate()
    {
        drawDuration = Mathf.Max(MinimumTransitionDuration, drawDuration);
        sheatheDuration = Mathf.Max(MinimumTransitionDuration, sheatheDuration);
    }
}
