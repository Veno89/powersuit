using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Cached, procedural blue-white propulsion feedback for powered sprint and
/// flight. It is presentation-only and never changes movement or heat state.
/// Shared propulsion heat shifts the plume toward orange/red near lockout.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(210)]
public sealed class PowerSuitThrusterPresentation : MonoBehaviour
{
    private const float VisibilityThreshold = 0.01f;
    private const int ExpectedJetCount = 4;

    [SerializeField] private PowerSuitController controller;
    [SerializeField] private PowerSuitPropulsionHeat propulsionHeat;
    [SerializeField] private Transform visualRoot;

    [Header("Response")]
    [SerializeField, Range(0f, 1f)] private float sprintIntensity = 1f;
    [SerializeField, Range(0f, 1f)] private float flightIntensity = 0.48f;
    [SerializeField, Range(0f, 1f)] private float boostIntensity = 1f;
    [SerializeField, Min(0f)] private float ignitionSharpness = 20f;
    [SerializeField, Min(0f)] private float releaseSharpness = 11f;

    [Header("Jet Shape")]
    [SerializeField, Min(0.01f)] private float backpackMaximumLength = 1.7f;
    [SerializeField, Min(0.01f)] private float bootMaximumLength = 1.1f;
    [SerializeField, Min(0.001f)] private float outerWidth = 0.16f;
    [SerializeField, Min(0.001f)] private float coreWidth = 0.055f;

    private readonly ThrusterJet[] jets = new ThrusterJet[ExpectedJetCount];
    private Material jetMaterial;
    private float currentIntensity;
    private bool isBuilt;

    public Transform VisualRoot
    {
        get => visualRoot;
        set
        {
            visualRoot = value;
            RebuildAnchors();
        }
    }

    public int CachedJetCount { get; private set; }
    public float CurrentIntensity => currentIntensity;

    private void Awake()
    {
        ResolveDependencies();
        EnsureBuilt();
        ApplyVisuals(0f);
    }

    private void OnEnable()
    {
        ResolveDependencies();
        EnsureBuilt();
    }

    private void LateUpdate()
    {
        if (controller == null || !isBuilt)
        {
            return;
        }

        float target = PowerSuitThrusterMath.ResolveTargetIntensity(
            controller.IsRunning,
            controller.IsFlying,
            controller.IsBoosting,
            sprintIntensity,
            flightIntensity,
            boostIntensity
        );
        float sharpness = target > currentIntensity
            ? ignitionSharpness
            : releaseSharpness;
        currentIntensity = PowerSuitVisualResponseMath.ExponentialStep(
            currentIntensity,
            target,
            sharpness,
            Time.deltaTime
        );
        if (Mathf.Abs(currentIntensity - target) < 0.001f)
        {
            currentIntensity = target;
        }

        ApplyVisuals(currentIntensity);
    }

    private void OnDisable()
    {
        currentIntensity = 0f;
        ApplyVisuals(0f);
    }

    private void ResolveDependencies()
    {
        controller ??= GetComponent<PowerSuitController>();
        propulsionHeat ??= GetComponent<PowerSuitPropulsionHeat>();
        if (visualRoot == null)
        {
            Transform candidate = transform.Find("PowerSuitVisual_Generator109");
            visualRoot = candidate != null ? candidate : transform;
        }
    }

    private void EnsureBuilt()
    {
        if (isBuilt || visualRoot == null)
        {
            return;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        if (shader == null)
        {
            throw new InvalidOperationException(
                "No unlit shader is available for suit thruster feedback."
            );
        }

        jetMaterial = new Material(shader)
        {
            name = "Power Suit Thruster Jet (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };

        CreateJet(0, "Thruster_Nozzle.L", false);
        CreateJet(1, "Thruster_Nozzle.R", false);
        CreateJet(2, "Heavy_Boot.L", true);
        CreateJet(3, "Heavy_Boot.R", true);
        isBuilt = true;
    }

    private void RebuildAnchors()
    {
        if (!isBuilt)
        {
            return;
        }

        for (int index = 0; index < jets.Length; index++)
        {
            if (jets[index] != null)
            {
                jets[index].DestroyImmediateSafe();
                jets[index] = null;
            }
        }
        CachedJetCount = 0;
        isBuilt = false;
        EnsureBuilt();
    }

    private void CreateJet(int index, string anchorName, bool isBoot)
    {
        Transform anchor = FindChildRecursive(visualRoot, anchorName);
        if (anchor == null)
        {
            return;
        }

        GameObject host = new GameObject($"{anchorName} Powered Exhaust");
        host.transform.SetParent(anchor, false);
        LineRenderer outer = CreateLine(host.transform, "Outer Plume", outerWidth);
        LineRenderer core = CreateLine(host.transform, "White-Hot Core", coreWidth);

        jets[index] = new ThrusterJet(
            host,
            anchor,
            outer,
            core,
            null,
            isBoot,
            isBoot ? bootMaximumLength : backpackMaximumLength
        );
        CachedJetCount++;
    }

    private LineRenderer CreateLine(Transform parent, string name, float width)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        LineRenderer line = child.AddComponent<LineRenderer>();
        line.sharedMaterial = jetMaterial;
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = width;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        line.enabled = false;
        return line;
    }

    private void ApplyVisuals(float intensity)
    {
        float clamped = Mathf.Clamp01(intensity);
        bool visible = clamped > VisibilityThreshold;
        Vector2 movement = controller != null
            ? Vector2.ClampMagnitude(controller.LocalMovement, 1f)
            : Vector2.zero;
        Vector3 planar = transform.forward * movement.y + transform.right * movement.x;

        for (int index = 0; index < jets.Length; index++)
        {
            ThrusterJet jet = jets[index];
            if (jet == null || jet.Anchor == null)
            {
                continue;
            }

            jet.SetEnabled(visible);
            if (!visible)
            {
                continue;
            }

            Vector3 direction;
            if (controller != null && controller.IsFlying)
            {
                direction = jet.IsBoot
                    ? (-Vector3.up - planar * 0.32f).normalized
                    : (-Vector3.up * 0.45f - planar * 0.9f).normalized;
            }
            else
            {
                direction = jet.IsBoot
                    ? (-Vector3.up * 0.42f - transform.forward).normalized
                    : (-transform.forward - Vector3.up * 0.16f).normalized;
            }

            float heat = propulsionHeat != null
                ? propulsionHeat.NormalizedHeat
                : 0f;
            float heatFlicker = heat > 0.8f
                ? Mathf.Lerp(1f, 0.72f + 0.28f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 47f)),
                    Mathf.InverseLerp(0.8f, 1f, heat))
                : 1f;
            float pulse = (0.92f + 0.08f * Mathf.Sin(
                (Time.unscaledTime * 31f) + index * 1.7f
            )) * heatFlicker;
            float length = jet.MaximumLength * clamped * pulse;
            Vector3 origin = jet.Anchor.position;
            jet.Outer.SetPosition(0, origin);
            jet.Outer.SetPosition(1, origin + direction * length);
            jet.Core.SetPosition(0, origin);
            jet.Core.SetPosition(1, origin + direction * length * 0.62f);

            Color coldOuter = new Color(0.08f, 0.62f, 1f);
            Color hotOuter = new Color(1f, 0.22f, 0.025f);
            Color coldCore = new Color(0.88f, 0.98f, 1f);
            Color hotCore = new Color(1f, 0.86f, 0.24f);
            Color outerStart = Color.Lerp(coldOuter, hotOuter, heat);
            outerStart.a = 0.9f * clamped;
            Color outerEnd = Color.Lerp(new Color(0.02f, 0.18f, 1f), Color.red, heat);
            outerEnd.a = 0f;
            Color coreStart = Color.Lerp(coldCore, hotCore, heat);
            coreStart.a = clamped;
            Color coreEnd = Color.Lerp(new Color(0.22f, 0.72f, 1f), new Color(1f, 0.35f, 0.02f), heat);
            coreEnd.a = 0f;
            jet.Outer.startColor = outerStart;
            jet.Outer.endColor = outerEnd;
            jet.Core.startColor = coreStart;
            jet.Core.endColor = coreEnd;
            if (jet.Glow != null)
            {
                jet.Glow.enabled = true;
                jet.Glow.color = Color.Lerp(new Color(0.3f, 0.78f, 1f), new Color(1f, 0.18f, 0.02f), heat);
                jet.Glow.intensity = 3.2f * clamped;
            }
        }
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null)
        {
            return null;
        }
        if (root.name.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }
        for (int index = 0; index < root.childCount; index++)
        {
            Transform result = FindChildRecursive(root.GetChild(index), name);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private void OnDestroy()
    {
        if (jetMaterial == null)
        {
            return;
        }
        if (Application.isPlaying)
        {
            Destroy(jetMaterial);
        }
        else
        {
            DestroyImmediate(jetMaterial);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        sprintIntensity = Mathf.Clamp01(sprintIntensity);
        flightIntensity = Mathf.Clamp01(flightIntensity);
        boostIntensity = Mathf.Clamp01(boostIntensity);
        ignitionSharpness = Mathf.Max(0f, ignitionSharpness);
        releaseSharpness = Mathf.Max(0f, releaseSharpness);
        backpackMaximumLength = Mathf.Max(0.01f, backpackMaximumLength);
        bootMaximumLength = Mathf.Max(0.01f, bootMaximumLength);
        outerWidth = Mathf.Max(0.001f, outerWidth);
        coreWidth = Mathf.Max(0.001f, coreWidth);
    }
#endif

    private sealed class ThrusterJet
    {
        public ThrusterJet(
            GameObject host,
            Transform anchor,
            LineRenderer outer,
            LineRenderer core,
            Light glow,
            bool isBoot,
            float maximumLength
        )
        {
            Host = host;
            Anchor = anchor;
            Outer = outer;
            Core = core;
            Glow = glow;
            IsBoot = isBoot;
            MaximumLength = maximumLength;
        }

        public GameObject Host { get; }
        public Transform Anchor { get; }
        public LineRenderer Outer { get; }
        public LineRenderer Core { get; }
        public Light Glow { get; }
        public bool IsBoot { get; }
        public float MaximumLength { get; }

        public void SetEnabled(bool enabled)
        {
            Outer.enabled = enabled;
            Core.enabled = enabled;
            if (!enabled && Glow != null)
            {
                Glow.enabled = false;
                Glow.intensity = 0f;
            }
        }

        public void DestroyImmediateSafe()
        {
            if (Host == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(Host);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(Host);
            }
        }
    }
}

public static class PowerSuitThrusterMath
{
    public static float ResolveTargetIntensity(
        bool isRunning,
        bool isFlying,
        bool isBoosting,
        float sprintIntensity,
        float flightIntensity,
        float boostIntensity
    )
    {
        if (isBoosting && isFlying)
        {
            return Sanitize01(boostIntensity);
        }
        if (isFlying)
        {
            return Sanitize01(flightIntensity);
        }
        return isRunning ? Sanitize01(sprintIntensity) : 0f;
    }

    private static float Sanitize01(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value)
            ? 0f
            : Mathf.Clamp01(value);
    }
}
