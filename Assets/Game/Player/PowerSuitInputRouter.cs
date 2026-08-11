using System;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

[Flags]
public enum PowerSuitInputButtons : ushort
{
    None = 0,
    Jump = 1 << 0,
    Boost = 1 << 2,
    Aim = 1 << 3,
    Fire = 1 << 4,
    Reload = 1 << 5,
    CarryToggle = 1 << 6,
    ShoulderRocket = 1 << 7,
    Lightning = 1 << 8,
    Ultimate = 1 << 9,
    Console = 1 << 10,
    Cancel = 1 << 11,
    Scope = 1 << 12,
    WeaponSlot1 = 1 << 13,
    WeaponSlot2 = 1 << 14,
    WeaponNext = 1 << 15
}

/// <summary>
/// Named physical controls used by the default gamepad map. Keeping this map
/// in plain C# makes conflicts visible and testable without an input device.
/// </summary>
public enum PowerSuitGamepadControl
{
    ButtonSouth,
    ButtonEast,
    ButtonWest,
    ButtonNorth,
    LeftShoulder,
    RightShoulder,
    LeftTrigger,
    RightTrigger,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
    RightStickPress,
    Start
}

public static class PowerSuitDefaultInputBindings
{
    /// <summary>
    /// Resolves one physical gamepad control to its discrete gameplay intent.
    /// Jump/flight is owned by the south button and fire is exclusively on the
    /// right trigger. The west button cycles the compact weapon loadout.
    /// </summary>
    public static PowerSuitInputButtons GetGamepadIntent(
        PowerSuitGamepadControl control
    )
    {
        switch (control)
        {
            case PowerSuitGamepadControl.ButtonSouth:
                return PowerSuitInputButtons.Jump;
            case PowerSuitGamepadControl.ButtonEast:
                return PowerSuitInputButtons.Cancel;
            case PowerSuitGamepadControl.ButtonWest:
                return PowerSuitInputButtons.WeaponNext;
            case PowerSuitGamepadControl.ButtonNorth:
                return PowerSuitInputButtons.Reload;
            case PowerSuitGamepadControl.RightShoulder:
                return PowerSuitInputButtons.Boost;
            case PowerSuitGamepadControl.LeftTrigger:
                return PowerSuitInputButtons.Aim;
            case PowerSuitGamepadControl.RightTrigger:
                return PowerSuitInputButtons.Fire;
            case PowerSuitGamepadControl.DpadUp:
                return PowerSuitInputButtons.Ultimate;
            case PowerSuitGamepadControl.DpadDown:
                return PowerSuitInputButtons.CarryToggle;
            case PowerSuitGamepadControl.DpadLeft:
                return PowerSuitInputButtons.ShoulderRocket;
            case PowerSuitGamepadControl.DpadRight:
                return PowerSuitInputButtons.Lightning;
            case PowerSuitGamepadControl.RightStickPress:
                return PowerSuitInputButtons.Scope;
            case PowerSuitGamepadControl.Start:
                return PowerSuitInputButtons.Console;
            default:
                // Left shoulder is a continuous descend control rather than a
                // discrete button intent.
                return PowerSuitInputButtons.None;
        }
    }
}

/// <summary>
/// Unfiltered device state captured at one point in a frame. Pressed and
/// released bits retain short taps which begin and end between two updates.
/// </summary>
public readonly struct PowerSuitRawInputState
{
    public Vector2 Move { get; }
    public Vector2 PointerLook { get; }
    public Vector2 GamepadLook { get; }
    public float Vertical { get; }
    public PowerSuitInputButtons Held { get; }
    public PowerSuitInputButtons Pressed { get; }
    public PowerSuitInputButtons Released { get; }

    public PowerSuitRawInputState(
        Vector2 move,
        Vector2 pointerLook,
        Vector2 gamepadLook,
        float vertical,
        PowerSuitInputButtons held,
        PowerSuitInputButtons pressed,
        PowerSuitInputButtons released
    )
    {
        Move = Vector2.ClampMagnitude(move, 1f);
        PointerLook = pointerLook;
        GamepadLook = Vector2.ClampMagnitude(gamepadLook, 1f);
        Vertical = Mathf.Clamp(vertical, -1f, 1f);
        Held = held;
        Pressed = pressed;
        Released = released;
    }
}

/// <summary>
/// Immutable player intent shared by every gameplay consumer for one frame.
/// </summary>
public readonly struct PowerSuitInputSnapshot
{
    public int SampleFrame { get; }
    public Vector2 Move { get; }
    public Vector2 PointerLook { get; }
    public Vector2 GamepadLook { get; }
    public Vector2 Look => PointerLook + GamepadLook;
    public float Vertical { get; }
    public PowerSuitInputButtons Held { get; }
    public PowerSuitInputButtons Pressed { get; }
    public PowerSuitInputButtons Released { get; }

    public bool JumpHeld => IsHeld(PowerSuitInputButtons.Jump);
    public bool JumpPressed => WasPressed(PowerSuitInputButtons.Jump);
    public bool BoostHeld => IsHeld(PowerSuitInputButtons.Boost);
    public bool AimHeld => IsHeld(PowerSuitInputButtons.Aim);
    public bool FireHeld => IsHeld(PowerSuitInputButtons.Fire);
    public bool FirePressed => WasPressed(PowerSuitInputButtons.Fire);
    public bool ReloadPressed => WasPressed(PowerSuitInputButtons.Reload);
    public bool CarryTogglePressed =>
        WasPressed(PowerSuitInputButtons.CarryToggle);
    public bool ShoulderRocketPressed =>
        WasPressed(PowerSuitInputButtons.ShoulderRocket);
    public bool LightningHeld => IsHeld(PowerSuitInputButtons.Lightning);
    public bool LightningPressed =>
        WasPressed(PowerSuitInputButtons.Lightning);
    public bool LightningReleased =>
        WasReleased(PowerSuitInputButtons.Lightning);
    public bool UltimatePressed => WasPressed(PowerSuitInputButtons.Ultimate);
    public bool ConsolePressed => WasPressed(PowerSuitInputButtons.Console);
    public bool CancelPressed => WasPressed(PowerSuitInputButtons.Cancel);
    public bool ScopeHeld => IsHeld(PowerSuitInputButtons.Scope);
    public bool ScopePressed => WasPressed(PowerSuitInputButtons.Scope);
    public bool WeaponSlot1Pressed =>
        WasPressed(PowerSuitInputButtons.WeaponSlot1);
    public bool WeaponSlot2Pressed =>
        WasPressed(PowerSuitInputButtons.WeaponSlot2);
    public bool WeaponNextPressed =>
        WasPressed(PowerSuitInputButtons.WeaponNext);

    public PowerSuitInputSnapshot(
        int sampleFrame,
        Vector2 move,
        Vector2 pointerLook,
        Vector2 gamepadLook,
        float vertical,
        PowerSuitInputButtons held,
        PowerSuitInputButtons pressed,
        PowerSuitInputButtons released
    )
    {
        SampleFrame = sampleFrame;
        Move = Vector2.ClampMagnitude(move, 1f);
        PointerLook = pointerLook;
        GamepadLook = Vector2.ClampMagnitude(gamepadLook, 1f);
        Vertical = Mathf.Clamp(vertical, -1f, 1f);
        Held = held;
        Pressed = pressed;
        Released = released;
    }

    public bool IsHeld(PowerSuitInputButtons button)
    {
        return (Held & button) != 0;
    }

    public bool WasPressed(PowerSuitInputButtons button)
    {
        return (Pressed & button) != 0;
    }

    public bool WasReleased(PowerSuitInputButtons button)
    {
        return (Released & button) != 0;
    }
}

/// <summary>
/// Plain frame cache which turns raw device state into stable held/edge
/// semantics. The first sample for a frame always wins.
/// </summary>
public sealed class PowerSuitInputFrameBuffer
{
    private PowerSuitInputSnapshot snapshot;
    private PowerSuitInputButtons previousHeld;
    private bool hasPreviousHeld;

    public bool HasSnapshot { get; private set; }

    public int SampleFrame => HasSnapshot ? snapshot.SampleFrame : -1;

    public bool NeedsSample(int frame)
    {
        return !HasSnapshot || snapshot.SampleFrame != frame;
    }

    public PowerSuitInputSnapshot Sample(
        int frame,
        PowerSuitRawInputState raw
    )
    {
        if (!NeedsSample(frame))
        {
            return snapshot;
        }

        PowerSuitInputButtons pressed = raw.Pressed;
        PowerSuitInputButtons released = raw.Released;
        if (hasPreviousHeld)
        {
            // Device APIs normally provide edges. Deriving missing transitions
            // as well keeps custom/test sources deterministic and robust.
            pressed |= raw.Held & ~previousHeld;
            released |= previousHeld & ~raw.Held;

            // Logical edges are aggregated across devices. Pressing a second
            // bound control while the intent was already held is not a second
            // gameplay press.
            pressed &= ~previousHeld;
        }

        // A release from one device cannot cancel the same logical control
        // while another device still holds it.
        released &= ~raw.Held;

        snapshot = new PowerSuitInputSnapshot(
            frame,
            raw.Move,
            raw.PointerLook,
            raw.GamepadLook,
            raw.Vertical,
            raw.Held,
            pressed,
            released
        );
        previousHeld = raw.Held;
        hasPreviousHeld = true;
        HasSnapshot = true;
        return snapshot;
    }

    public bool TryGetSnapshot(
        int frame,
        out PowerSuitInputSnapshot current
    )
    {
        if (HasSnapshot && snapshot.SampleFrame == frame)
        {
            current = snapshot;
            return true;
        }

        current = default;
        return false;
    }

    /// <summary>
    /// Clears cached edges and the held baseline. A re-enabled router therefore
    /// does not invent a press for a button which was already being held.
    /// </summary>
    public void Reset()
    {
        snapshot = default;
        previousHeld = PowerSuitInputButtons.None;
        hasPreviousHeld = false;
        HasSnapshot = false;
    }
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(-300)]
public sealed class PowerSuitInputRouter : MonoBehaviour
{
    private PowerSuitInputFrameBuffer frameBuffer;

    public PowerSuitInputSnapshot CurrentSnapshot
    {
        get
        {
            if (!TryGetCurrentSnapshot(out PowerSuitInputSnapshot current))
            {
                return default;
            }

            return current;
        }
    }

    private void Awake()
    {
        EnsureFrameBuffer();
    }

    private void OnEnable()
    {
        EnsureFrameBuffer();
        frameBuffer.Reset();
    }

    private void OnDisable()
    {
        frameBuffer?.Reset();
    }

    private void Update()
    {
        EnsureFrameBuffer();
        int frame = Time.frameCount;
        if (!frameBuffer.NeedsSample(frame))
        {
            return;
        }

        frameBuffer.Sample(frame, ReadCurrentRawState());
    }

    public bool TryGetCurrentSnapshot(out PowerSuitInputSnapshot current)
    {
        if (!isActiveAndEnabled || frameBuffer == null)
        {
            current = default;
            return false;
        }

        return frameBuffer.TryGetSnapshot(Time.frameCount, out current);
    }

    /// <summary>
    /// Direct device fallback for legacy scenes which do not yet contain a
    /// router. Consumers should cache this result for the rest of the frame.
    /// </summary>
    public static PowerSuitInputSnapshot ReadFallbackSnapshot()
    {
        PowerSuitRawInputState raw = ReadCurrentRawState();
        return new PowerSuitInputSnapshot(
            Time.frameCount,
            raw.Move,
            raw.PointerLook,
            raw.GamepadLook,
            raw.Vertical,
            raw.Held,
            raw.Pressed,
            raw.Released & ~raw.Held
        );
    }

    private void EnsureFrameBuffer()
    {
        if (frameBuffer == null)
        {
            frameBuffer = new PowerSuitInputFrameBuffer();
        }
    }

    private static PowerSuitRawInputState ReadCurrentRawState()
    {
        Vector2 move = Vector2.zero;
        Vector2 pointerLook = Vector2.zero;
        Vector2 gamepadLook = Vector2.zero;
        float vertical = 0f;
        PowerSuitInputButtons held = PowerSuitInputButtons.None;
        PowerSuitInputButtons pressed = PowerSuitInputButtons.None;
        PowerSuitInputButtons released = PowerSuitInputButtons.None;

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed)
            {
                move.y += 1f;
            }
            if (keyboard.sKey.isPressed)
            {
                move.y -= 1f;
            }
            if (keyboard.dKey.isPressed)
            {
                move.x += 1f;
            }
            if (keyboard.aKey.isPressed)
            {
                move.x -= 1f;
            }

            AddButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitInputButtons.Jump,
                keyboard.spaceKey
            );
            AddButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitInputButtons.Boost,
                keyboard.leftShiftKey
            );
            AddButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitInputButtons.Reload,
                keyboard.rKey
            );
            AddButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitInputButtons.CarryToggle,
                keyboard.qKey
            );
            AddButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitInputButtons.ShoulderRocket,
                keyboard.gKey
            );
            AddButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitInputButtons.Lightning,
                keyboard.eKey
            );
            AddButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitInputButtons.Ultimate,
                keyboard.xKey
            );
            AddButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitInputButtons.Scope,
                keyboard.vKey
            );
            AddButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitInputButtons.WeaponSlot1,
                keyboard.digit1Key
            );
            AddButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitInputButtons.WeaponSlot2,
                keyboard.digit2Key
            );
            AddButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitInputButtons.Console,
                keyboard.backquoteKey
            );
            AddButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitInputButtons.Cancel,
                keyboard.escapeKey
            );

            if (keyboard.spaceKey.isPressed)
            {
                vertical += 1f;
            }
            if (
                keyboard.leftCtrlKey.isPressed ||
                keyboard.cKey.isPressed
            )
            {
                vertical -= 1f;
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            pointerLook = mouse.delta.ReadValue();
            AddButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitInputButtons.Aim,
                mouse.rightButton
            );
            AddButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitInputButtons.Fire,
                mouse.leftButton
            );
            if (Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f)
            {
                pressed |= PowerSuitInputButtons.WeaponNext;
            }
        }

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null)
        {
            move += gamepad.leftStick.ReadValue();
            gamepadLook = gamepad.rightStick.ReadValue();
            if (gamepad.buttonSouth.isPressed)
            {
                vertical += 1f;
            }
            if (gamepad.leftShoulder.isPressed)
            {
                vertical -= 1f;
            }

            AddGamepadButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitGamepadControl.ButtonSouth,
                gamepad.buttonSouth
            );
            AddGamepadButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitGamepadControl.ButtonEast,
                gamepad.buttonEast
            );
            AddGamepadButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitGamepadControl.ButtonWest,
                gamepad.buttonWest
            );
            AddGamepadButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitGamepadControl.ButtonNorth,
                gamepad.buttonNorth
            );
            AddGamepadButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitGamepadControl.RightShoulder,
                gamepad.rightShoulder
            );
            AddGamepadButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitGamepadControl.LeftTrigger,
                gamepad.leftTrigger
            );
            AddGamepadButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitGamepadControl.RightTrigger,
                gamepad.rightTrigger
            );
            AddGamepadButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitGamepadControl.DpadUp,
                gamepad.dpad.up
            );
            AddGamepadButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitGamepadControl.DpadDown,
                gamepad.dpad.down
            );
            AddGamepadButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitGamepadControl.DpadLeft,
                gamepad.dpad.left
            );
            AddGamepadButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitGamepadControl.DpadRight,
                gamepad.dpad.right
            );
            AddGamepadButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitGamepadControl.RightStickPress,
                gamepad.rightStickButton
            );
            AddGamepadButton(
                ref held,
                ref pressed,
                ref released,
                PowerSuitGamepadControl.Start,
                gamepad.startButton
            );
        }
#else
        move = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        );
        pointerLook = new Vector2(
            Input.GetAxis("Mouse X"),
            Input.GetAxis("Mouse Y")
        );

        AddButton(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.Jump,
            Input.GetKey(KeyCode.Space) ||
                Input.GetKey(KeyCode.JoystickButton0),
            Input.GetKeyDown(KeyCode.Space) ||
                Input.GetKeyDown(KeyCode.JoystickButton0),
            Input.GetKeyUp(KeyCode.Space) ||
                Input.GetKeyUp(KeyCode.JoystickButton0)
        );
        AddButton(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.Boost,
            Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.JoystickButton5),
            Input.GetKeyDown(KeyCode.LeftShift) ||
                Input.GetKeyDown(KeyCode.JoystickButton5),
            Input.GetKeyUp(KeyCode.LeftShift) ||
                Input.GetKeyUp(KeyCode.JoystickButton5)
        );
        AddButton(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.Aim,
            Input.GetMouseButton(1),
            Input.GetMouseButtonDown(1),
            Input.GetMouseButtonUp(1)
        );
        AddButton(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.Fire,
            Input.GetMouseButton(0),
            Input.GetMouseButtonDown(0),
            Input.GetMouseButtonUp(0)
        );
        AddLegacyKey(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.Reload,
            KeyCode.R,
            KeyCode.JoystickButton3
        );
        AddLegacyKey(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.CarryToggle,
            KeyCode.Q,
            KeyCode.JoystickButton6
        );
        AddLegacyKey(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.ShoulderRocket,
            KeyCode.G
        );
        AddLegacyKey(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.Lightning,
            KeyCode.E
        );
        AddLegacyKey(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.Ultimate,
            KeyCode.X
        );
        AddLegacyKey(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.Scope,
            KeyCode.V,
            KeyCode.JoystickButton9
        );
        AddLegacyKey(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.WeaponSlot1,
            KeyCode.Alpha1
        );
        AddLegacyKey(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.WeaponSlot2,
            KeyCode.Alpha2
        );
        AddLegacyKey(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.WeaponNext,
            KeyCode.JoystickButton2
        );
        if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f)
        {
            pressed |= PowerSuitInputButtons.WeaponNext;
        }
        AddLegacyKey(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.Console,
            KeyCode.BackQuote,
            KeyCode.JoystickButton7
        );
        AddLegacyKey(
            ref held,
            ref pressed,
            ref released,
            PowerSuitInputButtons.Cancel,
            KeyCode.Escape,
            KeyCode.JoystickButton1
        );

        if (
            Input.GetKey(KeyCode.Space) ||
            Input.GetKey(KeyCode.JoystickButton0)
        )
        {
            vertical += 1f;
        }
        if (
            Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.C) ||
            Input.GetKey(KeyCode.JoystickButton4)
        )
        {
            vertical -= 1f;
        }
#endif

        return new PowerSuitRawInputState(
            move,
            pointerLook,
            gamepadLook,
            vertical,
            held,
            pressed,
            released
        );
    }

#if ENABLE_INPUT_SYSTEM
    private static void AddGamepadButton(
        ref PowerSuitInputButtons held,
        ref PowerSuitInputButtons pressed,
        ref PowerSuitInputButtons released,
        PowerSuitGamepadControl control,
        ButtonControl button
    )
    {
        AddButton(
            ref held,
            ref pressed,
            ref released,
            PowerSuitDefaultInputBindings.GetGamepadIntent(control),
            button
        );
    }

    private static void AddButton(
        ref PowerSuitInputButtons held,
        ref PowerSuitInputButtons pressed,
        ref PowerSuitInputButtons released,
        PowerSuitInputButtons intent,
        ButtonControl button
    )
    {
        AddButton(
            ref held,
            ref pressed,
            ref released,
            intent,
            button.isPressed,
            button.wasPressedThisFrame,
            button.wasReleasedThisFrame
        );
    }
#else
    private static void AddLegacyKey(
        ref PowerSuitInputButtons held,
        ref PowerSuitInputButtons pressed,
        ref PowerSuitInputButtons released,
        PowerSuitInputButtons intent,
        KeyCode first,
        KeyCode second = KeyCode.None
    )
    {
        bool hasSecond = second != KeyCode.None;
        AddButton(
            ref held,
            ref pressed,
            ref released,
            intent,
            Input.GetKey(first) || (hasSecond && Input.GetKey(second)),
            Input.GetKeyDown(first) ||
                (hasSecond && Input.GetKeyDown(second)),
            Input.GetKeyUp(first) || (hasSecond && Input.GetKeyUp(second))
        );
    }
#endif

    private static void AddButton(
        ref PowerSuitInputButtons held,
        ref PowerSuitInputButtons pressed,
        ref PowerSuitInputButtons released,
        PowerSuitInputButtons intent,
        bool isHeld,
        bool wasPressed,
        bool wasReleased
    )
    {
        if (intent == PowerSuitInputButtons.None)
        {
            return;
        }

        if (isHeld)
        {
            held |= intent;
        }
        if (wasPressed)
        {
            pressed |= intent;
        }
        if (wasReleased)
        {
            released |= intent;
        }
    }
}
