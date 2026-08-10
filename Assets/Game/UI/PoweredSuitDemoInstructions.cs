using UnityEngine;

public sealed class PoweredSuitDemoInstructions : MonoBehaviour
{
    private readonly Rect panelRect = new Rect(18f, 18f, 540f, 292f);

    private void OnGUI()
    {
        GUILayout.BeginArea(panelRect, GUI.skin.box);
        GUILayout.Label("POWERED SUIT COMBAT AND ANIMATION DEMO");
        GUILayout.Label("W: forward    S: backpedal    A/D: move    Mouse: look");
        GUILayout.Label("Right Mouse: over-shoulder aim    Left Mouse: fire");
        GUILayout.Label("Q: draw / stow rifle    R: reload magazine");
        GUILayout.Label("Hold Right Mouse + V: toggle Precision Rifle scope");
        GUILayout.Label("G: shoulder rocket (explosive AOE)");
        GUILayout.Label("Hold / release E: lightning strike (targeted AOE)");
        GUILayout.Label("X: void ultimate when fully charged");
        GUILayout.Label("Tap Space: jump    Hold Space after jumping: enter flight");
        GUILayout.Label("Space: ascend    Ctrl/C: descend    Touch down: land");
        GUILayout.Label("Shift: sprint on ground / boost in flight");
        GUILayout.Label("`: developer console");
        GUILayout.Label("Esc: release cursor    Click: capture cursor");
        GUILayout.Label("The precision rifle cycles its bolt after each accepted shot.");
        GUILayout.EndArea();
    }
}
