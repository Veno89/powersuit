using UnityEngine;

public sealed class PoweredSuitDemoInstructions : MonoBehaviour
{
    private readonly Rect panelRect = new Rect(18f, 18f, 430f, 164f);

    private void OnGUI()
    {
        GUILayout.BeginArea(panelRect, GUI.skin.box);
        GUILayout.Label("POWERED SUIT - GENERATOR 109 DEMO");
        GUILayout.Label("WASD: move    Mouse: look    Space: jump / ascend");
        GUILayout.Label("Right Mouse: over-shoulder aim    Left Mouse: fire");
        GUILayout.Label("F: toggle flight    Ctrl/C: descend    Shift: boost");
        GUILayout.Label("Esc: release cursor    Click: capture cursor");
        GUILayout.Label("The cyan rifle muzzle is the real imported Rifle_Muzzle helper.");
        GUILayout.EndArea();
    }
}
