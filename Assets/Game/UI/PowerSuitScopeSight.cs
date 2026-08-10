using System;
using System.Collections.Generic;
using Powersuit.Combat;
using UnityEngine;

/// <summary>
/// Reversible, screen-space precision-scope presentation. It keeps the optic's
/// decorative meshes out of the player camera while the camera is inside the
/// ocular housing and draws an aspect-safe reticle aligned to the gameplay ray.
/// </summary>
[DisallowMultipleComponent]
public sealed class PowerSuitScopeSight : MonoBehaviour
{
    private const int MaskTextureSize = 512;
    private const float MinimumVisibleBlend = 0.01f;
    private const float SightDiameterScreenFraction = 0.92f;
    private static readonly float[] RangeStadiaOffsets =
        { 0.16f, 0.25f, 0.35f, 0.46f, 0.58f };

    [SerializeField] private Color reticleColor =
        new Color(0.82f, 0.96f, 1f, 0.96f);
    [SerializeField] private Color shadowColor =
        new Color(0f, 0f, 0f, 0.72f);

    private readonly List<RendererState> hiddenRenderers =
        new List<RendererState>();

    private PowerSuitController controller;
    private PowerSuitWeapon weapon;
    private Transform cachedScopePoint;
    private Texture2D scopeMask;
    private bool opticRenderersHidden;

    public bool IsScopeEligible =>
        weapon != null &&
        weapon.Definition != null &&
        weapon.Definition.SupportsScope;

    public void Bind(PowerSuitController ownerController, PowerSuitWeapon ownerWeapon)
    {
        RestoreOpticRenderers();
        controller = ownerController;
        weapon = ownerWeapon;
        cachedScopePoint = null;
        CacheOpticRenderers();
        if (Application.isPlaying && IsScopeEligible)
        {
            GetOrCreateScopeMask();
        }
    }

    private void Awake()
    {
        if (controller == null)
        {
            controller = GetComponent<PowerSuitController>();
        }

        if (weapon == null)
        {
            weapon = GetComponent<PowerSuitWeapon>();
        }

        // Build/upload the reusable mask during player initialization so the
        // first V press does not allocate or upload a texture mid-combat.
        if (Application.isPlaying && IsScopeEligible)
        {
            GetOrCreateScopeMask();
        }
    }

    private void LateUpdate()
    {
        if (controller == null || weapon == null)
        {
            return;
        }

        if (cachedScopePoint != controller.ScopePoint)
        {
            RestoreOpticRenderers();
            CacheOpticRenderers();
        }

        bool shouldHide = IsScopeEligible &&
                          (controller.IsScoped ||
                           controller.ScopeBlend > MinimumVisibleBlend);
        SetOpticRenderersHidden(shouldHide);
    }

    private void OnDisable()
    {
        RestoreOpticRenderers();
    }

    private void OnDestroy()
    {
        RestoreOpticRenderers();
        if (scopeMask != null)
        {
            if (Application.isPlaying)
            {
                Destroy(scopeMask);
            }
            else
            {
                DestroyImmediate(scopeMask);
            }
            scopeMask = null;
        }
    }

    private void OnGUI()
    {
        if (!IsScopeEligible || controller == null)
        {
            return;
        }

        float blend = Mathf.Clamp01(controller.ScopeBlend);
        if (!controller.IsScoped && blend <= MinimumVisibleBlend)
        {
            return;
        }

        float alpha = Mathf.SmoothStep(0f, 1f, blend);
        Vector2 screenCenter = controller.ReticleScreenPosition;
        Vector2 guiCenter = new Vector2(
            screenCenter.x,
            Screen.height - screenCenter.y
        );
        float diameter = Mathf.Max(
            1f,
            Mathf.Min(Screen.width, Screen.height) * SightDiameterScreenFraction
        );
        float radius = diameter * 0.5f;
        Rect sightRect = new Rect(
            guiCenter.x - radius,
            guiCenter.y - radius,
            diameter,
            diameter
        );

        Color savedColor = GUI.color;
        DrawOutsideMask(sightRect, alpha);

        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(sightRect, GetOrCreateScopeMask(), ScaleMode.StretchToFill, true);

        DrawReticle(guiCenter, radius, alpha);
        GUI.color = savedColor;
    }

    private void CacheOpticRenderers()
    {
        hiddenRenderers.Clear();
        cachedScopePoint = controller != null ? controller.ScopePoint : null;
        if (cachedScopePoint == null)
        {
            return;
        }

        Transform rifleRoot = cachedScopePoint.parent;
        while (rifleRoot != null &&
               !rifleRoot.name.Equals("RifleRoot", StringComparison.OrdinalIgnoreCase))
        {
            rifleRoot = rifleRoot.parent;
        }

        Transform searchRoot = rifleRoot != null
            ? rifleRoot
            : cachedScopePoint.root;
        Renderer[] renderers = searchRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer opticRenderer in renderers)
        {
            if (!IsOpticRenderer(opticRenderer))
            {
                continue;
            }

            hiddenRenderers.Add(
                new RendererState(opticRenderer, opticRenderer.enabled)
            );
        }
    }

    private static bool IsOpticRenderer(Renderer candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        string objectName = candidate.gameObject.name;
        return objectName.StartsWith(
                   "Rifle_Scope",
                   StringComparison.OrdinalIgnoreCase
               ) ||
               objectName.Equals(
                   "Rifle_SightOcular",
                   StringComparison.OrdinalIgnoreCase
               );
    }

    private void SetOpticRenderersHidden(bool hidden)
    {
        if (opticRenderersHidden == hidden)
        {
            return;
        }

        opticRenderersHidden = hidden;
        foreach (RendererState state in hiddenRenderers)
        {
            if (state.Renderer != null)
            {
                state.Renderer.enabled = hidden
                    ? false
                    : state.WasEnabled;
            }
        }
    }

    private void RestoreOpticRenderers()
    {
        if (hiddenRenderers.Count == 0)
        {
            opticRenderersHidden = false;
            return;
        }

        foreach (RendererState state in hiddenRenderers)
        {
            if (state.Renderer != null)
            {
                state.Renderer.enabled = state.WasEnabled;
            }
        }

        opticRenderersHidden = false;
    }

    private Texture2D GetOrCreateScopeMask()
    {
        if (scopeMask != null)
        {
            return scopeMask;
        }

        scopeMask = new Texture2D(
            MaskTextureSize,
            MaskTextureSize,
            TextureFormat.RGBA32,
            false,
            true
        )
        {
            name = "Runtime Precision Scope Mask",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32 transparent = new Color32(0, 0, 0, 0);
        Color32 blackout = new Color32(0, 0, 0, 255);
        Color32 rim = new Color32(2, 7, 10, 245);
        Color32[] pixels = new Color32[MaskTextureSize * MaskTextureSize];
        float center = (MaskTextureSize - 1) * 0.5f;
        float outerRadius = MaskTextureSize * 0.5f;
        float innerRadius = outerRadius - 12f;
        float innerSquared = innerRadius * innerRadius;
        float outerSquared = outerRadius * outerRadius;

        for (int y = 0; y < MaskTextureSize; y++)
        {
            float dy = y - center;
            for (int x = 0; x < MaskTextureSize; x++)
            {
                float dx = x - center;
                float distanceSquared = (dx * dx) + (dy * dy);
                pixels[(y * MaskTextureSize) + x] =
                    distanceSquared > outerSquared
                        ? blackout
                        : distanceSquared > innerSquared
                            ? rim
                            : transparent;
            }
        }

        scopeMask.SetPixels32(pixels);
        scopeMask.Apply(false, true);
        return scopeMask;
    }

    private static void DrawOutsideMask(Rect sightRect, float alpha)
    {
        Color black = new Color(0f, 0f, 0f, alpha);
        GUI.color = black;
        DrawSolidRect(new Rect(0f, 0f, Screen.width, Mathf.Max(0f, sightRect.yMin)));
        DrawSolidRect(
            new Rect(
                0f,
                Mathf.Min(Screen.height, sightRect.yMax),
                Screen.width,
                Mathf.Max(0f, Screen.height - sightRect.yMax)
            )
        );
        DrawSolidRect(
            new Rect(
                0f,
                Mathf.Max(0f, sightRect.yMin),
                Mathf.Max(0f, sightRect.xMin),
                Mathf.Min(Screen.height, sightRect.height)
            )
        );
        DrawSolidRect(
            new Rect(
                Mathf.Min(Screen.width, sightRect.xMax),
                Mathf.Max(0f, sightRect.yMin),
                Mathf.Max(0f, Screen.width - sightRect.xMax),
                Mathf.Min(Screen.height, sightRect.height)
            )
        );
    }

    private void DrawReticle(Vector2 center, float radius, float alpha)
    {
        float scale = Mathf.Clamp(radius / 500f, 0.65f, 1.4f);
        float thickness = Mathf.Max(1.75f, 2f * scale);
        float centreGap = 7f * scale;
        float armLength = radius * 0.78f;

        GUI.color = new Color(
            shadowColor.r,
            shadowColor.g,
            shadowColor.b,
            shadowColor.a * alpha
        );
        DrawCrossArms(center + Vector2.one, centreGap, armLength, thickness + 2f);

        GUI.color = new Color(
            reticleColor.r,
            reticleColor.g,
            reticleColor.b,
            reticleColor.a * alpha
        );
        DrawCrossArms(center, centreGap, armLength, thickness);
        float centreDotSize = Mathf.Max(3f, 4f * scale);
        DrawSolidRect(
            new Rect(
                center.x - (centreDotSize * 0.5f),
                center.y - (centreDotSize * 0.5f),
                centreDotSize,
                centreDotSize
            )
        );

        DrawMilTicks(center, radius, thickness, scale);
        DrawRangeStadia(center, radius, thickness, scale);
    }

    private static void DrawCrossArms(
        Vector2 center,
        float gap,
        float length,
        float thickness
    )
    {
        DrawLine(center.x - length, center.y, center.x - gap, center.y, thickness);
        DrawLine(center.x + gap, center.y, center.x + length, center.y, thickness);
        DrawLine(center.x, center.y - length, center.x, center.y - gap, thickness);
        DrawLine(center.x, center.y + gap, center.x, center.y + length, thickness);
    }

    private static void DrawMilTicks(
        Vector2 center,
        float radius,
        float thickness,
        float scale
    )
    {
        float spacing = radius * 0.085f;
        for (int index = 1; index <= 7; index++)
        {
            float offset = spacing * index;
            float tick = (index % 2 == 0 ? 10f : 6f) * scale;
            DrawLine(
                center.x - offset,
                center.y - tick * 0.5f,
                center.x - offset,
                center.y + tick * 0.5f,
                thickness
            );
            DrawLine(
                center.x + offset,
                center.y - tick * 0.5f,
                center.x + offset,
                center.y + tick * 0.5f,
                thickness
            );
        }
    }

    private static void DrawRangeStadia(
        Vector2 center,
        float radius,
        float thickness,
        float scale
    )
    {
        for (int index = 0; index < RangeStadiaOffsets.Length; index++)
        {
            float y = center.y + (radius * RangeStadiaOffsets[index]);
            float halfWidth = (34f - (index * 4f)) * scale;
            DrawLine(
                center.x - halfWidth,
                y,
                center.x + halfWidth,
                y,
                thickness
            );
        }
    }

    private static void DrawLine(
        float x1,
        float y1,
        float x2,
        float y2,
        float thickness
    )
    {
        if (Mathf.Abs(y2 - y1) < 0.01f)
        {
            DrawSolidRect(
                new Rect(
                    Mathf.Min(x1, x2),
                    y1 - (thickness * 0.5f),
                    Mathf.Abs(x2 - x1),
                    thickness
                )
            );
            return;
        }

        DrawSolidRect(
            new Rect(
                x1 - (thickness * 0.5f),
                Mathf.Min(y1, y2),
                thickness,
                Mathf.Abs(y2 - y1)
            )
        );
    }

    private static void DrawSolidRect(Rect rect)
    {
        if (rect.width <= 0f || rect.height <= 0f)
        {
            return;
        }

        GUI.DrawTexture(rect, Texture2D.whiteTexture);
    }

    private readonly struct RendererState
    {
        public RendererState(Renderer renderer, bool wasEnabled)
        {
            Renderer = renderer;
            WasEnabled = wasEnabled;
        }

        public Renderer Renderer { get; }
        public bool WasEnabled { get; }
    }
}
