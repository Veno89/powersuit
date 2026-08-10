using System;
using UnityEngine;

/// <summary>
/// Presentation-only flight attitude and landing compression. This component
/// never rotates or translates the CharacterController root; it offsets the
/// explicitly assigned visual wrapper relative to its authored transform.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class PowerSuitVisualFlightResponse : MonoBehaviour
{
    [SerializeField] private PowerSuitController controller;
    [SerializeField] private Transform visualRoot;

    [Header("Flight Attitude")]
    [SerializeField, Range(0f, 30f)] private float maximumBankDegrees = 12f;
    [SerializeField, Range(0f, 20f)] private float maximumPitchDegrees = 6f;
    [SerializeField, Range(0f, 12f)] private float boostPitchDegrees = 3f;
    [SerializeField, Range(0f, 1f)] private float aimedAttitudeMultiplier = 0.25f;
    [SerializeField, Min(0f)] private float attitudeSharpness = 12f;

    [Header("Landing Response")]
    [Tooltip("Maximum proportional world-height squash. The wrapper origin/feet stay fixed.")]
    [SerializeField, Range(0f, 0.15f)] private float maximumCompression = 0.06f;
    [SerializeField, Min(0f)] private float minimumImpactSpeed = 4f;
    [SerializeField, Min(0.01f)] private float fullCompressionImpactSpeed = 14f;
    [SerializeField, Min(0f)] private float compressionRecoverySharpness = 16f;

    private Vector3 authoredLocalPosition;
    private Quaternion authoredLocalRotation;
    private Vector3 authoredLocalScale;
    private float currentPitch;
    private float currentRoll;
    private float compression;
    private bool hasAuthoredPose;
    private bool subscribed;
    private int compressionScaleAxis = 1;

    public Transform VisualRoot
    {
        get => visualRoot;
        set
        {
            visualRoot = value;
            CaptureAuthoredPose();
        }
    }

    public float CurrentPitchDegrees => currentPitch;
    public float CurrentRollDegrees => currentRoll;
    public float CurrentCompression => compression;

    private void Awake()
    {
        if (controller == null)
        {
            controller = GetComponent<PowerSuitController>();
        }

        CaptureAuthoredPose();
    }

    private void OnEnable()
    {
        if (controller == null)
        {
            controller = GetComponent<PowerSuitController>();
        }

        if (!hasAuthoredPose)
        {
            CaptureAuthoredPose();
        }

        SubscribeLanding();
    }

    private void LateUpdate()
    {
        if (controller == null || visualRoot == null || !hasAuthoredPose)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        if (!PowerSuitVisualResponseMath.IsUsableDeltaTime(deltaTime))
        {
            return;
        }

        float targetPitch = 0f;
        float targetRoll = 0f;
        if (controller.IsFlying)
        {
            Vector2 movement = Vector2.ClampMagnitude(
                controller.LocalMovement,
                1f
            );
            float attitudeMultiplier = controller.IsAiming
                ? aimedAttitudeMultiplier
                : 1f;
            targetRoll =
                -movement.x * maximumBankDegrees * attitudeMultiplier;
            targetPitch =
                -movement.y * maximumPitchDegrees * attitudeMultiplier;
            if (controller.IsBoosting)
            {
                targetPitch -= boostPitchDegrees * attitudeMultiplier;
            }
        }

        currentPitch = PowerSuitVisualResponseMath.ExponentialStep(
            currentPitch,
            targetPitch,
            attitudeSharpness,
            deltaTime
        );
        currentRoll = PowerSuitVisualResponseMath.ExponentialStep(
            currentRoll,
            targetRoll,
            attitudeSharpness,
            deltaTime
        );
        compression = PowerSuitVisualResponseMath.ExponentialStep(
            compression,
            0f,
            compressionRecoverySharpness,
            deltaTime
        );

        // Pre-multiplication keeps pitch and roll in player/root space while
        // retaining the wrapper's authored model-axis correction.
        visualRoot.localRotation =
            Quaternion.Euler(currentPitch, 0f, currentRoll) *
            authoredLocalRotation;
        visualRoot.localPosition = authoredLocalPosition;
        Vector3 compressedScale = authoredLocalScale;
        float compressionMultiplier = 1f - compression;
        compressedScale[compressionScaleAxis] *= compressionMultiplier;
        visualRoot.localScale = compressedScale;
    }

    private void OnDisable()
    {
        UnsubscribeLanding();
        RestoreAuthoredPose();
        currentPitch = 0f;
        currentRoll = 0f;
        compression = 0f;
    }

    public void ResetPresentation()
    {
        currentPitch = 0f;
        currentRoll = 0f;
        compression = 0f;
        RestoreAuthoredPose();
    }

    private void CaptureAuthoredPose()
    {
        if (visualRoot == null)
        {
            hasAuthoredPose = false;
            return;
        }

        authoredLocalPosition = visualRoot.localPosition;
        authoredLocalRotation = visualRoot.localRotation;
        authoredLocalScale = visualRoot.localScale;
        Vector3 localUp =
            Quaternion.Inverse(authoredLocalRotation) * Vector3.up;
        compressionScaleAxis =
            PowerSuitVisualResponseMath.FindDominantScaleAxis(localUp);
        hasAuthoredPose = true;
    }

    private void RestoreAuthoredPose()
    {
        if (!hasAuthoredPose || visualRoot == null)
        {
            return;
        }

        visualRoot.localPosition = authoredLocalPosition;
        visualRoot.localRotation = authoredLocalRotation;
        visualRoot.localScale = authoredLocalScale;
    }

    private void SubscribeLanding()
    {
        if (subscribed || controller == null)
        {
            return;
        }

        controller.Landed += HandleLanding;
        subscribed = true;
    }

    private void UnsubscribeLanding()
    {
        if (!subscribed || controller == null)
        {
            return;
        }

        controller.Landed -= HandleLanding;
        subscribed = false;
    }

    private void HandleLanding(float impactSpeed)
    {
        float landingAmount =
            PowerSuitVisualResponseMath.CalculateLandingCompression(
                -Mathf.Max(0f, impactSpeed),
                minimumImpactSpeed,
                fullCompressionImpactSpeed,
                maximumCompression
            );
        compression = Mathf.Max(compression, landingAmount);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maximumBankDegrees = SanitizeNonNegative(maximumBankDegrees, 12f);
        maximumPitchDegrees = SanitizeNonNegative(maximumPitchDegrees, 6f);
        boostPitchDegrees = SanitizeNonNegative(boostPitchDegrees, 3f);
        aimedAttitudeMultiplier = Mathf.Clamp01(
            float.IsNaN(aimedAttitudeMultiplier) ||
            float.IsInfinity(aimedAttitudeMultiplier)
                ? 0.25f
                : aimedAttitudeMultiplier
        );
        attitudeSharpness = SanitizeNonNegative(attitudeSharpness, 12f);
        maximumCompression = Mathf.Clamp(
            SanitizeNonNegative(maximumCompression, 0.06f),
            0f,
            0.15f
        );
        minimumImpactSpeed = SanitizeNonNegative(minimumImpactSpeed, 4f);
        fullCompressionImpactSpeed = Mathf.Max(
            minimumImpactSpeed + 0.01f,
            SanitizeNonNegative(fullCompressionImpactSpeed, 14f)
        );
        compressionRecoverySharpness = SanitizeNonNegative(
            compressionRecoverySharpness,
            16f
        );
    }

    private static float SanitizeNonNegative(float value, float fallback)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f
            ? value
            : fallback;
    }
#endif
}

public static class PowerSuitVisualResponseMath
{
    public static int FindDominantScaleAxis(Vector3 localDirection)
    {
        if (
            float.IsNaN(localDirection.x) ||
            float.IsNaN(localDirection.y) ||
            float.IsNaN(localDirection.z) ||
            float.IsInfinity(localDirection.x) ||
            float.IsInfinity(localDirection.y) ||
            float.IsInfinity(localDirection.z) ||
            localDirection.sqrMagnitude <= 0.000001f
        )
        {
            throw new ArgumentOutOfRangeException(nameof(localDirection));
        }

        Vector3 magnitude = new Vector3(
            Mathf.Abs(localDirection.x),
            Mathf.Abs(localDirection.y),
            Mathf.Abs(localDirection.z)
        );
        if (magnitude.x >= magnitude.y && magnitude.x >= magnitude.z)
        {
            return 0;
        }

        return magnitude.y >= magnitude.z ? 1 : 2;
    }

    public static bool IsUsableDeltaTime(float deltaTime)
    {
        return
            !float.IsNaN(deltaTime) &&
            !float.IsInfinity(deltaTime) &&
            deltaTime > 0f;
    }

    public static float ExponentialStep(
        float current,
        float target,
        float sharpness,
        float deltaTime
    )
    {
        RequireFinite(current, nameof(current));
        RequireFinite(target, nameof(target));
        RequireFiniteNonNegative(sharpness, nameof(sharpness));
        if (!IsUsableDeltaTime(deltaTime))
        {
            return current;
        }

        if (sharpness <= 0f)
        {
            return target;
        }

        float blend = 1f - Mathf.Exp(-sharpness * deltaTime);
        return Mathf.LerpUnclamped(current, target, blend);
    }

    public static float CalculateLandingCompression(
        float verticalSpeed,
        float minimumImpactSpeed,
        float fullCompressionImpactSpeed,
        float maximumCompression
    )
    {
        RequireFinite(verticalSpeed, nameof(verticalSpeed));
        RequireFiniteNonNegative(minimumImpactSpeed, nameof(minimumImpactSpeed));
        RequireFiniteNonNegative(maximumCompression, nameof(maximumCompression));
        if (
            float.IsNaN(fullCompressionImpactSpeed) ||
            float.IsInfinity(fullCompressionImpactSpeed) ||
            fullCompressionImpactSpeed <= minimumImpactSpeed
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(fullCompressionImpactSpeed)
            );
        }

        float impactSpeed = Mathf.Max(0f, -verticalSpeed);
        float normalized = Mathf.InverseLerp(
            minimumImpactSpeed,
            fullCompressionImpactSpeed,
            impactSpeed
        );
        return maximumCompression * normalized;
    }

    private static void RequireFinite(float value, string parameterName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void RequireFiniteNonNegative(
        float value,
        string parameterName
    )
    {
        RequireFinite(value, parameterName);
        if (value < 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
