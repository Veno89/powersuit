using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
public sealed class PowerSuitController : MonoBehaviour
{
    public bool IsFlying => isFlying;

    public bool IsMoving =>
        controller != null &&
        controller.velocity.sqrMagnitude > 0.05f;

    public bool IsAiming => isAiming;

    public Vector2 ReticleScreenPosition =>
        new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) + currentReticleOffset;

    public Vector2 ReticleOffset => currentReticleOffset;

    [Header("Ground Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float groundAcceleration = 20f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -25f;

    [Header("Flight")]
    [SerializeField] private float flightSpeed = 10f;
    [SerializeField] private float boostSpeed = 20f;
    [SerializeField] private float flightAcceleration = 12f;
    [SerializeField] private float turningSpeed = 12f;

    [Header("Camera")]
    [SerializeField] private float cameraDistance = 5f;
    [SerializeField] private float cameraHeight = 1.4f;
    [SerializeField] private float mouseSensitivity = 0.15f;
    [SerializeField] private float controllerLookSpeed = 120f;
    [SerializeField] private float minimumPitch = -55f;
    [SerializeField] private float maximumPitch = 70f;
    [SerializeField] private float cameraCollisionRadius = 0.2f;

    [Header("Third-Person Aim Mode")]
    [SerializeField] private float aimCameraDistance = 2.2f;
    [SerializeField] private float aimCameraHeight = 1.5f;
    [SerializeField] private Vector3 aimShoulderOffset = new Vector3(1.2f, 0.4f, 0f);
    [SerializeField] private float defaultFieldOfView = 60f;
    [SerializeField] private float aimFieldOfView = 45f;
    [SerializeField] private float aimTransitionSpeed = 12f;
    [SerializeField] private float maxReticleOffset = 140f;
    [SerializeField] private float aimMaxDistance = 200f;

    private CharacterController controller;
    private Camera playerCamera;

    private Vector3 horizontalVelocity;
    private float verticalVelocity;

    private float cameraYaw;
    private float cameraPitch = 15f;

    private bool isFlying;
    private bool isAiming;
    private bool cursorLocked;

    private float currentCameraDistance;
    private float currentCameraHeight;
    private Vector3 currentShoulderOffset;
    private float currentFOV;
    private Vector2 currentReticleOffset;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        playerCamera = Camera.main;

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
        currentCameraDistance = cameraDistance;
        currentCameraHeight = cameraHeight;
        currentShoulderOffset = Vector3.zero;
        currentFOV = defaultFieldOfView;
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

        HandleCameraInput();
        HandleAimingState();

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

        if (isAiming)
        {
            RotateTowardsDirection(cameraForward);
        }
        else
        {
            RotateTowardsMovement(desiredDirection);
        }
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

        if (isAiming)
        {
            Vector3 cameraPlanar = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up);
            RotateTowardsDirection(cameraPlanar);
        }
        else
        {
            RotateTowardsMovement(planarDirection);
        }

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
            turningSpeed * Time.deltaTime
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
        float targetDistance = isAiming ? aimCameraDistance : cameraDistance;
        float targetHeight = isAiming ? aimCameraHeight : cameraHeight;
        Vector3 targetShoulder = isAiming ? aimShoulderOffset : Vector3.zero;
        float targetFOV = isAiming ? aimFieldOfView : defaultFieldOfView;

        currentCameraDistance = Mathf.Lerp(currentCameraDistance, targetDistance, Time.deltaTime * aimTransitionSpeed);
        currentCameraHeight = Mathf.Lerp(currentCameraHeight, targetHeight, Time.deltaTime * aimTransitionSpeed);
        currentShoulderOffset = Vector3.Lerp(currentShoulderOffset, targetShoulder, Time.deltaTime * aimTransitionSpeed);
        currentFOV = Mathf.Lerp(currentFOV, targetFOV, Time.deltaTime * aimTransitionSpeed);

        if (playerCamera != null)
        {
            playerCamera.fieldOfView = currentFOV;
        }

        Vector3 pivot = transform.position + Vector3.up * currentCameraHeight;
        Quaternion cameraRotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);

        Vector3 cameraRight = cameraRotation * Vector3.right;
        Vector3 cameraUp = Vector3.up;
        Vector3 cameraForward = cameraRotation * Vector3.forward;

        Vector3 desiredPosition = pivot
            + (cameraRight * currentShoulderOffset.x)
            + (cameraUp * currentShoulderOffset.y)
            - (cameraForward * currentCameraDistance);

        Vector3 rayDirection = desiredPosition - pivot;
        float distance = rayDirection.magnitude;

        float allowedDistance = distance;

        if (distance > 0.001f)
        {
            RaycastHit[] hits = Physics.SphereCastAll(
                pivot,
                cameraCollisionRadius,
                rayDirection.normalized,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            foreach (RaycastHit hit in hits)
            {
                Transform hitTransform = hit.collider.transform;

                if (
                    hitTransform == transform ||
                    hitTransform.IsChildOf(transform)
                )
                {
                    continue;
                }

                allowedDistance = Mathf.Min(
                    allowedDistance,
                    Mathf.Max(
                        0.2f,
                        hit.distance - cameraCollisionRadius
                    )
                );
            }
        }

        playerCamera.transform.position = pivot + rayDirection.normalized * allowedDistance;
        playerCamera.transform.rotation = cameraRotation;
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

        RaycastHit[] hits = Physics.RaycastAll(
            aimRay,
            aimMaxDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        System.Array.Sort(
            hits,
            (first, second) => first.distance.CompareTo(second.distance)
        );

        bool foundHit = false;
        Vector3 hitPoint = Vector3.zero;

        foreach (RaycastHit hit in hits)
        {
            if (
                hit.transform == transform ||
                hit.transform.IsChildOf(transform)
            )
            {
                continue;
            }

            hitPoint = hit.point;
            foundHit = true;
            break;
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