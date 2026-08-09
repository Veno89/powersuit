using UnityEngine;

public sealed class PowerSuitAnimationDriver : MonoBehaviour
{
    public const string AirborneAimLayerName = "Airborne Aim";
    public const string MovementXParameterName = "MovementX";
    public const string MovementYParameterName = "MovementY";
    public const string MovementSpeedParameterName = "MovementSpeed";
    public const string LocomotionPlaybackSpeedParameterName =
        "LocomotionPlaybackSpeed";
    public const string IsBackpedalingParameterName = "IsBackpedaling";
    public const string IsAimWalkingParameterName = "IsAimWalking";

    [SerializeField] private PowerSuitController controller;
    [SerializeField] private Animator animator;
    [SerializeField] private PowerSuitWeaponPresentation weaponPresentation;
    [SerializeField, Min(0f)] private float movementDamping = 0.1f;
    [SerializeField, Min(1f)] private float fullSpeedLocomotionPlayback = 2f;
    [SerializeField, Min(0f)] private float airborneAimBlendSharpness = 12f;

    private static readonly int IsMovingParameter =
        Animator.StringToHash("IsMoving");

    private static readonly int IsFlyingParameter =
        Animator.StringToHash("IsFlying");

    private static readonly int IsAimingParameter =
        Animator.StringToHash("IsAiming");

    private static readonly int MovementXParameter =
        Animator.StringToHash(MovementXParameterName);

    private static readonly int MovementYParameter =
        Animator.StringToHash(MovementYParameterName);

    private static readonly int MovementSpeedParameter =
        Animator.StringToHash(MovementSpeedParameterName);

    private static readonly int LocomotionPlaybackSpeedParameter =
        Animator.StringToHash(LocomotionPlaybackSpeedParameterName);

    private static readonly int IsBackpedalingParameter =
        Animator.StringToHash(IsBackpedalingParameterName);

    private static readonly int IsAimWalkingParameter =
        Animator.StringToHash(IsAimWalkingParameterName);

    private bool hasMovementX;
    private bool hasMovementY;
    private bool hasMovementSpeed;
    private bool hasLocomotionPlaybackSpeed;
    private bool hasIsBackpedaling;
    private bool hasIsAimWalking;
    private int airborneAimLayerIndex = -1;
    private float airborneAimLayerWeight;

    private void Awake()
    {
        ResolveDependencies();

        if (controller == null || animator == null)
        {
            Debug.LogError(
                "Could not find the PowerSuitController or Animator.",
                this
            );

            enabled = false;
            return;
        }

        CacheAnimatorBindings();
    }

    private void OnEnable()
    {
        // Unity can reload scripts while Play Mode remains active. Runtime
        // layer indices and parameter caches are not serialized, so rebuild
        // them whenever the adapter is enabled as well as during first Awake.
        ResolveDependencies();
        if (controller != null && animator != null)
        {
            CacheAnimatorBindings();
        }
    }

    private void ResolveDependencies()
    {
        if (controller == null)
        {
            controller = GetComponent<PowerSuitController>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (weaponPresentation == null)
        {
            weaponPresentation = GetComponent<PowerSuitWeaponPresentation>();
        }
    }

    private void CacheAnimatorBindings()
    {
        hasMovementX = HasParameter(MovementXParameter, AnimatorControllerParameterType.Float);
        hasMovementY = HasParameter(MovementYParameter, AnimatorControllerParameterType.Float);
        hasMovementSpeed = HasParameter(MovementSpeedParameter, AnimatorControllerParameterType.Float);
        hasLocomotionPlaybackSpeed = HasParameter(
            LocomotionPlaybackSpeedParameter,
            AnimatorControllerParameterType.Float
        );
        hasIsBackpedaling = HasParameter(IsBackpedalingParameter, AnimatorControllerParameterType.Bool);
        hasIsAimWalking = HasParameter(IsAimWalkingParameter, AnimatorControllerParameterType.Bool);
        airborneAimLayerIndex = animator.GetLayerIndex(AirborneAimLayerName);
        if (airborneAimLayerIndex >= 0)
        {
            animator.SetLayerWeight(airborneAimLayerIndex, 0f);
        }
    }

    private void Update()
    {
        bool canAnimateAim =
            weaponPresentation == null ||
            weaponPresentation.CanUseWeapon;

        animator.SetBool(
            IsMovingParameter,
            controller.IsMoving
        );

        animator.SetBool(
            IsFlyingParameter,
            controller.IsFlying
        );

        animator.SetBool(
            IsAimingParameter,
            controller.IsAiming && canAnimateAim
        );

        UpdateAirborneAimLayer(canAnimateAim);

        SetOptionalFloat(
            MovementXParameter,
            controller.MovementX,
            hasMovementX
        );

        SetOptionalFloat(
            MovementYParameter,
            controller.MovementY,
            hasMovementY
        );

        SetOptionalFloat(
            MovementSpeedParameter,
            controller.MovementSpeedNormalized,
            hasMovementSpeed
        );

        SetOptionalFloat(
            LocomotionPlaybackSpeedParameter,
            PowerSuitLocomotionMath.CalculateLocomotionPlaybackSpeed(
                controller.MovementSpeedNormalized,
                fullSpeedLocomotionPlayback
            ),
            hasLocomotionPlaybackSpeed
        );

        if (hasIsBackpedaling)
        {
            animator.SetBool(
                IsBackpedalingParameter,
                controller.IsBackpedaling
            );
        }

        if (hasIsAimWalking)
        {
            animator.SetBool(
                IsAimWalkingParameter,
                controller.IsAimWalking && canAnimateAim
            );
        }
    }

    private void UpdateAirborneAimLayer(bool canAnimateAim)
    {
        if (airborneAimLayerIndex < 0)
        {
            return;
        }

        float targetWeight =
            controller.IsFlying &&
            controller.IsAiming &&
            canAnimateAim
                ? 1f
                : 0f;
        float blendFactor = PowerSuitCameraMath.ExponentialDampingFactor(
            airborneAimBlendSharpness,
            Time.deltaTime
        );
        airborneAimLayerWeight = Mathf.Lerp(
            airborneAimLayerWeight,
            targetWeight,
            blendFactor
        );

        if (Mathf.Abs(airborneAimLayerWeight - targetWeight) < 0.001f)
        {
            airborneAimLayerWeight = targetWeight;
        }

        animator.SetLayerWeight(
            airborneAimLayerIndex,
            airborneAimLayerWeight
        );
    }

    private void OnDisable()
    {
        airborneAimLayerWeight = 0f;
        if (animator != null && airborneAimLayerIndex >= 0)
        {
            animator.SetLayerWeight(airborneAimLayerIndex, 0f);
        }
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

    private void SetOptionalFloat(
        int parameterHash,
        float value,
        bool isAvailable
    )
    {
        if (!isAvailable)
        {
            return;
        }

        animator.SetFloat(
            parameterHash,
            value,
            movementDamping,
            Time.deltaTime
        );
    }

    private void OnValidate()
    {
        movementDamping = Mathf.Max(0f, movementDamping);
        fullSpeedLocomotionPlayback = Mathf.Max(
            1f,
            fullSpeedLocomotionPlayback
        );
        airborneAimBlendSharpness = Mathf.Max(0f, airborneAimBlendSharpness);
    }
}
