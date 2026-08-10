using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(-200)]
public sealed class PowerSuitController : MonoBehaviour
{
    private const float MovementStateThreshold = 0.01f;
    private const int CameraCollisionHitCapacity = 32;
    private const int AimHitCapacity = 32;

    public bool IsFlying => isFlying;

    public bool IsMoving =>
        controller != null &&
        controller.velocity.sqrMagnitude > 0.05f;

    public bool IsAiming => isAiming;

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
    [SerializeField] private float walkSpeed = 2.2f;
    [SerializeField] private float groundAcceleration = 20f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -25f;

    [Header("Flight")]
    [SerializeField] private float flightSpeed = 10f;
    [SerializeField] private float boostSpeed = 20f;
    [SerializeField] private float flightAcceleration = 12f;
    [SerializeField] private float turningSpeed = 12f;

    [Header("Camera")]
    [SerializeField] private float cameraDistance = 9.5f;
    [SerializeField] private float cameraHeight = 1.5f;
    [SerializeField] private float mouseSensitivity = 0.15f;
    [SerializeField] private float controllerLookSpeed = 120f;
    [SerializeField] private float minimumPitch = -55f;
    [SerializeField] private float maximumPitch = 70f;
    [SerializeField] private float cameraCollisionRadius = 0.2f;
    [SerializeField] private float cameraCollisionPadding = 0.05f;
    [SerializeField] private float cameraCollisionReleaseSharpness = 14f;
    [SerializeField] private float cameraLookSharpness = 28f;

    [Header("Flight Camera")]
    [SerializeField] private float flightCameraDistance = 11f;
    [SerializeField] private float flightCameraHeight = 1.75f;
    [SerializeField] private float flightFieldOfView = 74f;

    [Header("Third-Person Aim Mode")]
    [SerializeField] private float aimCameraDistance = 4.3f;
    [SerializeField] private float aimCameraHeight = 1.45f;
    [SerializeField] private Vector3 aimShoulderOffset = new Vector3(-1.2f, 0.05f, 0f);
    [SerializeField] private float defaultFieldOfView = 72f;
    [SerializeField] private float aimFieldOfView = 62f;
    [SerializeField] private float aimTransitionSpeed = 12f;
    [SerializeField] private float maxReticleOffset = 140f;
    [SerializeField] private float aimMaxDistance = 200f;

    private CharacterController controller;
    private Camera playerCamera;
    private PowerSuitWeaponAnimationDriver weaponAnimationDriver;

    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private Vector2 localMovement;

    private float cameraYaw;
    private float cameraPitch = 15f;
    private float smoothedCameraYaw;
    private float smoothedCameraPitch;

    private bool isFlying;
    private bool isAiming;
    private bool cursorLocked;

    private float currentCameraDistance;
    private float currentCameraHeight;
    private Vector3 currentShoulderOffset;
    private float currentFOV;
    private float currentCollisionDistance;
    private bool cameraOccluded;
    private Vector2 currentReticleOffset;

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
        currentRecoilOffset.x += Random.Range(-yawKick, yawKick);
        currentRecoilOffset = Vector2.ClampMagnitude(currentRecoilOffset, maxAccumulatedRecoil);
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

        SetCursorLocked(true);
    }

    private void Update()
    {
        HandleCursor();

        if (!cursorLocked)
        {
            return;
        }

        HandleAimingState();
        HandleCameraInput();

        if (WasFlightTogglePressed())
        {
            isFlying = !isFlying;
            verticalVelocity = 0f;
        }

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

        Vector3 desiredVelocity =
            desiredDirection * walkSpeed;

        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity,
            desiredVelocity,
            groundAcceleration * Time.deltaTime
        );

        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }

            if (WasJumpPressed())
            {
                verticalVelocity = Mathf.Sqrt(
                    jumpHeight * -2f * gravity
                );
            }
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 movement =
            horizontalVelocity +
            Vector3.up * verticalVelocity;

        controller.Move(movement * Time.deltaTime);

        Vector3 facingDirection = PowerSuitLocomotionMath.ResolveFacingDirection(
            input,
            desiredDirection,
            transform.forward,
            cameraForward,
            ShouldFaceCameraForCombat()
        );

        RotateTowardsMovement(facingDirection);
        UpdateLocalMovement(walkSpeed);
    }

    private void HandleFlight()
    {
        Vector2 input = ReadMovementInput();

        Vector3 desiredDirection =
            playerCamera.transform.forward * input.y +
            playerCamera.transform.right * input.x;

        float verticalInput = ReadVerticalFlightInput();
        desiredDirection += Vector3.up * verticalInput;

        desiredDirection = Vector3.ClampMagnitude(
            desiredDirection,
            1f
        );

        float selectedSpeed =
            IsBoostHeld() ? boostSpeed : flightSpeed;

        Vector3 desiredVelocity =
            desiredDirection * selectedSpeed;

        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity,
            desiredVelocity,
            flightAcceleration * Time.deltaTime
        );

        verticalVelocity = 0f;

        controller.Move(
            horizontalVelocity * Time.deltaTime
        );

        Vector3 planarDirection = Vector3.ProjectOnPlane(
            desiredDirection,
            Vector3.up
        );

        if (ShouldFaceCameraForCombat())
        {
            Vector3 cameraPlanar = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up);
            RotateTowardsDirection(cameraPlanar);
        }
        else
        {
            RotateTowardsMovement(planarDirection);
        }

        UpdateLocalMovement(selectedSpeed);

        if (
            controller.isGrounded &&
            verticalInput < -0.1f
        )
        {
            isFlying = false;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = -2f;
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

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            PowerSuitCameraMath.ExponentialDampingFactor(
                turningSpeed,
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

        cameraYaw += mouseLook.x * mouseSensitivity;
        cameraPitch -= mouseLook.y * mouseSensitivity;

        cameraYaw += controllerLook.x *
                     controllerLookSpeed *
                     Time.deltaTime;

        cameraPitch -= controllerLook.y *
                       controllerLookSpeed *
                       Time.deltaTime;

        cameraPitch = Mathf.Clamp(
            cameraPitch,
            minimumPitch,
            maximumPitch
        );

        if (isAiming)
        {
            currentReticleOffset += mouseLook * (mouseSensitivity * 15f);
            currentReticleOffset = Vector2.ClampMagnitude(currentReticleOffset, maxReticleOffset);
        }
    }

    private void HandleAimingState()
    {
        isAiming = cursorLocked && IsAimHeld();

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
        float explorationDistance = isFlying
            ? flightCameraDistance
            : cameraDistance;
        float explorationHeight = isFlying
            ? flightCameraHeight
            : cameraHeight;
        float explorationFov = isFlying
            ? flightFieldOfView
            : defaultFieldOfView;

        float targetDistance = isAiming
            ? aimCameraDistance
            : explorationDistance;
        float targetHeight = isAiming
            ? aimCameraHeight
            : explorationHeight;
        Vector3 targetShoulder = isAiming ? aimShoulderOffset : Vector3.zero;
        float targetFOV = isAiming ? aimFieldOfView : explorationFov;
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
            SetCursorLocked(true);
        }
    }

    private void SetCursorLocked(bool locked)
    {
        cursorLocked = locked;

        if (!locked)
        {
            isAiming = false;
        }

        Cursor.lockState = locked
            ? CursorLockMode.Locked
            : CursorLockMode.None;

        Cursor.visible = !locked;
    }

    private Vector2 ReadMovementInput()
    {
        Vector2 input = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed)
            {
                input.y += 1f;
            }

            if (Keyboard.current.sKey.isPressed)
            {
                input.y -= 1f;
            }

            if (Keyboard.current.dKey.isPressed)
            {
                input.x += 1f;
            }

            if (Keyboard.current.aKey.isPressed)
            {
                input.x -= 1f;
            }
        }

        if (Gamepad.current != null)
        {
            input += Gamepad.current.leftStick.ReadValue();
        }
#else
        input = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        );
#endif

        return Vector2.ClampMagnitude(input, 1f);
    }

    private Vector2 ReadMouseLook()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null
            ? Mouse.current.delta.ReadValue()
            : Vector2.zero;
#else
        return new Vector2(
            Input.GetAxis("Mouse X"),
            Input.GetAxis("Mouse Y")
        );
#endif
    }

    private Vector2 ReadControllerLook()
    {
#if ENABLE_INPUT_SYSTEM
        return Gamepad.current != null
            ? Gamepad.current.rightStick.ReadValue()
            : Vector2.zero;
#else
        return Vector2.zero;
#endif
    }

    private float ReadVerticalFlightInput()
    {
        float input = 0f;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.isPressed)
            {
                input += 1f;
            }

            if (
                Keyboard.current.leftCtrlKey.isPressed ||
                Keyboard.current.cKey.isPressed
            )
            {
                input -= 1f;
            }
        }

        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.isPressed)
            {
                input += 1f;
            }

            if (Gamepad.current.leftShoulder.isPressed)
            {
                input -= 1f;
            }
        }
#else
        if (Input.GetKey(KeyCode.Space))
        {
            input += 1f;
        }

        if (
            Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.C)
        )
        {
            input -= 1f;
        }
#endif

        return Mathf.Clamp(input, -1f, 1f);
    }

    private bool WasJumpPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return
            (
                Keyboard.current != null &&
                Keyboard.current.spaceKey.wasPressedThisFrame
            ) ||
            (
                Gamepad.current != null &&
                Gamepad.current.buttonSouth.wasPressedThisFrame
            );
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    private bool WasFlightTogglePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return
            (
                Keyboard.current != null &&
                Keyboard.current.fKey.wasPressedThisFrame
            ) ||
            (
                Gamepad.current != null &&
                Gamepad.current.buttonWest.wasPressedThisFrame
            );
#else
        return Input.GetKeyDown(KeyCode.F);
#endif
    }

    private bool IsBoostHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return
            (
                Keyboard.current != null &&
                Keyboard.current.leftShiftKey.isPressed
            ) ||
            (
                Gamepad.current != null &&
                Gamepad.current.rightShoulder.isPressed
            );
#else
        return Input.GetKey(KeyCode.LeftShift);
#endif
    }

    private bool IsAimHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return
            (
                Mouse.current != null &&
                Mouse.current.rightButton.isPressed
            ) ||
            (
                Gamepad.current != null &&
                Gamepad.current.leftTrigger.isPressed
            );
#else
        return Input.GetMouseButton(1);
#endif
    }

    private bool WasEscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return
            Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private bool WasPrimaryClickPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return
            Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
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
}
