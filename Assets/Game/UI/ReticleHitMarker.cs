using UnityEngine;

public sealed class ReticleHitMarker : MonoBehaviour
{
    private static ReticleHitMarker instance;
    public static ReticleHitMarker Instance => instance;

    [SerializeField] private Color markerColor = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField] private float displayDuration = 0.15f;
    [SerializeField] private float startSize = 10f;
    [SerializeField] private float endSize = 20f;
    [SerializeField] private float lineThickness = 2.5f;

    private float timer;
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

    public static void ShowHitMarker()
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
            instance.TriggerHitMarker();
        }
    }

    public void TriggerHitMarker()
    {
        timer = displayDuration;
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

        float progress = 1f - (timer / displayDuration);
        float currentSize = Mathf.Lerp(startSize, endSize, progress);
        float alpha = Mathf.Clamp01(1f - progress);

        Color c = markerColor;
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
