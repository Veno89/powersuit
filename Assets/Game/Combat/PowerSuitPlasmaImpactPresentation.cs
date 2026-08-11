using UnityEngine;

/// <summary>
/// Cached presentation for the Heavy Plasma Cannon's radial impact. The
/// expanding rings show the authoritative splash radius without performing
/// damage queries or owning projectile lifetime.
/// </summary>
[DisallowMultipleComponent]
public sealed class PowerSuitPlasmaImpactPresentation : MonoBehaviour
{
    [SerializeField] private Transform ringRoot;
    [SerializeField] private LineRenderer[] rings = System.Array.Empty<LineRenderer>();
    [SerializeField] private Light impactLight;
    [SerializeField, Min(0.01f)] private float durationSeconds = 0.75f;
    [SerializeField, Min(0.1f)] private float radius = 5.5f;
    [SerializeField] private Color color = new Color(0.72f, 0.2f, 1f, 1f);
    [SerializeField, Min(0f)] private float peakLightIntensity = 16f;

    private float elapsed;

    public float Radius => radius;
    public int RingCount => rings?.Length ?? 0;

    private void OnEnable()
    {
        elapsed = 0f;
        Apply(0f);
    }

    private void Update()
    {
        elapsed = Mathf.Min(durationSeconds, elapsed + Time.deltaTime);
        Apply(elapsed / durationSeconds);
    }

    public void Configure(
        Transform authoredRingRoot,
        LineRenderer[] authoredRings,
        Light authoredLight,
        float authoredDuration,
        float authoredRadius,
        Color authoredColor,
        float authoredPeakLightIntensity
    )
    {
        ringRoot = authoredRingRoot;
        rings = authoredRings != null
            ? (LineRenderer[])authoredRings.Clone()
            : System.Array.Empty<LineRenderer>();
        impactLight = authoredLight;
        durationSeconds = Mathf.Max(0.01f, authoredDuration);
        radius = Mathf.Max(0.1f, authoredRadius);
        color = authoredColor;
        peakLightIntensity = Mathf.Max(0f, authoredPeakLightIntensity);
        Apply(0f);
    }

    private void Apply(float normalized)
    {
        float t = Mathf.Clamp01(normalized);
        float expansion = 1f - Mathf.Pow(1f - t, 3f);
        float alpha = 1f - t;
        if (ringRoot != null)
        {
            ringRoot.localScale = Vector3.one * (radius * expansion);
        }

        Color faded = new Color(color.r, color.g, color.b, color.a * alpha);
        if (rings != null)
        {
            for (int index = 0; index < rings.Length; index++)
            {
                LineRenderer ring = rings[index];
                if (ring == null)
                {
                    continue;
                }
                ring.startColor = faded;
                ring.endColor = faded;
                ring.widthMultiplier = Mathf.Lerp(0.08f, 0.018f, t) /
                    Mathf.Max(0.1f, radius * Mathf.Max(0.05f, expansion));
            }
        }

        if (impactLight != null)
        {
            impactLight.color = color;
            impactLight.range = radius * 1.35f;
            impactLight.intensity = peakLightIntensity * alpha * alpha;
        }
    }
}
