using UnityEngine;
using UnityEngine.Serialization;

[DefaultExecutionOrder(100)]
public sealed class PowerSuitAnimationDriver : MonoBehaviour
{
    public const string ForwardWeaponPoseLayerName = "Forward Weapon Pose";
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
    [SerializeField] private PowerSuitWeaponAnimationDriver weaponAnimationDriver;
    [SerializeField, Min(0f)] private float movementDamping = 0.1f;
    [SerializeField, Min(1f)] private float fullSpeedLocomotionPlayback = 2f;
    [FormerlySerializedAs("airborneAimBlendSharpness")]
    [SerializeField, Min(0f)] private float forwardPoseBlendSharpness = 12f;

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
    private int forwardWeaponPoseLayerIndex = -1;
    private float forwardWeaponPoseLayerWeight;

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

        if (weaponAnimationDriver == null)
        {
            weaponAnimationDriver = GetComponent<PowerSuitWeaponAnimationDriver>();
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
        forwardWeaponPoseLayerIndex = animator.GetLayerIndex(
            ForwardWeaponPoseLayerName
        );
        forwardWeaponPoseLayerWeight =
            weaponAnimationDriver != null &&
            weaponAnimationDriver.RequiresForwardWeaponPose
                ? 1f
                : 0f;
        if (forwardWeaponPoseLayerIndex >= 0)
        {
            animator.SetLayerWeight(
                forwardWeaponPoseLayerIndex,
                forwardWeaponPoseLayerWeight
            );
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

        UpdateForwardWeaponPoseLayer(canAnimateAim);

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

    private void UpdateForwardWeaponPoseLayer(bool canAnimateAim)
    {
        if (forwardWeaponPoseLayerIndex < 0)
        {
            return;
        }

        bool requestedByAim =
            controller.IsFlying && controller.IsAiming && canAnimateAim;
        bool requestedByFiring =
            weaponAnimationDriver != null &&
            weaponAnimationDriver.RequiresForwardWeaponPose;
        float targetWeight = requestedByAim || requestedByFiring
                ? 1f
                : 0f;
        if (requestedByFiring)
        {
            // CycleStarted is raised before projectile feedback. Snap the
            // shouldered pose in for the same rendered frame so a hip shot can
            // never be presented from the diagonal carry stance.
            forwardWeaponPoseLayerWeight = 1f;
            animator.SetLayerWeight(forwardWeaponPoseLayerIndex, 1f);
            return;
        }

        float blendFactor = PowerSuitCameraMath.ExponentialDampingFactor(
            forwardPoseBlendSharpness,
            Time.deltaTime
        );
        forwardWeaponPoseLayerWeight = Mathf.Lerp(
            forwardWeaponPoseLayerWeight,
            targetWeight,
            blendFactor
        );

        if (Mathf.Abs(forwardWeaponPoseLayerWeight - targetWeight) < 0.001f)
        {
            forwardWeaponPoseLayerWeight = targetWeight;
        }

        animator.SetLayerWeight(
            forwardWeaponPoseLayerIndex,
            forwardWeaponPoseLayerWeight
        );
    }

    private void OnDisable()
    {
        ResetForRespawn();
    }

    /// <summary>
    /// Immediately clears transient upper-body pose blending. A normal release
    /// is damped for presentation quality, but death/respawn must not carry a
    /// partially blended firing pose into the restored player state.
    /// </summary>
    public void ResetForRespawn()
    {
        forwardWeaponPoseLayerWeight = 0f;
        if (animator != null && forwardWeaponPoseLayerIndex >= 0)
        {
            animator.SetLayerWeight(forwardWeaponPoseLayerIndex, 0f);
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
        forwardPoseBlendSharpness = Mathf.Max(0f, forwardPoseBlendSharpness);
    }
}
