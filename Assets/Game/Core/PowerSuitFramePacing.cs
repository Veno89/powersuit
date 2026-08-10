using UnityEngine;

/// <summary>
/// Applies the desktop prototype's presentation policy without changing the
/// repository's global QualitySettings asset. A display at 60 Hz or faster is
/// synchronized to its refresh; slower/unsupported displays use the 60 FPS
/// fallback cap.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public sealed class PowerSuitFramePacing : MonoBehaviour
{
    [SerializeField] private bool runInBackground = true;
    [SerializeField] private bool synchronizeToDisplay = true;
    [SerializeField, Min(30)] private int fallbackTargetFrameRate = 60;

    public bool RunInBackground => runInBackground;
    public bool SynchronizeToDisplay => synchronizeToDisplay;
    public int FallbackTargetFrameRate => fallbackTargetFrameRate;

    private void Awake()
    {
        ApplyNow();
    }

    public void ApplyNow()
    {
        int targetFrameRate = Mathf.Max(30, fallbackTargetFrameRate);
        double refreshRate = Screen.currentResolution.refreshRateRatio.value;
        bool useVSync = PowerSuitFramePacingPolicy.ShouldUseVSync(
            synchronizeToDisplay,
            refreshRate,
            targetFrameRate
        );

        Application.runInBackground = runInBackground;
        QualitySettings.vSyncCount = useVSync ? 1 : 0;

        // Unity ignores targetFrameRate on desktop while VSync is active. It
        // remains a useful fallback in the Editor, on mobile, or when display
        // synchronization is unavailable.
        Application.targetFrameRate = targetFrameRate;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        fallbackTargetFrameRate = Mathf.Max(30, fallbackTargetFrameRate);
    }
#endif
}

public static class PowerSuitFramePacingPolicy
{
    public static bool ShouldUseVSync(
        bool synchronizeToDisplay,
        double displayRefreshRate,
        int fallbackTargetFrameRate
    )
    {
        if (
            !synchronizeToDisplay ||
            double.IsNaN(displayRefreshRate) ||
            double.IsInfinity(displayRefreshRate) ||
            displayRefreshRate <= 0d
        )
        {
            return false;
        }

        int fallback = Mathf.Max(30, fallbackTargetFrameRate);
        // Treat NTSC-style 59.94 Hz as 60 Hz without accidentally syncing a
        // genuinely sub-60 display.
        return displayRefreshRate >= fallback - 0.1d;
    }
}
