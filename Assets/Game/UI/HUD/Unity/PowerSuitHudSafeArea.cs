using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class PowerSuitHudSafeArea : MonoBehaviour
{
    private RectTransform target;
    private Rect appliedSafeArea;
    private int appliedWidth;
    private int appliedHeight;

    private void Awake()
    {
        target = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        ApplyCurrentSafeArea(force: true);
    }

    private void LateUpdate()
    {
        ApplyCurrentSafeArea(force: false);
    }

    public void ApplyCurrentSafeArea(bool force = false)
    {
        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);
        Rect safeArea = Screen.safeArea;
        if (
            !force &&
            width == appliedWidth &&
            height == appliedHeight &&
            safeArea == appliedSafeArea
        )
        {
            return;
        }

        if (target == null)
        {
            target = GetComponent<RectTransform>();
        }

        Rect anchors = CalculateNormalizedSafeArea(safeArea, width, height);
        target.anchorMin = anchors.min;
        target.anchorMax = anchors.max;
        target.offsetMin = Vector2.zero;
        target.offsetMax = Vector2.zero;

        appliedSafeArea = safeArea;
        appliedWidth = width;
        appliedHeight = height;
    }

    public static Rect CalculateNormalizedSafeArea(
        Rect safeArea,
        int screenWidth,
        int screenHeight
    )
    {
        float width = Mathf.Max(1, screenWidth);
        float height = Mathf.Max(1, screenHeight);
        float xMin = Mathf.Clamp01(safeArea.xMin / width);
        float yMin = Mathf.Clamp01(safeArea.yMin / height);
        float xMax = Mathf.Clamp01(safeArea.xMax / width);
        float yMax = Mathf.Clamp01(safeArea.yMax / height);

        if (xMax < xMin)
        {
            xMax = xMin;
        }
        if (yMax < yMin)
        {
            yMax = yMin;
        }

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }
}
