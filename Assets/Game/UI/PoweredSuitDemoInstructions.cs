using UnityEngine;

public sealed class PoweredSuitDemoInstructions : MonoBehaviour
{
    private const float LineHeight = 18f;
    private const float Padding = 10f;

    private static readonly GUIContent[] Lines =
    {
        new GUIContent("POWERED SUIT COMBAT AND ANIMATION DEMO"),
        new GUIContent("W: forward    S: backpedal    A/D: move    Mouse: look"),
        new GUIContent("Right Mouse: over-shoulder aim    Left Mouse: fire"),
        new GUIContent("Q: draw / stow rifle    R: reload magazine"),
        new GUIContent("1 / 2 or Mouse Wheel: Precision / Assault Rifle"),
        new GUIContent("Hold Right Mouse + V: toggle Precision Rifle scope"),
        new GUIContent("G: shoulder rocket (explosive AOE)"),
        new GUIContent("Hold / release E: lightning strike (targeted AOE)"),
        new GUIContent("X: void ultimate when fully charged"),
        new GUIContent("Tap Space: jump    Hold Space after jumping: enter flight"),
        new GUIContent("Space: ascend    Ctrl/C: descend    Touch down: land"),
        new GUIContent("Shift: sprint on ground / boost in flight"),
        new GUIContent("`: developer console"),
        new GUIContent("Esc: release cursor    Click: capture cursor"),
        new GUIContent("The precision rifle cycles its bolt after each accepted shot.")
    };

    private readonly Rect panelRect = new Rect(
        18f,
        18f,
        540f,
        Padding * 2f + Lines.Length * LineHeight
    );
    private GUIStyle labelStyle;

    private void OnGUI()
    {
        labelStyle ??= GUI.skin.label;
        GUI.Box(panelRect, GUIContent.none);
        float x = panelRect.x + Padding;
        float y = panelRect.y + Padding;
        float width = panelRect.width - Padding * 2f;
        for (int index = 0; index < Lines.Length; index++)
        {
            GUI.Label(
                new Rect(x, y + index * LineHeight, width, LineHeight),
                Lines[index],
                labelStyle
            );
        }
    }
}
