using System;
using Powersuit.Combat;
using UnityEngine;

/// <summary>
/// Selects the authored receiver for the equipped weapon and adds a small,
/// data-driven visual kick to the automatic rifle. Gameplay hardpoints remain
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
    [SerializeField] private Transform assaultFeedbackRoot;
    [SerializeField, Min(0.01f)] private float recoilRecoverySharpness = 24f;

    private PowerSuitScopeSight scopeSight;
    private Vector3 feedbackBasePosition;
    private Quaternion feedbackBaseRotation = Quaternion.identity;
    private float recoilAmount;
    private float recoilYawSign = 1f;
    private bool subscribed;

    public int PrecisionRendererCount => precisionRenderers?.Length ?? 0;
    public int AssaultRendererCount => assaultRenderers?.Length ?? 0;
    public bool IsAssaultVisualActive { get; private set; }
    public float RecoilAmount => recoilAmount;
    public Transform AssaultFeedbackRoot => assaultFeedbackRoot;

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
        if (assaultFeedbackRoot == null)
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
        assaultFeedbackRoot.localPosition =
            feedbackBasePosition + Vector3.back * (distance * recoilAmount);
        assaultFeedbackRoot.localRotation = feedbackBaseRotation *
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
        Transform feedbackRoot
    )
    {
        weapon = ownerWeapon;
        controller = ownerController;
        precisionRenderers = CloneRenderers(authoredPrecisionRenderers);
        assaultRenderers = CloneRenderers(authoredAssaultRenderers);
        precisionRendererDefaults = CaptureDefaults(precisionRenderers);
        assaultRendererDefaults = CaptureDefaults(assaultRenderers);
        assaultFeedbackRoot = feedbackRoot;
        CaptureFeedbackBaseline();
        ApplyWeaponVisual(weapon != null ? weapon.Definition : null);
    }

    public void ApplyWeaponVisual(WeaponDefinition definition)
    {
        bool useAssault =
            definition != null &&
            definition.WeaponClass == WeaponClass.AssaultRifle;
        IsAssaultVisualActive = useAssault;

        ApplyRendererSet(
            precisionRenderers,
            precisionRendererDefaults,
            visible: !useAssault
        );
        ApplyRendererSet(
            assaultRenderers,
            assaultRendererDefaults,
            visible: useAssault
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
        if (!IsAssaultVisualActive || !result.Fired)
        {
            return;
        }

        recoilAmount = Mathf.Min(1f, recoilAmount + 0.72f);
        recoilYawSign = -recoilYawSign;
    }

    private void CaptureFeedbackBaseline()
    {
        if (assaultFeedbackRoot == null)
        {
            return;
        }

        feedbackBasePosition = assaultFeedbackRoot.localPosition;
        feedbackBaseRotation = assaultFeedbackRoot.localRotation;
    }

    private void RestoreFeedbackBaseline()
    {
        if (assaultFeedbackRoot == null)
        {
            return;
        }

        assaultFeedbackRoot.localPosition = feedbackBasePosition;
        assaultFeedbackRoot.localRotation = feedbackBaseRotation;
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
