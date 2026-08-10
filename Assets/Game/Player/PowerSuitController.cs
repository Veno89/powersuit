using System;
using Powersuit.Combat;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(-200)]
public sealed class PowerSuitController : MonoBehaviour
{
    private const float MovementStateThreshold = 0.01f;
    private const int CameraCollisionHitCapacity = 32;
    private const int AimHitCapacity = 32;

    public const float MinimumSpeedMultiplier = 0f;
    public const float MaximumSpeedMultiplier = 10f;

    public bool IsFlying => isFlying;

    public Camera PlayerCamera => playerCamera;

    public float GroundSpeedMultiplier => groundSpeedMultiplier;

    public float FlightSpeedMultiplier => flightSpeedMultiplier;

    public bool IsBoosting => isBoosting;

    public bool IsRunning => isRunning;

    public bool IsGrounded =>
        groundContactState != null
            ? groundContactState.IsGrounded
            : controller != null && controller.isGrounded;

    public float VerticalSpeed => verticalVelocity;

    /// <summary>
    /// Raised once when a previously unsupported controller contacts ground
    /// while descending. The value is the pre-collision downward speed.
    /// </summary>
    public event Action<float> Landed;

    public bool IsMoving =>
        controller != null &&
        PowerSuitLocomotionMath.ProjectOntoGroundPlane(
            controller.velocity
        ).sqrMagnitude > 0.05f;

    public bool IsAiming => isAiming;

    public bool IsScoped =>
        weaponAimState != null && weaponAimState.IsScoped;

    public WeaponAimMode AimMode =>
        weaponAimState != null
            ? weaponAimState.Mode
            : isAiming
                ? WeaponAimMode.ShoulderAim
                : WeaponAimMode.Exploration;

    public float ScopeBlend => weaponAimState?.ScopeBlend ?? 0f;

    /// <summary>
    /// Raw player intent to aim. Presentation can use this request to draw a
    /// stowed weapon while <see cref="IsAiming"/> remains false until the
    /// weapon has reached its usable ready state.
    /// </summary>
    public bool AimRequested => aimRequested;

    /// <summary>
    /// True after an unlocked primary click has been consumed to recapture the
    /// cursor and until that button is released. Weapon input must ignore the
    /// same physical click.
    /// </summary>
    public bool IsPrimaryFireSuppressed =>
        !cursorLocked || suppressPrimaryFireUntilReleased;

    /// <summary>
    /// The controller's actual planar velocity in suit-local space, normalized
    /// against the active movement speed. X is right/left and Y is
    /// forward/backward. Both values are suitable for directional blend trees.
    /// </summary>
    public Vector2 LocalMovement => localMovement;

    public float MovementX => localMovement.x;

    public float MovementY => localMovement.y;

    public float MovementSpeedNormalized => localMovement.magnitude;

    public bool IsBackpedaling =>
        !isFlying &&
        MovementY < -MovementStateThreshold;

    public bool IsAimWalking =>
        !isFlying &&
        isAiming &&
        MovementSpeedNormalized > MovementStateThreshold;

    public Vector2 ReticleScreenPosition =>
        new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) + currentReticleOffset;

    public Vector2 ReticleOffset => currentReticleOffset;

    [Header("Ground Movement")]
    [SerializeField] private float walkSpeed = 6.5f;
    [SerializeField] private float groundAcceleration = 55f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -25f;

    [Header("Flight")]
    [SerializeField] private float flightSpeed = 14f;
    [SerializeField] private float boostSpeed = 28f;
    [SerializeField] private float flightAcceleration = 38f;
    [SerializeField] private float turningSpeed = 20f;
    [SerializeField] private float combatTurningSpeed = 32f;

    [Header("Runtime Tuning")]
    [SerializeField, Range(MinimumSpeedMultiplier, MaximumSpeedMultiplier)]
    private float groundSpeedMultiplier = 1f;
    [SerializeField, Range(MinimumSpeedMultiplier, MaximumSpeedMultiplier)]
    private float flightSpeedMultiplier = 1f;

    [Header("Movement Feel")]
    [SerializeField]
    private PowerSuitMovementSettings movementSettings =
        new PowerSuitMovementSettings();

    [Header("Camera")]
    [SerializeField] private float cameraDistance = 9.5f;
    [SerializeField] private float cameraHeight = 1.5f;
    [SerializeField] private float mouseSensitivity = 0.18f;
    [SerializeField] private float controllerLookSpeed = 180f;
    [SerializeField] private float minimumPitch = -55f;
    [SerializeField] private float maximumPitch = 70f;
    [SerializeField] private float cameraCollisionRadius = 0.2f;
    [SerializeField] private float cameraCollisionPadding = 0.05f;
    [SerializeField] private float cameraCollisionReleaseSharpness = 14f;
    [SerializeField] private float cameraLookSharpness = 45f;

    [Header("Flight Camera")]
    [SerializeField] private float flightCameraDistance = 11f;
    [SerializeField] private float flightCameraHeight = 1.75f;
    [SerializeField] private float flightFieldOfView = 74f;
    [SerializeField] private float boostCameraDistance = 12f;
    [SerializeField] private float boostCameraHeight = 1.8f;
    [SerializeField] private float boostFieldOfView = 82f;

    [Header("Third-Person Aim Mode")]
    [SerializeField] private float aimCameraDistance = 4.3f;
    [SerializeField] private float aimCameraHeight = 1.45f;
    [SerializeField] private Vector3 aimShoulderOffset = new Vector3(-1.2f, 0.05f, 0f);
    [SerializeField] private float defaultFieldOfView = 72f;
    [SerializeField] private float aimFieldOfView = 62f;
    [SerializeField] private float aimTransitionSpeed = 22f;
    [SerializeField] private float maxReticleOffset = 140f;
    [SerializeField] private float aimMaxDistance = 200f;

    [Header("Precision Scope")]
    [SerializeField] private Transform scopePoint;
    [SerializeField, Min(0f)] private float scopeEyeRelief = 0.045f;
    [SerializeField, Min(0.001f)] private float scopedNearClipPlane = 0.02f;
    [SerializeField] private ScopeActivationPolicy scopeActivationPolicy =
        ScopeActivationPolicy.Toggle;

    private CharacterController controller;
    private Camera playerCamera;
    private PowerSuitWeaponAnimationDriver weaponAnimationDriver;
    private PowerSuitWeaponPresentation weaponPresentation;
    private PowerSuitWeapon weapon;
    private PlayerHealth playerHealth;
    private WeaponAimState weaponAimState;
    private WeaponDefinition weaponAimDefinition;
    private bool weaponAimStateUsesFallback;
    private PowerSuitInputRouter inputRouter;

    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private Vector2 localMovement;

    private float cameraYaw;
    private float cameraPitch = 15f;
    private float smoothedCameraYaw;
    private float smoothedCameraPitch;

    private bool isFlying;
    private bool isBoosting;
    private bool isRunning;
    private bool isAiming;
    private bool aimRequested;
    private bool cursorLocked;
    private bool suppressPrimaryFireUntilReleased;
    private int fallbackInputFrame = -1;
    private PowerSuitInputSnapshot fallbackInputSnapshot;
    private float flightTakeoffGraceRemaining;
    private PowerSuitGroundContactState groundContactState;
    private PowerSuitJumpFlightState jumpFlightState;

    private float currentCameraDistance;
    private float currentCameraHeight;
    private Vector3 currentShoulderOffset;
    private float currentFOV;
    private float currentCollisionDistance;
    private bool cameraOccluded;
    private Vector2 currentReticleOffset;
    private bool scopeHeld;
    private bool scopePressedThisFrame;
    private int scopePressEvaluatedFrame = -1;
    private float defaultNearClipPlane;

    private readonly RaycastHit[] cameraCollisionHits =
        new RaycastHit[CameraCollisionHitCapacity];
    private readonly RaycastHit[] aimHits = new RaycastHit[AimHitCapacity];

    [Header("Recoil")]
    [SerializeField] private float recoilRecoverySpeed = 15f;
    [SerializeField] private float maxAccumulatedRecoil = 4f;
    private Vector2 currentRecoilOffset;

    public void AddRecoil(float pitchKick, float yawKick)
    {
        currentRecoilOffset.y += pitchKick;
        currentRecoilOffset.x += UnityEngine.Random.Range(-yawKick, yawKick);
        currentRecoilOffset = Vector2.ClampMagnitude(currentRecoilOffset, maxAccumulatedRecoil);
    }

    public Transform ScopePoint
    {
        get => scopePoint;
        set => scopePoint = value;
    }

    /// <summary>
    /// Applies a bounded runtime multiplier to ground movement speed. NaN
    /// leaves the current value unchanged; infinities clamp to the safe range.
    /// </summary>
    public float SetGroundSpeedMultiplier(float value)
    {
        groundSpeedMultiplier = ClampRuntimeSpeedMultiplier(
            value,
            groundSpeedMultiplier
        );
        return groundSpeedMultiplier;
    }

    /// <summary>
    /// Applies a bounded runtime multiplier to normal flight, boost, and
    /// vertical flight speed while leaving acceleration tuning authoritative.
    /// </summary>
    public float SetFlightSpeedMultiplier(float value)
    {
        flightSpeedMultiplier = ClampRuntimeSpeedMultiplier(
            value,
            flightSpeedMultiplier
        );
        return flightSpeedMultiplier;
    }

    /// <summary>
    /// Accepted non-aim shots still need the character and shouldered rifle to
    /// face the camera's combat ray. This changes only body heading: it does
    /// not enable aim zoom, aim spread, or the aim locomotion state.
    /// </summary>
    public void FaceCameraForWeaponFire()
    {
        if (playerCamera == null)
        {
            return;
        }

        Vector3 cameraForward = Vector3.ProjectOnPlane(
            playerCamera.transform.forward,
            Vector3.up
        );
        if (cameraForward.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(
            cameraForward.normalized,
            Vector3.up
        );
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        playerCamera = Camera.main;
        weaponAnimationDriver = GetComponent<PowerSuitWeaponAnimationDriver>();
        weaponPresentation = GetComponent<PowerSuitWeaponPresentation>();
        weapon = GetComponent<PowerSuitWeapon>();
        playerHealth = GetComponent<PlayerHealth>();
        inputRouter = GetComponent<PowerSuitInputRouter>();
        EnsureWeaponAimState();
        GetMovementSettings().Sanitize();
        InitializeGroundContactState();

        if (playerCamera == null)
        {
            Debug.LogError(
                "No camera tagged MainCamera was found.",
                this
            );

            enabled = false;
            return;
        }

        cameraYaw = transform.eulerAngles.y;
        smoothedCameraYaw = cameraYaw;
        smoothedCameraPitch = cameraPitch;
        currentCameraDistance = cameraDistance;
        currentCameraHeight = cameraHeight;
        currentShoulderOffset = Vector3.zero;
        currentFOV = defaultFieldOfView;
        currentCollisionDistance = cameraDistance;
        playerCamera.fieldOfView = defaultFieldOfView;
        defaultNearClipPlane = playerCamera.nearClipPlane;

        SetCursorLocked(true);
    }

    private void Update()
    {
        if (
            suppressPrimaryFireUntilReleased &&
            !IsPrimaryClickHeld()
        )
        {
            suppressPrimaryFireUntilReleased = false;
        }

        HandleCursor();

        if (!cursorLocked)
        {
            return;
        }

        HandleAimingState();
        HandleCameraInput();

        if (isFlying)
        {
            HandleFlight();
        }
        else
        {
            HandleGroundMovement();
        }
    }

    private void LateUpdate()
    {
        if (playerCamera == null)
        {
            return;
        }

        UpdateCamera();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            SetCursorLocked(false);
        }
    }

    private void HandleGroundMovement()
    {
        float deltaTime = Time.deltaTime;
        PowerSuitMovementSettings tuning = GetMovementSettings();
        EnsureGroundContactState();
        groundContactState.Advance(controller.isGrounded, deltaTime);
        if (WasJumpPressed())
        {
            groundContactState.BufferJump();
        }

        isBoosting = false;
        isRunning = false;
        flightTakeoffGraceRemaining = 0f;

        // Planar and vertical velocity have separate owners. Sanitize legacy
        // or externally injected values before applying ground control.
        horizontalVelocity = PowerSuitLocomotionMath.ProjectOntoGroundPlane(
            horizontalVelocity
        );

        Vector2 input = ReadMovementInput();

        Vector3 cameraForward = Vector3.ProjectOnPlane(
            playerCamera.transform.forward,
            Vector3.up
        ).normalized;

        Vector3 cameraRight = Vector3.ProjectOnPlane(
            playerCamera.transform.right,
            Vector3.up
        ).normalized;

        Vector3 desiredDirection =
            cameraForward * input.y +
            cameraRight * input.x;

        desiredDirection = Vector3.ClampMagnitude(
            desiredDirection,
            1f
        );

        bool hasStableSupport = groundContactState.IsGrounded;
        isRunning = PowerSuitLocomotionMath.ShouldRun(
            hasStableSupport,
            IsBoostHeld(),
            aimRequested,
            isAiming,
            input
        );
        float effectiveWalkSpeed =
            PowerSuitLocomotionMath.CalculateGroundTargetSpeed(
                walkSpeed,
                isRunning,
                tuning.GroundRunSpeedMultiplier
            ) * groundSpeedMultiplier;
        Vector3 desiredVelocity =
            desiredDirection * effectiveWalkSpeed;

        horizontalVelocity = PowerSuitLocomotionMath.ApproachVelocity(
            horizontalVelocity,
            desiredVelocity,
            hasStableSupport
                ? groundAcceleration
                : tuning.AirAcceleration,
            hasStableSupport
                ? tuning.GroundDeceleration
                : tuning.AirDeceleration,
            hasStableSupport
                ? tuning.GroundBrakingAcceleration
                : tuning.AirBrakingAcceleration,
            deltaTime
        );

        bool didJump = groundContactState.TryConsumeBufferedJump();
        if (didJump)
        {
            verticalVelocity = PowerSuitLocomotionMath.CalculateJumpSpeed(
                jumpHeight,
                gravity
            );
            isRunning = false;
            EnsureJumpFlightState();
            jumpFlightState.Arm(IsJumpHeld());
        }
        else if (hasStableSupport)
        {
            verticalVelocity = Mathf.Min(
                0f,
                tuning.GroundedStickVelocity
            );
        }
        else
        {
            float jumpGravityScale =
                jumpFlightState != null &&
                jumpFlightState.IsArmed &&
                IsJumpHeld()
                    ? tuning.JumpHoldGravityScale
                    : 1f;
            verticalVelocity = PowerSuitLocomotionMath.ApplyGravity(
                verticalVelocity,
                gravity * jumpGravityScale,
                tuning.TerminalFallSpeed,
                deltaTime
            );
        }

        Vector3 movement =
            horizontalVelocity +
            Vector3.up * verticalVelocity;

        bool hadGroundSupportBeforeMove = controller.isGrounded;
        float preMoveVerticalSpeed = verticalVelocity;
        CollisionFlags collisionFlags = controller.Move(
            movement * deltaTime
        );
        RaiseLandingContactIfNeeded(
            collisionFlags,
            hadGroundSupportBeforeMove,
            preMoveVerticalSpeed
        );
        ReconcileVelocityAfterMove(collisionFlags);

        Vector3 facingDirection = PowerSuitLocomotionMath.ResolveFacingDirection(
            input,
            desiredDirection,
            transform.forward,
            cameraForward,
            ShouldFaceCameraForCombat()
        );

        RotateTowardsMovement(facingDirection);
        UpdateLocalMovement(effectiveWalkSpeed);

        EnsureJumpFlightState();
        bool isPhysicallyAirborne =
            (collisionFlags & CollisionFlags.Below) == 0 &&
            !controller.isGrounded;
        if (
            jumpFlightState.Advance(
                IsJumpHeld(),
                isPhysicallyAirborne,
                deltaTime
            )
        )
        {
            SetFlightEnabled(true);
        }
    }

    private void HandleFlight()
    {
        float deltaTime = Time.deltaTime;
        PowerSuitMovementSettings tuning = GetMovementSettings();
        flightTakeoffGraceRemaining = Mathf.Max(
            0f,
            flightTakeoffGraceRemaining - deltaTime
        );

        Vector2 input = ReadMovementInput();

        Vector3 cameraRelativeInput =
            playerCamera.transform.forward * input.y +
            playerCamera.transform.right * input.x;

        float verticalInput = ReadVerticalFlightInput();
        Vector3 desiredPlanarDirection = Vector3.ClampMagnitude(
            PowerSuitLocomotionMath.ProjectOntoGroundPlane(
                cameraRelativeInput
            ),
            1f
        );
        float desiredVerticalInput = Mathf.Clamp(
            cameraRelativeInput.y + verticalInput,
            -1f,
            1f
        );

        bool hasFlightIntent =
            desiredPlanarDirection.sqrMagnitude > 0.0001f ||
            Mathf.Abs(desiredVerticalInput) > 0.0001f;
        isBoosting = IsBoostHeld() && hasFlightIntent;

        float selectedPlanarSpeed =
            (isBoosting ? boostSpeed : flightSpeed) * flightSpeedMultiplier;
        float selectedVerticalSpeed = isBoosting
            ? tuning.BoostVerticalSpeed
            : tuning.FlightVerticalSpeed;
        selectedVerticalSpeed *= flightSpeedMultiplier;
        float accelerationMultiplier = isBoosting
            ? tuning.BoostAccelerationMultiplier
            : 1f;

        Vector3 desiredPlanarVelocity =
            desiredPlanarDirection * selectedPlanarSpeed;
        horizontalVelocity = PowerSuitLocomotionMath.ApproachVelocity(
            horizontalVelocity,
            desiredPlanarVelocity,
            flightAcceleration * accelerationMultiplier,
            tuning.FlightDeceleration,
            tuning.FlightBrakingAcceleration,
            deltaTime
        );

        float desiredVerticalVelocity =
            desiredVerticalInput * selectedVerticalSpeed;
        verticalVelocity = PowerSuitLocomotionMath.ApproachVelocity(
            verticalVelocity,
            desiredVerticalVelocity,
            tuning.FlightVerticalAcceleration * accelerationMultiplier,
            tuning.FlightVerticalDeceleration,
            tuning.FlightVerticalBrakingAcceleration,
            deltaTime
        );

        bool hadGroundSupportBeforeMove = controller.isGrounded;
        float preMoveVerticalSpeed = verticalVelocity;
        CollisionFlags collisionFlags = controller.Move(
            (
                horizontalVelocity +
                Vector3.up * verticalVelocity
            ) * deltaTime
        );
        RaiseLandingContactIfNeeded(
            collisionFlags,
            hadGroundSupportBeforeMove,
            preMoveVerticalSpeed
        );
        ReconcileVelocityAfterMove(collisionFlags);

        Vector3 planarDirection = desiredPlanarDirection;

        if (ShouldFaceCameraForCombat())
        {
            Vector3 cameraPlanar = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up);
            RotateTowardsDirection(cameraPlanar);
        }
        else
        {
            RotateTowardsMovement(planarDirection);
        }

        UpdateLocalMovement(selectedPlanarSpeed);

        if (
            PowerSuitLocomotionMath.ShouldCompleteFlightLanding(
                collisionFlags,
                flightTakeoffGraceRemaining,
                preMoveVerticalSpeed
            )
        )
        {
            CompleteFlightLanding();
        }
    }

    private void RotateTowardsMovement(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        RotateTowardsDirection(direction);
    }

    private bool ShouldFaceCameraForCombat()
    {
        return isAiming ||
            (weaponAnimationDriver != null &&
             weaponAnimationDriver.RequiresForwardWeaponPose);
    }

    private void RotateTowardsDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(
            direction.normalized,
            Vector3.up
        );

        float rotationSharpness = ShouldFaceCameraForCombat()
            ? combatTurningSpeed
            : turningSpeed;
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            PowerSuitCameraMath.ExponentialDampingFactor(
                rotationSharpness,
                Time.deltaTime
            )
        );
    }

    private void UpdateLocalMovement(float referenceSpeed)
    {
        Vector3 worldVelocity = controller != null
            ? controller.velocity
            : horizontalVelocity;

        localMovement = PowerSuitLocomotionMath.ToLocalMovement(
            transform.rotation,
            worldVelocity,
            referenceSpeed
        );
    }

    private void HandleCameraInput()
    {
        Vector2 mouseLook = ReadMouseLook();
        Vector2 controllerLook = ReadControllerLook();
        float sensitivityMultiplier = weaponAimState != null
            ? weaponAimState.Profile.GetLookSensitivityMultiplier(
                weaponAimState.Mode
            )
            : isAiming
                ? 0.9f
                : 1f;

        cameraYaw += mouseLook.x * mouseSensitivity * sensitivityMultiplier;
        cameraPitch -= mouseLook.y * mouseSensitivity * sensitivityMultiplier;

        cameraYaw += controllerLook.x *
                     controllerLookSpeed *
                     sensitivityMultiplier *
                     Time.deltaTime;

        cameraPitch -= controllerLook.y *
                       controllerLookSpeed *
                       sensitivityMultiplier *
                       Time.deltaTime;

        cameraPitch = Mathf.Clamp(
            cameraPitch,
            minimumPitch,
            maximumPitch
        );

        // Mouse delta already rotates the camera. Applying the same delta to
        // the reticle made small aim corrections move twice and caused the
        // camera-derived trajectory to drift away from screen centre.
    }

    private void HandleAimingState()
    {
        PowerSuitInputSnapshot input = ReadInputSnapshot();
        aimRequested = cursorLocked && input.AimHeld;
        scopeHeld = input.ScopeHeld;
        scopePressedThisFrame = input.ScopePressed;
        EvaluateWeaponAimState(advancePresentation: true);

        if (!isAiming)
        {
            currentReticleOffset = Vector2.MoveTowards(
                currentReticleOffset,
                Vector2.zero,
                Time.deltaTime * maxReticleOffset * 6f
            );
        }
    }

    private void UpdateCamera()
    {
        bool useBoostProfile = isFlying && isBoosting;
        float explorationDistance = useBoostProfile
            ? boostCameraDistance
            : isFlying
                ? flightCameraDistance
                : cameraDistance;
        float explorationHeight = useBoostProfile
            ? boostCameraHeight
            : isFlying
                ? flightCameraHeight
                : cameraHeight;
        float explorationFov = useBoostProfile
            ? boostFieldOfView
            : isFlying
                ? flightFieldOfView
                : defaultFieldOfView;

        float targetDistance = isAiming
            ? aimCameraDistance
            : explorationDistance;
        float targetHeight = isAiming
            ? aimCameraHeight
            : explorationHeight;
        Vector3 targetShoulder = isAiming ? aimShoulderOffset : Vector3.zero;
        float targetFOV = isAiming
            ? weaponAimState != null
                ? weaponAimState.Profile.GetFieldOfView(
                    weaponAimState.Mode,
                    explorationFov
                )
                : aimFieldOfView
            : explorationFov;
        float cameraDeltaTime = Time.unscaledDeltaTime;
        float transitionFactor = PowerSuitCameraMath.ExponentialDampingFactor(
            aimTransitionSpeed,
            cameraDeltaTime
        );

        currentCameraDistance = Mathf.Lerp(
            currentCameraDistance,
            targetDistance,
            transitionFactor
        );
        currentCameraHeight = Mathf.Lerp(
            currentCameraHeight,
            targetHeight,
            transitionFactor
        );
        currentShoulderOffset = Vector3.Lerp(
            currentShoulderOffset,
            targetShoulder,
            transitionFactor
        );
        currentFOV = Mathf.Lerp(currentFOV, targetFOV, transitionFactor);

        currentRecoilOffset = Vector2.MoveTowards(currentRecoilOffset, Vector2.zero, recoilRecoverySpeed * Time.deltaTime);

        float lookFactor = PowerSuitCameraMath.ExponentialDampingFactor(
            cameraLookSharpness,
            cameraDeltaTime
        );
        smoothedCameraYaw = Mathf.LerpAngle(
            smoothedCameraYaw,
            cameraYaw,
            lookFactor
        );
        smoothedCameraPitch = Mathf.LerpAngle(
            smoothedCameraPitch,
            cameraPitch,
            lookFactor
        );

        if (playerCamera != null)
        {
            playerCamera.fieldOfView = currentFOV;
        }

        Vector3 pivot = transform.position + Vector3.up * currentCameraHeight;
        float orbitPitch = smoothedCameraPitch;
        if (!isFlying)
        {
            float floorSafeOrbitPitch =
                PowerSuitCameraMath.CalculateFloorSafeMinimumPitch(
                    currentCameraDistance,
                    currentCameraHeight + currentShoulderOffset.y,
                    cameraCollisionRadius + cameraCollisionPadding,
                    minimumPitch
                );
            orbitPitch = Mathf.Max(orbitPitch, floorSafeOrbitPitch);
        }

        Quaternion cameraRotation = Quaternion.Euler(
            smoothedCameraPitch - currentRecoilOffset.y,
            smoothedCameraYaw + currentRecoilOffset.x,
            0f
        );
        Quaternion orbitRotation = Quaternion.Euler(
            orbitPitch,
            smoothedCameraYaw + currentRecoilOffset.x,
            0f
        );

        Vector3 cameraRight = orbitRotation * Vector3.right;
        Vector3 cameraUp = Vector3.up;
        Vector3 cameraForward = orbitRotation * Vector3.forward;

        Vector3 desiredPosition = pivot
            + (cameraRight * currentShoulderOffset.x)
            + (cameraUp * currentShoulderOffset.y)
            - (cameraForward * currentCameraDistance);

        float scopeBlend = ScopeBlend;
        if (scopePoint != null && scopeBlend > 0f)
        {
            Vector3 scopedPosition =
                scopePoint.position - scopePoint.forward * scopeEyeRelief;
            desiredPosition = Vector3.Lerp(
                desiredPosition,
                scopedPosition,
                scopeBlend
            );
        }

        if (playerCamera != null)
        {
            playerCamera.nearClipPlane = Mathf.Lerp(
                defaultNearClipPlane,
                scopedNearClipPlane,
                scopeBlend
            );
        }

        Vector3 rayDirection = desiredPosition - pivot;
        float distance = rayDirection.magnitude;

        float allowedDistance = FindAllowedCameraDistance(
            pivot,
            rayDirection,
            distance
        );

        currentCollisionDistance = PowerSuitCameraMath.ResolveCameraDistance(
            currentCollisionDistance,
            distance,
            allowedDistance,
            cameraOccluded,
            cameraCollisionReleaseSharpness,
            cameraDeltaTime,
            out cameraOccluded
        );

        if (distance <= 0.001f)
        {
            playerCamera.transform.position = pivot;
        }
        else
        {
            playerCamera.transform.position =
                pivot + rayDirection.normalized * currentCollisionDistance;
        }

        playerCamera.transform.rotation = cameraRotation;
    }

    private float FindAllowedCameraDistance(
        Vector3 pivot,
        Vector3 rayDirection,
        float distance
    )
    {
        if (distance <= 0.001f)
        {
            return 0f;
        }

        Vector3 direction = rayDirection / distance;
        int hitCount = Physics.SphereCastNonAlloc(
            pivot,
            cameraCollisionRadius,
            direction,
            cameraCollisionHits,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        float allowedDistance = ScanCameraHits(
            cameraCollisionHits,
            hitCount,
            distance
        );

        // NonAlloc hit order is undefined. A saturated buffer might omit the
        // closest wall, so take the rare allocating path only on overflow.
        if (hitCount == cameraCollisionHits.Length)
        {
            RaycastHit[] overflowHits = Physics.SphereCastAll(
                pivot,
                cameraCollisionRadius,
                direction,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );
            allowedDistance = Mathf.Min(
                allowedDistance,
                ScanCameraHits(overflowHits, overflowHits.Length, distance)
            );
        }

        return allowedDistance;
    }

    private float ScanCameraHits(
        RaycastHit[] hits,
        int hitCount,
        float unobstructedDistance
    )
    {
        float allowedDistance = unobstructedDistance;

        for (int index = 0; index < hitCount; index++)
        {
            RaycastHit hit = hits[index];
            if (hit.collider == null)
            {
                continue;
            }

            Transform hitTransform = hit.collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
            {
                continue;
            }

            allowedDistance = Mathf.Min(
                allowedDistance,
                Mathf.Max(0.2f, hit.distance - cameraCollisionPadding)
            );
        }

        return allowedDistance;
    }

    public Ray GetAimRay()
    {
        if (playerCamera == null)
        {
            return new Ray(transform.position + Vector3.up * cameraHeight, transform.forward);
        }

        return playerCamera.ScreenPointToRay(ReticleScreenPosition);
    }

    public Vector3 GetAimPoint(Vector3 muzzlePosition)
    {
        Ray aimRay = GetAimRay();
        Vector3 targetPoint;

        int hitCount = Physics.RaycastNonAlloc(
            aimRay,
            aimHits,
            aimMaxDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        bool foundHit = false;
        Vector3 hitPoint = Vector3.zero;
        float nearestDistance = float.PositiveInfinity;

        for (int index = 0; index < hitCount; index++)
        {
            RaycastHit hit = aimHits[index];
            if (
                hit.transform == transform ||
                hit.transform.IsChildOf(transform)
            )
            {
                continue;
            }

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                hitPoint = hit.point;
                foundHit = true;
            }
        }

        // As with camera collision, saturation makes the NonAlloc subset
        // unspecified. Preserve correctness in unusually collider-dense shots.
        if (hitCount == aimHits.Length)
        {
            RaycastHit[] overflowHits = Physics.RaycastAll(
                aimRay,
                aimMaxDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            foreach (RaycastHit hit in overflowHits)
            {
                if (
                    hit.transform == transform ||
                    hit.transform.IsChildOf(transform) ||
                    hit.distance >= nearestDistance
                )
                {
                    continue;
                }

                nearestDistance = hit.distance;
                hitPoint = hit.point;
                foundHit = true;
            }
        }

        if (foundHit)
        {
            targetPoint = hitPoint;
        }
        else
        {
            targetPoint = aimRay.origin + aimRay.direction * aimMaxDistance;
        }

        Vector3 muzzleToTarget = targetPoint - muzzlePosition;
        float dist = muzzleToTarget.magnitude;

        if (dist > 0.01f)
        {
            RaycastHit muzzleHit;
            if (Physics.Raycast(
                    muzzlePosition,
                    muzzleToTarget.normalized,
                    out muzzleHit,
                    dist,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore
                ))
            {
                if (
                    muzzleHit.transform != transform &&
                    !muzzleHit.transform.IsChildOf(transform)
                )
                {
                    targetPoint = muzzleHit.point;
                }
            }
        }

        return targetPoint;
    }

    private void HandleCursor()
    {
        if (WasEscapePressed())
        {
            SetCursorLocked(!cursorLocked);
        }

        if (!cursorLocked && WasPrimaryClickPressed())
        {
            suppressPrimaryFireUntilReleased = true;
            SetCursorLocked(true);
        }
    }

    private void SetCursorLocked(bool locked)
    {
        cursorLocked = locked;

        if (!locked)
        {
            aimRequested = false;
            isAiming = false;
            scopeHeld = false;
            scopePressedThisFrame = false;
            weaponAimState?.Reset();
            suppressPrimaryFireUntilReleased = true;
        }

        Cursor.lockState = locked
            ? CursorLockMode.Locked
            : CursorLockMode.None;

        Cursor.visible = !locked;
    }

    /// <summary>
    /// Enters or leaves flight while keeping planar and vertical momentum in
    /// their explicit channels. Ground takeoff receives lift; airborne entry
    /// and exit preserve their current vertical motion.
    /// </summary>
    public void SetFlightEnabled(bool enabled)
    {
        if (enabled == isFlying)
        {
            return;
        }

        PowerSuitMovementSettings tuning = GetMovementSettings();
        EnsureGroundContactState();
        horizontalVelocity = PowerSuitLocomotionMath.ProjectOntoGroundPlane(
            horizontalVelocity
        );

        if (enabled)
        {
            bool takingOffFromGround =
                groundContactState.IsGrounded ||
                (controller != null && controller.isGrounded);

            isFlying = true;
            isBoosting = false;
            isRunning = false;
            flightTakeoffGraceRemaining =
                tuning.FlightTakeoffGraceSeconds;
            if (takingOffFromGround)
            {
                verticalVelocity = Mathf.Max(
                    verticalVelocity,
                    tuning.FlightTakeoffSpeed
                );
            }

            groundContactState.ForceAirborneUntilSeparated();
            EnsureJumpFlightState();
            jumpFlightState.Reset();
            return;
        }

        if (controller != null && controller.isGrounded)
        {
            CompleteFlightLanding();
            return;
        }

        isFlying = false;
        isBoosting = false;
        isRunning = false;
        flightTakeoffGraceRemaining = 0f;
        verticalVelocity = Mathf.Max(
            verticalVelocity,
            -tuning.TerminalFallSpeed
        );
        groundContactState.ForceAirborneUntilSeparated();
    }

    private void CompleteFlightLanding()
    {
        PowerSuitMovementSettings tuning = GetMovementSettings();
        EnsureGroundContactState();
        isFlying = false;
        isBoosting = false;
        isRunning = false;
        flightTakeoffGraceRemaining = 0f;
        horizontalVelocity = PowerSuitLocomotionMath.ProjectOntoGroundPlane(
            horizontalVelocity
        );
        verticalVelocity = Mathf.Min(
            0f,
            tuning.GroundedStickVelocity
        );
        groundContactState.Reset(grounded: true);
        EnsureJumpFlightState();
        jumpFlightState.Reset();
    }

    private void InitializeGroundContactState()
    {
        PowerSuitMovementSettings tuning = GetMovementSettings();
        groundContactState = new PowerSuitGroundContactState(
            tuning.GroundedReleaseGraceSeconds,
            tuning.CoyoteTimeSeconds,
            tuning.JumpBufferSeconds
        );
        groundContactState.Reset(
            controller != null && controller.isGrounded
        );
    }

    private void EnsureGroundContactState()
    {
        if (groundContactState == null)
        {
            InitializeGroundContactState();
        }
    }

    private void EnsureJumpFlightState()
    {
        if (jumpFlightState == null)
        {
            jumpFlightState = new PowerSuitJumpFlightState(
                GetMovementSettings().JumpHoldFlightDelaySeconds
            );
        }
    }

    private PowerSuitMovementSettings GetMovementSettings()
    {
        if (movementSettings == null)
        {
            movementSettings = new PowerSuitMovementSettings();
        }

        return movementSettings;
    }

    private void ReconcileVelocityAfterMove(CollisionFlags collisionFlags)
    {
        if (controller == null || Time.deltaTime <= 0f)
        {
            return;
        }

        Vector3 actualVelocity = controller.velocity;
        horizontalVelocity = PowerSuitLocomotionMath.ProjectOntoGroundPlane(
            actualVelocity
        );

        bool hitCeiling =
            (collisionFlags & CollisionFlags.Above) != 0 &&
            verticalVelocity > 0f;
        bool hitGround =
            (collisionFlags & CollisionFlags.Below) != 0 &&
            verticalVelocity < 0f;
        verticalVelocity = hitCeiling || hitGround
            ? 0f
            : actualVelocity.y;
    }

    private void RaiseLandingContactIfNeeded(
        CollisionFlags collisionFlags,
        bool hadGroundSupportBeforeMove,
        float preMoveVerticalSpeed
    )
    {
        if (
            hadGroundSupportBeforeMove ||
            (collisionFlags & CollisionFlags.Below) == 0 ||
            preMoveVerticalSpeed >= 0f
        )
        {
            return;
        }

        Landed?.Invoke(-preMoveVerticalSpeed);
    }

    /// <summary>
    /// Re-evaluates effective aim after weapon presentation changes state.
    /// </summary>
    public void RefreshAimAvailability()
    {
        if (weaponPresentation == null)
        {
            weaponPresentation = GetComponent<PowerSuitWeaponPresentation>();
        }

        EvaluateWeaponAimState(advancePresentation: false);
    }

    private void EvaluateWeaponAimState(bool advancePresentation)
    {
        EnsureWeaponAimState();

        bool canUseWeapon =
            weaponPresentation == null || weaponPresentation.CanUseWeapon;
        bool isAlive = playerHealth == null || !playerHealth.IsDefeated;
        bool scopePressed =
            scopePressedThisFrame &&
            scopePressEvaluatedFrame != Time.frameCount;
        if (scopePressed)
        {
            scopePressEvaluatedFrame = Time.frameCount;
        }

        weaponAimState.Evaluate(
            new WeaponAimInput(
                aimHeld: aimRequested && canUseWeapon,
                scopeHeld: scopeHeld,
                scopePressed: scopePressed,
                isReloading: weapon != null && weapon.IsReloading,
                isAlive: isAlive
            )
        );
        if (advancePresentation)
        {
            weaponAimState.AdvancePresentation(Time.unscaledDeltaTime);
        }

        isAiming = weaponAimState.IsAiming;
    }

    private void EnsureWeaponAimState()
    {
        if (weapon == null)
        {
            weapon = GetComponent<PowerSuitWeapon>();
        }

        if (playerHealth == null)
        {
            playerHealth = GetComponent<PlayerHealth>();
        }

        WeaponDefinition currentDefinition = weapon != null
            ? weapon.Definition
            : null;
        bool useFallback = currentDefinition == null;
        if (
            weaponAimState != null &&
            weaponAimDefinition == currentDefinition &&
            weaponAimStateUsesFallback == useFallback
        )
        {
            return;
        }

        WeaponAimProfile profile = currentDefinition != null
            ? currentDefinition.CreateAimProfile()
            : new WeaponAimProfile(
                supportsScope: false,
                shoulderFieldOfViewDegrees: aimFieldOfView,
                scopedFieldOfViewDegrees: Mathf.Min(28f, aimFieldOfView - 1f),
                shoulderLookSensitivityMultiplier: 0.9f,
                scopedLookSensitivityMultiplier: 0.45f,
                transitionSharpness: Mathf.Max(0.01f, aimTransitionSpeed)
            );

        weaponAimState = new WeaponAimState(
            profile,
            scopeActivationPolicy
        );
        weaponAimDefinition = currentDefinition;
        weaponAimStateUsesFallback = useFallback;
    }

    /// <summary>
    /// Clears all transient locomotion, aim, camera, and recoil state before a
    /// respawn. Serialized tuning and cursor ownership are left unchanged.
    /// </summary>
    public void ResetForRespawn()
    {
        isFlying = false;
        isBoosting = false;
        isRunning = false;
        aimRequested = false;
        isAiming = false;
        scopeHeld = false;
        scopePressedThisFrame = false;
        scopePressEvaluatedFrame = -1;
        weaponAimState?.Reset();
        // A held button must be released after respawn before it can request a
        // new shot. If it is already up, the next Update clears this latch
        // before the weapon adapter samples input.
        suppressPrimaryFireUntilReleased = true;
        flightTakeoffGraceRemaining = 0f;
        EnsureJumpFlightState();
        jumpFlightState.Reset();

        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
        EnsureGroundContactState();
        groundContactState.Reset(grounded: false);
        localMovement = Vector2.zero;
        currentRecoilOffset = Vector2.zero;
        currentReticleOffset = Vector2.zero;

        cameraYaw = transform.eulerAngles.y;
        cameraPitch = 15f;
        smoothedCameraYaw = cameraYaw;
        smoothedCameraPitch = cameraPitch;
        currentCameraDistance = cameraDistance;
        currentCameraHeight = cameraHeight;
        currentShoulderOffset = Vector3.zero;
        currentFOV = defaultFieldOfView;
        currentCollisionDistance = cameraDistance;
        cameraOccluded = false;

        if (playerCamera != null)
        {
            playerCamera.fieldOfView = defaultFieldOfView;
            playerCamera.nearClipPlane = defaultNearClipPlane;
        }
    }

    private void OnValidate()
    {
        walkSpeed = Mathf.Max(0f, walkSpeed);
        groundAcceleration = Mathf.Max(0f, groundAcceleration);
        jumpHeight = Mathf.Max(0f, jumpHeight);
        gravity = Mathf.Min(-0.01f, gravity);

        flightSpeed = Mathf.Max(0f, flightSpeed);
        boostSpeed = Mathf.Max(flightSpeed, boostSpeed);
        flightAcceleration = Mathf.Max(0f, flightAcceleration);
        flightCameraDistance = Mathf.Max(0.1f, flightCameraDistance);
        flightCameraHeight = Mathf.Max(0f, flightCameraHeight);
        flightFieldOfView = Mathf.Clamp(flightFieldOfView, 1f, 179f);
        boostCameraDistance = Mathf.Max(
            flightCameraDistance,
            boostCameraDistance
        );
        boostCameraHeight = Mathf.Max(0f, boostCameraHeight);
        boostFieldOfView = Mathf.Clamp(
            Mathf.Max(flightFieldOfView, boostFieldOfView),
            1f,
            179f
        );
        groundSpeedMultiplier = ClampRuntimeSpeedMultiplier(
            groundSpeedMultiplier,
            1f
        );
        flightSpeedMultiplier = ClampRuntimeSpeedMultiplier(
            flightSpeedMultiplier,
            1f
        );
        scopeEyeRelief = Mathf.Max(0f, scopeEyeRelief);
        scopedNearClipPlane = Mathf.Max(0.001f, scopedNearClipPlane);
        GetMovementSettings().Sanitize();
    }

    private static float ClampRuntimeSpeedMultiplier(
        float value,
        float fallback
    )
    {
        if (float.IsNaN(value))
        {
            return fallback;
        }

        if (float.IsPositiveInfinity(value))
        {
            return MaximumSpeedMultiplier;
        }

        if (float.IsNegativeInfinity(value))
        {
            return MinimumSpeedMultiplier;
        }

        return Mathf.Clamp(
            value,
            MinimumSpeedMultiplier,
            MaximumSpeedMultiplier
        );
    }

    private Vector2 ReadMovementInput()
    {
        return ReadInputSnapshot().Move;
    }

    private Vector2 ReadMouseLook()
    {
        return ReadInputSnapshot().PointerLook;
    }

    private Vector2 ReadControllerLook()
    {
        return ReadInputSnapshot().GamepadLook;
    }

    private float ReadVerticalFlightInput()
    {
        return ReadInputSnapshot().Vertical;
    }

    private bool WasJumpPressed()
    {
        return ReadInputSnapshot().JumpPressed;
    }

    private bool IsJumpHeld()
    {
        return ReadInputSnapshot().JumpHeld;
    }

    private bool IsBoostHeld()
    {
        return ReadInputSnapshot().BoostHeld;
    }

    private bool IsAimHeld()
    {
        return ReadInputSnapshot().AimHeld;
    }

    private bool WasEscapePressed()
    {
        return ReadInputSnapshot().CancelPressed;
    }

    private bool WasPrimaryClickPressed()
    {
        return ReadInputSnapshot().FirePressed;
    }

    private bool IsPrimaryClickHeld()
    {
        return ReadInputSnapshot().FireHeld;
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
}

/// <summary>
/// Phase B response and timing data. The controller keeps its original
/// speed/jump fields for prefab and generator compatibility, while all added
/// feel tuning lives in this serializable value object with safe defaults.
/// </summary>
[System.Serializable]
public sealed class PowerSuitMovementSettings
{
    [Header("Ground Response")]
    [SerializeField, Min(1f)] private float groundRunSpeedMultiplier = 1.65f;
    [SerializeField] private float groundDeceleration = 65f;
    [SerializeField] private float groundBrakingAcceleration = 105f;
    [SerializeField] private float airAcceleration = 16f;
    [SerializeField] private float airDeceleration = 4f;
    [SerializeField] private float airBrakingAcceleration = 22f;
    [SerializeField] private float terminalFallSpeed = 35f;
    [SerializeField] private float groundedStickVelocity = -2f;

    [Header("Jump Forgiveness")]
    [SerializeField] private float groundedReleaseGraceSeconds = 0.06f;
    [SerializeField] private float coyoteTimeSeconds = 0.12f;
    [SerializeField] private float jumpBufferSeconds = 0.12f;
    [SerializeField, Min(0f)] private float jumpHoldFlightDelaySeconds = 0.9f;
    [SerializeField, Range(0.01f, 1f)] private float jumpHoldGravityScale = 0.55f;

    [Header("Flight Response")]
    [SerializeField] private float flightDeceleration = 30f;
    [SerializeField] private float flightBrakingAcceleration = 55f;
    [SerializeField] private float flightVerticalSpeed = 11f;
    [SerializeField] private float boostVerticalSpeed = 18f;
    [SerializeField] private float flightVerticalAcceleration = 36f;
    [SerializeField] private float flightVerticalDeceleration = 30f;
    [SerializeField] private float flightVerticalBrakingAcceleration = 55f;
    [SerializeField] private float flightTakeoffSpeed = 5f;
    [SerializeField] private float flightTakeoffGraceSeconds = 0.12f;
    [SerializeField] private float boostAccelerationMultiplier = 1.7f;

    public float GroundRunSpeedMultiplier => groundRunSpeedMultiplier;
    public float GroundDeceleration => groundDeceleration;
    public float GroundBrakingAcceleration => groundBrakingAcceleration;
    public float AirAcceleration => airAcceleration;
    public float AirDeceleration => airDeceleration;
    public float AirBrakingAcceleration => airBrakingAcceleration;
    public float TerminalFallSpeed => terminalFallSpeed;
    public float GroundedStickVelocity => groundedStickVelocity;
    public float GroundedReleaseGraceSeconds =>
        groundedReleaseGraceSeconds;
    public float CoyoteTimeSeconds => coyoteTimeSeconds;
    public float JumpBufferSeconds => jumpBufferSeconds;
    public float JumpHoldFlightDelaySeconds => jumpHoldFlightDelaySeconds;
    public float JumpHoldGravityScale => jumpHoldGravityScale;
    public float FlightDeceleration => flightDeceleration;
    public float FlightBrakingAcceleration => flightBrakingAcceleration;
    public float FlightVerticalSpeed => flightVerticalSpeed;
    public float BoostVerticalSpeed => boostVerticalSpeed;
    public float FlightVerticalAcceleration => flightVerticalAcceleration;
    public float FlightVerticalDeceleration => flightVerticalDeceleration;
    public float FlightVerticalBrakingAcceleration =>
        flightVerticalBrakingAcceleration;
    public float FlightTakeoffSpeed => flightTakeoffSpeed;
    public float FlightTakeoffGraceSeconds => flightTakeoffGraceSeconds;
    public float BoostAccelerationMultiplier => boostAccelerationMultiplier;

    public void Sanitize()
    {
        groundRunSpeedMultiplier = Mathf.Max(1f, groundRunSpeedMultiplier);
        groundDeceleration = Mathf.Max(0f, groundDeceleration);
        groundBrakingAcceleration = Mathf.Max(
            0f,
            groundBrakingAcceleration
        );
        airAcceleration = Mathf.Max(0f, airAcceleration);
        airDeceleration = Mathf.Max(0f, airDeceleration);
        airBrakingAcceleration = Mathf.Max(
            0f,
            airBrakingAcceleration
        );
        terminalFallSpeed = Mathf.Max(0.01f, terminalFallSpeed);
        groundedStickVelocity = Mathf.Min(0f, groundedStickVelocity);
        groundedReleaseGraceSeconds = Mathf.Max(
            0f,
            groundedReleaseGraceSeconds
        );
        coyoteTimeSeconds = Mathf.Max(0f, coyoteTimeSeconds);
        jumpBufferSeconds = Mathf.Max(0f, jumpBufferSeconds);
        jumpHoldFlightDelaySeconds = Mathf.Max(
            0f,
            jumpHoldFlightDelaySeconds
        );
        jumpHoldGravityScale = Mathf.Clamp(
            jumpHoldGravityScale,
            0.01f,
            1f
        );

        flightDeceleration = Mathf.Max(0f, flightDeceleration);
        flightBrakingAcceleration = Mathf.Max(
            0f,
            flightBrakingAcceleration
        );
        flightVerticalSpeed = Mathf.Max(0f, flightVerticalSpeed);
        boostVerticalSpeed = Mathf.Max(
            flightVerticalSpeed,
            boostVerticalSpeed
        );
        flightVerticalAcceleration = Mathf.Max(
            0f,
            flightVerticalAcceleration
        );
        flightVerticalDeceleration = Mathf.Max(
            0f,
            flightVerticalDeceleration
        );
        flightVerticalBrakingAcceleration = Mathf.Max(
            0f,
            flightVerticalBrakingAcceleration
        );
        flightTakeoffSpeed = Mathf.Max(0f, flightTakeoffSpeed);
        flightTakeoffGraceSeconds = Mathf.Max(
            0f,
            flightTakeoffGraceSeconds
        );
        boostAccelerationMultiplier = Mathf.Max(
            1f,
            boostAccelerationMultiplier
        );
    }
}

/// <summary>
/// Frame-rate-independent camera interpolation kept outside the component so
/// convergence and collision recovery can be verified without a live scene.
/// </summary>
public static class PowerSuitCameraMath
{
    public static float CalculateFloorSafeMinimumPitch(
        float cameraDistance,
        float pivotHeight,
        float minimumClearance,
        float configuredMinimumPitch
    )
    {
        if (
            cameraDistance <= 0f ||
            float.IsNaN(cameraDistance) ||
            float.IsInfinity(cameraDistance) ||
            float.IsNaN(pivotHeight) ||
            float.IsInfinity(pivotHeight) ||
            float.IsNaN(minimumClearance) ||
            float.IsInfinity(minimumClearance)
        )
        {
            return configuredMinimumPitch;
        }

        float requiredSine = Mathf.Clamp(
            (Mathf.Max(0f, minimumClearance) - pivotHeight) /
                cameraDistance,
            -1f,
            1f
        );
        float floorSafePitch = Mathf.Asin(requiredSine) * Mathf.Rad2Deg;
        return Mathf.Max(configuredMinimumPitch, floorSafePitch);
    }

    public static float ExponentialDampingFactor(
        float sharpness,
        float deltaTime
    )
    {
        if (float.IsNaN(deltaTime) || deltaTime <= 0f)
        {
            return 0f;
        }

        if (float.IsNaN(sharpness) || sharpness <= 0f)
        {
            return 1f;
        }

        if (float.IsInfinity(deltaTime) || float.IsInfinity(sharpness))
        {
            return 1f;
        }

        return 1f - Mathf.Exp(-sharpness * deltaTime);
    }

    public static float Damp(
        float current,
        float target,
        float sharpness,
        float deltaTime
    )
    {
        return Mathf.Lerp(
            current,
            target,
            ExponentialDampingFactor(sharpness, deltaTime)
        );
    }

    public static float ResolveCollisionDistance(
        float currentDistance,
        float unobstructedDistance,
        float allowedDistance,
        float releaseSharpness,
        float deltaTime
    )
    {
        float targetDistance = Mathf.Clamp(
            allowedDistance,
            0f,
            Mathf.Max(0f, unobstructedDistance)
        );

        if (targetDistance <= currentDistance)
        {
            return targetDistance;
        }

        return Mathf.Min(
            unobstructedDistance,
            Damp(
                currentDistance,
                targetDistance,
                releaseSharpness,
                deltaTime
            )
        );
    }

    public static float ResolveCameraDistance(
        float currentDistance,
        float unobstructedDistance,
        float allowedDistance,
        bool wasOccluded,
        float releaseSharpness,
        float deltaTime,
        out bool isOccluded
    )
    {
        const float ObstructionEpsilon = 0.001f;
        const float RecoverySnapEpsilon = 0.01f;

        bool hasObstruction =
            allowedDistance < unobstructedDistance - ObstructionEpsilon;
        isOccluded = hasObstruction || wasOccluded;

        // The normal/aim profile already has its own damping. With no wall
        // recovery in progress, follow that profile exactly rather than
        // filtering the outward transition a second time.
        if (!isOccluded)
        {
            return Mathf.Max(0f, unobstructedDistance);
        }

        float resolvedDistance = ResolveCollisionDistance(
            currentDistance,
            unobstructedDistance,
            allowedDistance,
            releaseSharpness,
            deltaTime
        );

        if (
            !hasObstruction &&
            Mathf.Abs(resolvedDistance - unobstructedDistance) <=
                RecoverySnapEpsilon
        )
        {
            isOccluded = false;
            return Mathf.Max(0f, unobstructedDistance);
        }

        return resolvedDistance;
    }
}

/// <summary>
/// Stateless locomotion decisions kept outside the MonoBehaviour so facing and
/// animation inputs can be verified without running a scene.
/// </summary>
public static class PowerSuitLocomotionMath
{
    private const float DirectionEpsilon = 0.0001f;
    private const float BackwardInputThreshold = -0.01f;

    /// <summary>
    /// Removes flight-only lift or descent while preserving planar momentum.
    /// </summary>
    public static Vector3 ProjectOntoGroundPlane(Vector3 velocity)
    {
        return Vector3.ProjectOnPlane(velocity, Vector3.up);
    }

    /// <summary>
    /// Selects the configured walk or run speed without coupling input state to
    /// the movement adapter.
    /// </summary>
    public static float CalculateGroundTargetSpeed(
        float walkSpeed,
        bool isRunning,
        float runSpeedMultiplier
    )
    {
        if (
            float.IsNaN(walkSpeed) ||
            float.IsInfinity(walkSpeed) ||
            walkSpeed < 0f ||
            float.IsNaN(runSpeedMultiplier) ||
            float.IsInfinity(runSpeedMultiplier) ||
            runSpeedMultiplier < 1f
        )
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(runSpeedMultiplier),
                "Ground speeds must be finite/non-negative and the run multiplier must be at least one."
            );
        }

        return walkSpeed * (isRunning ? runSpeedMultiplier : 1f);
    }

    /// <summary>
    /// Sprint is a deliberate supported forward/lateral action. Backpedal keeps
    /// its dedicated reverse pose, and any aim request wins immediately.
    /// </summary>
    public static bool ShouldRun(
        bool hasStableSupport,
        bool runHeld,
        bool aimRequested,
        bool isAiming,
        Vector2 movementInput
    )
    {
        return
            hasStableSupport &&
            runHeld &&
            !aimRequested &&
            !isAiming &&
            movementInput.y >= -0.01f &&
            movementInput.sqrMagnitude > 0.0001f;
    }

    public static bool ShouldCompleteFlightLanding(
        CollisionFlags collisionFlags,
        float takeoffGraceRemaining,
        float preMoveVerticalSpeed
    )
    {
        return
            (collisionFlags & CollisionFlags.Below) != 0 &&
            takeoffGraceRemaining <= 0f &&
            preMoveVerticalSpeed <= 0f;
    }

    /// <summary>
    /// Approaches a target using distinct acceleration, coasting deceleration,
    /// and direction-reversal braking rates. All rates are units/second².
    /// </summary>
    public static Vector3 ApproachVelocity(
        Vector3 current,
        Vector3 target,
        float acceleration,
        float deceleration,
        float brakingAcceleration,
        float deltaTime
    )
    {
        ValidateRate(acceleration, nameof(acceleration));
        ValidateRate(deceleration, nameof(deceleration));
        ValidateRate(brakingAcceleration, nameof(brakingAcceleration));
        ValidateDeltaTime(deltaTime);

        float selectedRate;
        if (target.sqrMagnitude <= DirectionEpsilon)
        {
            selectedRate = deceleration;
        }
        else if (
            current.sqrMagnitude > DirectionEpsilon &&
            Vector3.Dot(current, target) < 0f
        )
        {
            return ReverseVelocity(
                current,
                target,
                acceleration,
                brakingAcceleration,
                deltaTime
            );
        }
        else if (
            target.sqrMagnitude + DirectionEpsilon < current.sqrMagnitude
        )
        {
            selectedRate = deceleration;
        }
        else
        {
            selectedRate = acceleration;
        }

        return Vector3.MoveTowards(
            current,
            target,
            selectedRate * deltaTime
        );
    }

    public static float ApproachVelocity(
        float current,
        float target,
        float acceleration,
        float deceleration,
        float brakingAcceleration,
        float deltaTime
    )
    {
        ValidateRate(acceleration, nameof(acceleration));
        ValidateRate(deceleration, nameof(deceleration));
        ValidateRate(brakingAcceleration, nameof(brakingAcceleration));
        ValidateDeltaTime(deltaTime);

        float selectedRate;
        if (Mathf.Abs(target) <= DirectionEpsilon)
        {
            selectedRate = deceleration;
        }
        else if (
            Mathf.Abs(current) > DirectionEpsilon &&
            current * target < 0f
        )
        {
            return ReverseVelocity(
                current,
                target,
                acceleration,
                brakingAcceleration,
                deltaTime
            );
        }
        else if (Mathf.Abs(target) + DirectionEpsilon < Mathf.Abs(current))
        {
            selectedRate = deceleration;
        }
        else
        {
            selectedRate = acceleration;
        }

        return Mathf.MoveTowards(
            current,
            target,
            selectedRate * deltaTime
        );
    }

    private static Vector3 ReverseVelocity(
        Vector3 current,
        Vector3 target,
        float acceleration,
        float brakingAcceleration,
        float deltaTime
    )
    {
        if (brakingAcceleration <= 0f)
        {
            return current;
        }

        float stopTime = current.magnitude / brakingAcceleration;
        if (deltaTime <= stopTime)
        {
            return Vector3.MoveTowards(
                current,
                Vector3.zero,
                brakingAcceleration * deltaTime
            );
        }

        return Vector3.MoveTowards(
            Vector3.zero,
            target,
            acceleration * (deltaTime - stopTime)
        );
    }

    private static float ReverseVelocity(
        float current,
        float target,
        float acceleration,
        float brakingAcceleration,
        float deltaTime
    )
    {
        if (brakingAcceleration <= 0f)
        {
            return current;
        }

        float stopTime = Mathf.Abs(current) / brakingAcceleration;
        if (deltaTime <= stopTime)
        {
            return Mathf.MoveTowards(
                current,
                0f,
                brakingAcceleration * deltaTime
            );
        }

        return Mathf.MoveTowards(
            0f,
            target,
            acceleration * (deltaTime - stopTime)
        );
    }

    public static float CalculateJumpSpeed(float jumpHeight, float gravity)
    {
        if (
            float.IsNaN(jumpHeight) ||
            float.IsInfinity(jumpHeight) ||
            jumpHeight < 0f ||
            float.IsNaN(gravity) ||
            float.IsInfinity(gravity) ||
            gravity >= 0f
        )
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(jumpHeight),
                "Jump height must be finite/non-negative and gravity must be finite/negative."
            );
        }

        return Mathf.Sqrt(jumpHeight * -2f * gravity);
    }

    public static float ApplyGravity(
        float currentVerticalVelocity,
        float gravity,
        float terminalFallSpeed,
        float deltaTime
    )
    {
        if (
            float.IsNaN(currentVerticalVelocity) ||
            float.IsInfinity(currentVerticalVelocity) ||
            float.IsNaN(gravity) ||
            float.IsInfinity(gravity) ||
            gravity >= 0f ||
            float.IsNaN(terminalFallSpeed) ||
            float.IsInfinity(terminalFallSpeed) ||
            terminalFallSpeed <= 0f
        )
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(terminalFallSpeed),
                "Gravity inputs must be finite, with negative gravity and a positive terminal speed."
            );
        }

        ValidateDeltaTime(deltaTime);
        return Mathf.Max(
            -terminalFallSpeed,
            currentVerticalVelocity + gravity * deltaTime
        );
    }

    /// <summary>
    /// Selects the heading the suit should turn toward for the current input.
    /// Backward ground movement faces opposite its travel vector so velocity is
    /// reliably local-backward; aim mode always follows the camera heading.
    /// </summary>
    public static Vector3 ResolveFacingDirection(
        Vector2 movementInput,
        Vector3 desiredMovementDirection,
        Vector3 currentForward,
        Vector3 cameraForward,
        bool isAiming
    )
    {
        if (isAiming)
        {
            return NormalizePlanarOrFallback(cameraForward, currentForward);
        }

        if (movementInput.y < BackwardInputThreshold)
        {
            return NormalizePlanarOrFallback(
                -desiredMovementDirection,
                currentForward
            );
        }

        return NormalizePlanarOrFallback(desiredMovementDirection, Vector3.zero);
    }

    /// <summary>
    /// Converts world velocity to a signed, normalized local-space blend value.
    /// Vertical motion is ignored so jump/fall speed cannot select a walk clip.
    /// </summary>
    public static Vector2 ToLocalMovement(
        Quaternion characterRotation,
        Vector3 worldVelocity,
        float referenceSpeed
    )
    {
        if (referenceSpeed <= DirectionEpsilon)
        {
            return Vector2.zero;
        }

        Vector3 planarVelocity = Vector3.ProjectOnPlane(
            worldVelocity,
            Vector3.up
        );

        Vector3 localVelocity = Quaternion.Inverse(characterRotation) * planarVelocity;
        Vector2 localMovement = new Vector2(
            localVelocity.x,
            localVelocity.z
        ) / referenceSpeed;

        return Vector2.ClampMagnitude(localMovement, 1f);
    }

    /// <summary>
    /// Keeps the idle cycle at normal speed and increases gait cadence as
    /// actual movement approaches its configured maximum. The Generator 111
    /// walk has about 1.08 metres/second of planted-foot travel at 1x, so the
    /// demo's 2.2 m/s ground speed is matched closely at the default 2x value.
    /// </summary>
    public static float CalculateLocomotionPlaybackSpeed(
        float normalizedMovementSpeed,
        float fullSpeedMultiplier
    )
    {
        if (
            float.IsNaN(normalizedMovementSpeed) ||
            float.IsInfinity(normalizedMovementSpeed) ||
            float.IsNaN(fullSpeedMultiplier) ||
            float.IsInfinity(fullSpeedMultiplier) ||
            fullSpeedMultiplier < 1f
        )
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(fullSpeedMultiplier),
                "Locomotion playback inputs must be finite and the multiplier must be at least one."
            );
        }

        return Mathf.Lerp(
            1f,
            fullSpeedMultiplier,
            Mathf.Clamp01(normalizedMovementSpeed)
        );
    }

    private static Vector3 NormalizePlanarOrFallback(
        Vector3 direction,
        Vector3 fallback
    )
    {
        Vector3 planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);

        if (planarDirection.sqrMagnitude > DirectionEpsilon)
        {
            return planarDirection.normalized;
        }

        Vector3 planarFallback = Vector3.ProjectOnPlane(fallback, Vector3.up);
        return planarFallback.sqrMagnitude > DirectionEpsilon
            ? planarFallback.normalized
            : Vector3.zero;
    }

    private static void ValidateRate(float value, string parameterName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
        {
            throw new System.ArgumentOutOfRangeException(
                parameterName,
                "Movement rates must be finite and non-negative."
            );
        }
    }

    private static void ValidateDeltaTime(float deltaTime)
    {
        if (
            float.IsNaN(deltaTime) ||
            float.IsInfinity(deltaTime) ||
            deltaTime < 0f
        )
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(deltaTime),
                "Elapsed time must be finite and non-negative."
            );
        }
    }
}

/// <summary>
/// Engine-independent timing state for support hysteresis, coyote time, and
/// buffered jumps. Raw support owns jump eligibility; hysteresis only
/// stabilizes the public grounded signal, so the two grace periods never add.
/// </summary>
public sealed class PowerSuitGroundContactState
{
    private readonly float groundedReleaseGraceSeconds;
    private readonly float coyoteTimeSeconds;
    private readonly float jumpBufferSeconds;

    private bool hasRawSupport;
    private bool isGrounded;
    private bool waitingForSeparation;
    private bool hasBufferedJump;
    private float groundedReleaseRemaining;
    private float coyoteRemaining;
    private float jumpBufferRemaining;

    public PowerSuitGroundContactState(
        float groundedReleaseGraceSeconds,
        float coyoteTimeSeconds,
        float jumpBufferSeconds
    )
    {
        ValidateDuration(
            groundedReleaseGraceSeconds,
            nameof(groundedReleaseGraceSeconds)
        );
        ValidateDuration(coyoteTimeSeconds, nameof(coyoteTimeSeconds));
        ValidateDuration(jumpBufferSeconds, nameof(jumpBufferSeconds));

        this.groundedReleaseGraceSeconds = groundedReleaseGraceSeconds;
        this.coyoteTimeSeconds = coyoteTimeSeconds;
        this.jumpBufferSeconds = jumpBufferSeconds;
    }

    public bool HasRawSupport => hasRawSupport;
    public bool IsGrounded => isGrounded;
    public bool HasBufferedJump => hasBufferedJump;
    public bool WaitingForSeparation => waitingForSeparation;
    public float CoyoteRemaining => coyoteRemaining;
    public float JumpBufferRemaining => jumpBufferRemaining;

    public void Reset(bool grounded)
    {
        hasRawSupport = grounded;
        isGrounded = grounded;
        waitingForSeparation = false;
        hasBufferedJump = false;
        groundedReleaseRemaining = grounded
            ? groundedReleaseGraceSeconds
            : 0f;
        coyoteRemaining = grounded ? coyoteTimeSeconds : 0f;
        jumpBufferRemaining = 0f;
    }

    public void Advance(bool rawGrounded, float deltaTime)
    {
        ValidateDuration(deltaTime, nameof(deltaTime));
        AdvanceJumpBuffer(deltaTime);

        if (waitingForSeparation)
        {
            hasRawSupport = false;
            isGrounded = false;
            groundedReleaseRemaining = 0f;
            coyoteRemaining = 0f;
            if (!rawGrounded)
            {
                waitingForSeparation = false;
            }
            return;
        }

        hasRawSupport = rawGrounded;
        if (rawGrounded)
        {
            isGrounded = true;
            groundedReleaseRemaining = groundedReleaseGraceSeconds;
            coyoteRemaining = coyoteTimeSeconds;
            return;
        }

        groundedReleaseRemaining = System.Math.Max(
            0f,
            groundedReleaseRemaining - deltaTime
        );
        coyoteRemaining = System.Math.Max(
            0f,
            coyoteRemaining - deltaTime
        );
        isGrounded = groundedReleaseRemaining > 0f;
    }

    public void BufferJump()
    {
        hasBufferedJump = true;
        jumpBufferRemaining = jumpBufferSeconds;
    }

    public bool TryConsumeBufferedJump()
    {
        bool hasJumpSurface =
            !waitingForSeparation &&
            (hasRawSupport || coyoteRemaining > 0f);
        if (!hasBufferedJump || !hasJumpSurface)
        {
            return false;
        }

        hasBufferedJump = false;
        jumpBufferRemaining = 0f;
        hasRawSupport = false;
        isGrounded = false;
        groundedReleaseRemaining = 0f;
        coyoteRemaining = 0f;
        waitingForSeparation = true;
        return true;
    }

    public void ForceAirborneUntilSeparated()
    {
        hasRawSupport = false;
        isGrounded = false;
        waitingForSeparation = true;
        hasBufferedJump = false;
        groundedReleaseRemaining = 0f;
        coyoteRemaining = 0f;
        jumpBufferRemaining = 0f;
    }

    private void AdvanceJumpBuffer(float deltaTime)
    {
        if (!hasBufferedJump)
        {
            return;
        }

        if (jumpBufferRemaining <= 0f)
        {
            hasBufferedJump = false;
            return;
        }

        jumpBufferRemaining = System.Math.Max(
            0f,
            jumpBufferRemaining - deltaTime
        );
        if (jumpBufferRemaining <= 0f)
        {
            hasBufferedJump = false;
        }
    }

    private static void ValidateDuration(float value, string parameterName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
        {
            throw new System.ArgumentOutOfRangeException(
                parameterName,
                "Movement timing values must be finite and non-negative."
            );
        }
    }
}

/// <summary>
/// Plain timing state which distinguishes a quick jump tap from a deliberate
/// hold-to-fly gesture. Only a successfully consumed ground/coyote jump can arm
/// flight, so holding Jump while falling cannot unexpectedly enable it.
/// </summary>
public sealed class PowerSuitJumpFlightState
{
    private readonly float holdDelaySeconds;
    private bool isArmed;
    private bool hasSeparatedFromGround;
    private float heldSeconds;

    public PowerSuitJumpFlightState(float holdDelaySeconds)
    {
        ValidateDuration(holdDelaySeconds, nameof(holdDelaySeconds));
        this.holdDelaySeconds = holdDelaySeconds;
    }

    public bool IsArmed => isArmed;
    public bool HasSeparatedFromGround => hasSeparatedFromGround;
    public float HeldSeconds => heldSeconds;

    public void Arm(bool jumpHeld)
    {
        Reset();
        isArmed = jumpHeld;
    }

    /// <summary>
    /// Returns true exactly once when a continuously held, accepted jump has
    /// separated from its launch surface and reached the configured delay.
    /// </summary>
    public bool Advance(bool jumpHeld, bool isAirborne, float deltaTime)
    {
        ValidateDuration(deltaTime, nameof(deltaTime));
        if (!isArmed)
        {
            return false;
        }

        if (!jumpHeld)
        {
            Reset();
            return false;
        }

        heldSeconds += deltaTime;
        if (isAirborne)
        {
            hasSeparatedFromGround = true;
        }

        if (hasSeparatedFromGround && !isAirborne)
        {
            Reset();
            return false;
        }

        if (!hasSeparatedFromGround || heldSeconds < holdDelaySeconds)
        {
            return false;
        }

        Reset();
        return true;
    }

    public void Reset()
    {
        isArmed = false;
        hasSeparatedFromGround = false;
        heldSeconds = 0f;
    }

    private static void ValidateDuration(float value, string parameterName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
        {
            throw new System.ArgumentOutOfRangeException(
                parameterName,
                "Jump/flight timing values must be finite and non-negative."
            );
        }
    }
}
