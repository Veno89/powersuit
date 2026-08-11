using UnityEngine;

public sealed class ReticleHitMarker : MonoBehaviour
{
    private static ReticleHitMarker instance;
    public static ReticleHitMarker Instance => instance;

    [SerializeField] private Color markerColor = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField] private Color killMarkerColor = new Color(1f, 0.82f, 0.2f, 1f);
    [SerializeField] private Color criticalMarkerColor = new Color(0.25f, 0.95f, 1f, 1f);
    [SerializeField] private float displayDuration = 0.15f;
    [SerializeField] private float killDisplayDuration = 0.28f;
    [SerializeField] private float startSize = 10f;
    [SerializeField] private float endSize = 20f;
    [SerializeField] private float lineThickness = 2.5f;

    private float timer;
    private float activeDuration;
    private bool showingKill;
    private bool showingCritical;
    private PowerSuitController controller;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        controller = GetComponent<PowerSuitController>();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public static void ShowHitMarker(bool wasKilled = false, bool wasCritical = false)
    {
        if (instance == null)
        {
            PowerSuitController p = FindAnyObjectByType<PowerSuitController>();
            if (p != null)
            {
                instance = p.gameObject.GetComponent<ReticleHitMarker>() ?? p.gameObject.AddComponent<ReticleHitMarker>();
            }
        }

        if (instance != null)
        {
            instance.TriggerHitMarker(wasKilled, wasCritical);
        }
    }

    public static void ShowKillMarker()
    {
        ShowHitMarker(true);
    }

    public void TriggerHitMarker(bool wasKilled = false, bool wasCritical = false)
    {
        showingKill = wasKilled;
        showingCritical = wasCritical;
        activeDuration = wasKilled
            ? Mathf.Max(displayDuration, killDisplayDuration)
            : displayDuration;
        timer = activeDuration;
    }

    private void Update()
    {
        if (timer > 0f)
        {
            timer -= Time.deltaTime;
        }
    }

    private void OnGUI()
    {
        if (timer <= 0f)
        {
            return;
        }

        if (controller == null)
        {
            controller = GetComponent<PowerSuitController>() ?? FindAnyObjectByType<PowerSuitController>();
        }

        Vector2 reticlePos = (controller != null && controller.IsAiming)
            ? controller.ReticleScreenPosition
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        float guiX = reticlePos.x;
        float guiY = Screen.height - reticlePos.y;

        float progress = 1f - (timer / Mathf.Max(0.001f, activeDuration));
        float sizeMultiplier = showingKill ? 1.45f : showingCritical ? 1.22f : 1f;
        float currentSize = Mathf.Lerp(startSize, endSize, progress) * sizeMultiplier;
        float alpha = Mathf.Clamp01(1f - progress);

        Color c = showingKill
            ? killMarkerColor
            : showingCritical
                ? criticalMarkerColor
                : markerColor;
        c.a = alpha;

        Color savedColor = GUI.color;
        GUI.color = c;

        float halfSize = currentSize * 0.5f;
        float gap = 3f;

        // Render hit marker lines (diagonal X shape around reticle)
        DrawLine(new Vector2(guiX - gap - halfSize, guiY - gap - halfSize), new Vector2(guiX - gap, guiY - gap), lineThickness);
        DrawLine(new Vector2(guiX + gap + halfSize, guiY - gap - halfSize), new Vector2(guiX + gap, guiY - gap), lineThickness);
        DrawLine(new Vector2(guiX - gap - halfSize, guiY + gap + halfSize), new Vector2(guiX - gap, guiY + gap), lineThickness);
        DrawLine(new Vector2(guiX + gap + halfSize, guiY + gap + halfSize), new Vector2(guiX + gap, guiY + gap), lineThickness);

        if (showingKill || showingCritical)
        {
            float cardinal = currentSize * 0.72f;
            float inner = gap + 1f;
            DrawLine(new Vector2(guiX, guiY - cardinal), new Vector2(guiX, guiY - inner), lineThickness);
            DrawLine(new Vector2(guiX, guiY + cardinal), new Vector2(guiX, guiY + inner), lineThickness);
            DrawLine(new Vector2(guiX - cardinal, guiY), new Vector2(guiX - inner, guiY), lineThickness);
            DrawLine(new Vector2(guiX + cardinal, guiY), new Vector2(guiX + inner, guiY), lineThickness);
        }

        GUI.color = savedColor;
    }

    private static void DrawLine(Vector2 pointA, Vector2 pointB, float width)
    {
        Matrix4x4 savedMatrix = GUI.matrix;
        Vector2 d = pointB - pointA;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        float length = d.magnitude;

        GUIUtility.RotateAroundPivot(angle, pointA);
        GUI.DrawTexture(new Rect(pointA.x, pointA.y - width * 0.5f, length, width), Texture2D.whiteTexture);
        GUI.matrix = savedMatrix;
    }
}
