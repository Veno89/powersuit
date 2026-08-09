using UnityEngine;

public sealed class PowerSuitAnimationDriver : MonoBehaviour
{
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

    private void Awake()
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

        if (controller == null || animator == null)
        {
            Debug.LogError(
                "Could not find the PowerSuitController or Animator.",
                this
            );

            enabled = false;
            return;
        }

        hasMovementX = HasParameter(MovementXParameter, AnimatorControllerParameterType.Float);
        hasMovementY = HasParameter(MovementYParameter, AnimatorControllerParameterType.Float);
        hasMovementSpeed = HasParameter(MovementSpeedParameter, AnimatorControllerParameterType.Float);
        hasLocomotionPlaybackSpeed = HasParameter(
            LocomotionPlaybackSpeedParameter,
            AnimatorControllerParameterType.Float
        );
        hasIsBackpedaling = HasParameter(IsBackpedalingParameter, AnimatorControllerParameterType.Bool);
        hasIsAimWalking = HasParameter(IsAimWalkingParameter, AnimatorControllerParameterType.Bool);
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
    }
}
