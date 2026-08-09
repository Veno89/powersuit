using UnityEngine;

public sealed class PoweredSuitDemoInstructions : MonoBehaviour
{
    private readonly Rect panelRect = new Rect(18f, 18f, 470f, 198f);

    private void OnGUI()
    {
        GUILayout.BeginArea(panelRect, GUI.skin.box);
        GUILayout.Label("POWERED SUIT COMBAT AND ANIMATION DEMO");
        GUILayout.Label("W: forward    S: backpedal    A/D: move    Mouse: look");
        GUILayout.Label("Right Mouse: over-shoulder aim    Left Mouse: fire");
        GUILayout.Label("Q: draw / stow rifle    R: reload magazine");
        GUILayout.Label("Space: jump / ascend");
        GUILayout.Label("F: toggle flight    Ctrl/C: descend    Shift: boost");
        GUILayout.Label("Esc: release cursor    Click: capture cursor");
        GUILayout.Label("The precision rifle cycles its bolt after each accepted shot.");
        GUILayout.EndArea();
    }
}
