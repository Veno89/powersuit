using System;
using Powersuit.Combat;
using UnityEngine;

/// <summary>
/// Selects the authored receiver for the equipped weapon and adds a small,
/// data-driven visual kick to the active generated receiver. Gameplay hardpoints remain
/// on the animated carrier rig, so presentation never changes ballistics.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-70)]
public sealed class PowerSuitWeaponVisualController : MonoBehaviour
{
    [SerializeField] private PowerSuitWeapon weapon;
    [SerializeField] private PowerSuitController controller;
    [SerializeField] private Renderer[] precisionRenderers =
        Array.Empty<Renderer>();
    [SerializeField] private bool[] precisionRendererDefaults =
        Array.Empty<bool>();
    [SerializeField] private Renderer[] assaultRenderers =
        Array.Empty<Renderer>();
    [SerializeField] private bool[] assaultRendererDefaults =
        Array.Empty<bool>();
    [SerializeField] private Renderer[] heavyRenderers =
        Array.Empty<Renderer>();
    [SerializeField] private bool[] heavyRendererDefaults =
        Array.Empty<bool>();
    [SerializeField] private Transform assaultFeedbackRoot;
    [SerializeField] private Transform heavyFeedbackRoot;
    [SerializeField, Min(0.01f)] private float recoilRecoverySharpness = 24f;

    private PowerSuitScopeSight scopeSight;
    private Vector3 feedbackBasePosition;
    private Quaternion feedbackBaseRotation = Quaternion.identity;
    private Vector3 heavyFeedbackBasePosition;
    private Quaternion heavyFeedbackBaseRotation = Quaternion.identity;
    private float recoilAmount;
    private float recoilYawSign = 1f;
    private bool subscribed;

    public int PrecisionRendererCount => precisionRenderers?.Length ?? 0;
    public int AssaultRendererCount => assaultRenderers?.Length ?? 0;
    public int HeavyRendererCount => heavyRenderers?.Length ?? 0;
    public bool IsAssaultVisualActive { get; private set; }
    public bool IsHeavyVisualActive { get; private set; }
    public float RecoilAmount => recoilAmount;
    public Transform AssaultFeedbackRoot => assaultFeedbackRoot;
    public Transform HeavyFeedbackRoot => heavyFeedbackRoot;

    private void Awake()
    {
        ResolveDependencies();
        CaptureFeedbackBaseline();
        ApplyWeaponVisual(weapon != null ? weapon.Definition : null);
    }

    private void OnEnable()
    {
        ResolveDependencies();
        Subscribe();
        ApplyWeaponVisual(weapon != null ? weapon.Definition : null);
    }

    private void LateUpdate()
    {
        Transform activeFeedbackRoot = IsHeavyVisualActive
            ? heavyFeedbackRoot
            : assaultFeedbackRoot;
        if (activeFeedbackRoot == null)
        {
            return;
        }

        float sharpness = Mathf.Max(0.01f, recoilRecoverySharpness);
        recoilAmount *= Mathf.Exp(-sharpness * Time.deltaTime);
        if (recoilAmount < 0.001f)
        {
            recoilAmount = 0f;
        }

        WeaponDefinition definition = weapon != null ? weapon.Definition : null;
        float distance = definition != null
            ? definition.VisualRecoilDistance
            : 0f;
        float degrees = definition != null
            ? definition.VisualRecoilDegrees
            : 0f;
        Vector3 basePosition = IsHeavyVisualActive
            ? heavyFeedbackBasePosition
            : feedbackBasePosition;
        Quaternion baseRotation = IsHeavyVisualActive
            ? heavyFeedbackBaseRotation
            : feedbackBaseRotation;
        activeFeedbackRoot.localPosition =
            basePosition + Vector3.back * (distance * recoilAmount);
        activeFeedbackRoot.localRotation = baseRotation *
            Quaternion.Euler(
                -degrees * recoilAmount,
                degrees * 0.18f * recoilAmount * recoilYawSign,
                0f
            );
    }

    private void OnDisable()
    {
        Unsubscribe();
        RestoreFeedbackBaseline();
    }

    public void Configure(
        PowerSuitWeapon ownerWeapon,
        PowerSuitController ownerController,
        Renderer[] authoredPrecisionRenderers,
        Renderer[] authoredAssaultRenderers,
        Transform feedbackRoot,
        Renderer[] authoredHeavyRenderers,
        Transform authoredHeavyFeedbackRoot
    )
    {
        weapon = ownerWeapon;
        controller = ownerController;
        precisionRenderers = CloneRenderers(authoredPrecisionRenderers);
        assaultRenderers = CloneRenderers(authoredAssaultRenderers);
        precisionRendererDefaults = CaptureDefaults(precisionRenderers);
        assaultRendererDefaults = CaptureDefaults(assaultRenderers);
        heavyRenderers = CloneRenderers(authoredHeavyRenderers);
        heavyRendererDefaults = CaptureDefaults(heavyRenderers);
        assaultFeedbackRoot = feedbackRoot;
        heavyFeedbackRoot = authoredHeavyFeedbackRoot;
        CaptureFeedbackBaseline();
        ApplyWeaponVisual(weapon != null ? weapon.Definition : null);
    }

    public void ApplyWeaponVisual(WeaponDefinition definition)
    {
        bool useAssault =
            definition != null &&
            definition.WeaponClass == WeaponClass.AssaultRifle;
        bool useHeavy =
            definition != null &&
            definition.WeaponClass == WeaponClass.HeavyWeapon;
        IsAssaultVisualActive = useAssault;
        IsHeavyVisualActive = useHeavy;

        ApplyRendererSet(
            precisionRenderers,
            precisionRendererDefaults,
            visible: !useAssault && !useHeavy
        );
        ApplyRendererSet(
            assaultRenderers,
            assaultRendererDefaults,
            visible: useAssault
        );
        ApplyRendererSet(
            heavyRenderers,
            heavyRendererDefaults,
            visible: useHeavy
        );

        recoilAmount = 0f;
        RestoreFeedbackBaseline();
    }

    private void ResolveDependencies()
    {
        weapon ??= GetComponent<PowerSuitWeapon>();
        controller ??= GetComponent<PowerSuitController>();
        scopeSight ??= GetComponent<PowerSuitScopeSight>();
    }

    private void Subscribe()
    {
        if (subscribed || weapon == null)
        {
            return;
        }

        weapon.WeaponEquipped += OnWeaponEquipped;
        weapon.ShotAccepted += OnShotAccepted;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || weapon == null)
        {
            subscribed = false;
            return;
        }

        weapon.WeaponEquipped -= OnWeaponEquipped;
        weapon.ShotAccepted -= OnShotAccepted;
        subscribed = false;
    }

    private void OnWeaponEquipped(WeaponDefinition definition)
    {
        ApplyWeaponVisual(definition);

        // PowerSuitWeapon binds the scope presenter before broadcasting this
        // event. Rebind after receiver visibility changes so scoped restore
        // remembers the newly equipped receiver rather than the old one.
        scopeSight ??= GetComponent<PowerSuitScopeSight>();
        scopeSight?.Bind(controller, weapon);
    }

    private void OnShotAccepted(WeaponFireResult result)
    {
        if ((!IsAssaultVisualActive && !IsHeavyVisualActive) || !result.Fired)
        {
            return;
        }

        recoilAmount = Mathf.Min(
            1f,
            recoilAmount + (IsHeavyVisualActive ? 1f : 0.72f)
        );
        recoilYawSign = -recoilYawSign;
    }

    private void CaptureFeedbackBaseline()
    {
        if (assaultFeedbackRoot == null)
        {
            feedbackBasePosition = Vector3.zero;
            feedbackBaseRotation = Quaternion.identity;
        }
        else
        {
            feedbackBasePosition = assaultFeedbackRoot.localPosition;
            feedbackBaseRotation = assaultFeedbackRoot.localRotation;
        }

        if (heavyFeedbackRoot == null)
        {
            heavyFeedbackBasePosition = Vector3.zero;
            heavyFeedbackBaseRotation = Quaternion.identity;
        }
        else
        {
            heavyFeedbackBasePosition = heavyFeedbackRoot.localPosition;
            heavyFeedbackBaseRotation = heavyFeedbackRoot.localRotation;
        }
    }

    private void RestoreFeedbackBaseline()
    {
        if (assaultFeedbackRoot != null)
        {
            assaultFeedbackRoot.localPosition = feedbackBasePosition;
            assaultFeedbackRoot.localRotation = feedbackBaseRotation;
        }

        if (heavyFeedbackRoot != null)
        {
            heavyFeedbackRoot.localPosition = heavyFeedbackBasePosition;
            heavyFeedbackRoot.localRotation = heavyFeedbackBaseRotation;
        }
    }

    private static Renderer[] CloneRenderers(Renderer[] source)
    {
        return source != null
            ? (Renderer[])source.Clone()
            : Array.Empty<Renderer>();
    }

    private static bool[] CaptureDefaults(Renderer[] renderers)
    {
        bool[] defaults = new bool[renderers?.Length ?? 0];
        for (int index = 0; index < defaults.Length; index++)
        {
            defaults[index] = renderers[index] != null &&
                renderers[index].enabled;
        }
        return defaults;
    }

    private static void ApplyRendererSet(
        Renderer[] renderers,
        bool[] defaults,
        bool visible
    )
    {
        if (renderers == null)
        {
            return;
        }

        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null)
            {
                continue;
            }

            bool authoredEnabled =
                defaults != null &&
                index < defaults.Length &&
                defaults[index];
            renderer.enabled = visible && authoredEnabled;
        }
    }
}
