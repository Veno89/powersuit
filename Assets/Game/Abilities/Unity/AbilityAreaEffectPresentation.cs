using System;
using UnityEngine;

namespace Powersuit.Abilities.UnityAdapters
{
    public enum AbilityAreaPresentationPhase
    {
        Hidden = 0,
        Targeting = 1,
        Telegraph = 2,
        Impact = 3
    }

    public enum AbilityAreaPresentationStyle
    {
        Rocket = 0,
        Lightning = 1,
        TargetValid = 2,
        TargetInvalid = 3
    }

    /// <summary>
    /// Lightweight procedural area feedback shared by targeting, rocket, and
    /// lightning adapters. It creates a few cached line renderers and one
    /// unshadowed light, so every pooled use is allocation-free after setup.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AbilityAreaEffectPresentation : MonoBehaviour
    {
        private const int CircleSegments = 64;
        private const int BoltSegments = 9;
        private const int BurstRayCount = 12;

        [SerializeField, Min(0.01f)] private float lineWidth = 0.105f;
        [SerializeField, Min(0f)] private float surfaceOffset = 0.055f;
        [SerializeField, Min(0.01f)] private float defaultImpactSeconds = 0.8f;
        [SerializeField] private bool createImpactLight = true;

        private LineRenderer boundaryRing;
        private LineRenderer energyRing;
        private LineRenderer radialBurst;
        private LineRenderer lightningBolt;
        private LineRenderer aftermathRing;
        private Light impactLight;
        private Material presentationMaterial;
        private AbilityAreaPresentationPhase phase;
        private AbilityAreaPresentationStyle style;
        private float radius;
        private float duration;
        private float elapsed;
        private bool isBuilt;

        public AbilityAreaPresentationPhase Phase => phase;
        public AbilityAreaPresentationStyle Style => style;
        public float Radius => radius;
        public float NormalizedTime => duration > 0f
            ? Mathf.Clamp01(elapsed / duration)
            : 1f;
        public bool IsVisible => phase != AbilityAreaPresentationPhase.Hidden;
        public int CachedLineRendererCount => isBuilt ? 5 : 0;

        private void Awake()
        {
            EnsureBuilt();
            ResetPresentation();
        }

        private void Update()
        {
            AdvancePresentation(Time.deltaTime);
        }

        public void ShowTarget(float areaRadius, bool isValid)
        {
            EnsureBuilt();
            radius = SanitizeRadius(areaRadius);
            style = isValid
                ? AbilityAreaPresentationStyle.TargetValid
                : AbilityAreaPresentationStyle.TargetInvalid;
            if (phase != AbilityAreaPresentationPhase.Targeting)
            {
                elapsed = 0f;
            }
            phase = AbilityAreaPresentationPhase.Targeting;
            duration = 1f;
            RefreshVisuals();
        }

        public void BeginTelegraph(
            float areaRadius,
            float telegraphDuration,
            AbilityAreaPresentationStyle presentationStyle
        )
        {
            EnsureBuilt();
            radius = SanitizeRadius(areaRadius);
            style = presentationStyle;
            duration = SanitizeDuration(telegraphDuration, 0.01f);
            elapsed = 0f;
            phase = AbilityAreaPresentationPhase.Telegraph;
            RefreshVisuals();
        }

        public void PlayImpact(
            float areaRadius,
            float impactDuration,
            AbilityAreaPresentationStyle presentationStyle
        )
        {
            EnsureBuilt();
            radius = SanitizeRadius(areaRadius);
            style = presentationStyle;
            duration = SanitizeDuration(
                impactDuration,
                defaultImpactSeconds
            );
            elapsed = 0f;
            phase = AbilityAreaPresentationPhase.Impact;
            RefreshVisuals();
        }

        public void HideTarget()
        {
            if (phase == AbilityAreaPresentationPhase.Targeting)
            {
                ResetPresentation();
            }
        }

        public void ResetPresentation()
        {
            phase = AbilityAreaPresentationPhase.Hidden;
            elapsed = 0f;
            duration = 0f;
            SetLineVisible(boundaryRing, false);
            SetLineVisible(energyRing, false);
            SetLineVisible(radialBurst, false);
            SetLineVisible(lightningBolt, false);
            SetLineVisible(aftermathRing, false);
            if (impactLight != null)
            {
                impactLight.enabled = false;
            }
        }

        /// <summary>
        /// Explicit advancement keeps lifecycle behavior directly testable.
        /// Targeting loops continuously; finite telegraphs and impacts hold
        /// their last frame until their owning pooled actor recycles them.
        /// </summary>
        public void AdvancePresentation(float deltaSeconds)
        {
            if (
                phase == AbilityAreaPresentationPhase.Hidden ||
                !IsFinite(deltaSeconds) ||
                deltaSeconds <= 0f
            )
            {
                return;
            }

            elapsed += deltaSeconds;
            if (phase == AbilityAreaPresentationPhase.Targeting)
            {
                elapsed %= duration;
            }
            else
            {
                elapsed = Mathf.Min(elapsed, duration);
            }
            RefreshVisuals();
        }

        private void EnsureBuilt()
        {
            if (isBuilt)
            {
                return;
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "No unlit shader is available for ability area feedback."
                );
            }

            presentationMaterial = new Material(shader)
            {
                name = "Ability Area Feedback (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            boundaryRing = CreateLine("Area Boundary", true, CircleSegments + 1);
            energyRing = CreateLine("Energy Shockwave", true, CircleSegments + 1);
            radialBurst = CreateLine(
                "Radial Power Burst",
                false,
                BurstRayCount * 2 + 1
            );
            lightningBolt = CreateLine(
                "Lightning Column",
                false,
                BoltSegments
            );
            aftermathRing = CreateLine(
                "Area Aftermath",
                true,
                CircleSegments + 1
            );

            GameObject lightObject = new GameObject("Impact Flash Light");
            lightObject.transform.SetParent(transform, false);
            impactLight = lightObject.AddComponent<Light>();
            impactLight.type = LightType.Point;
            impactLight.shadows = LightShadows.None;
            impactLight.renderMode = LightRenderMode.ForceVertex;
            impactLight.enabled = false;
            isBuilt = true;
        }

        private LineRenderer CreateLine(
            string objectName,
            bool loop,
            int positionCount
        )
        {
            GameObject child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = presentationMaterial;
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = positionCount;
            line.widthMultiplier = lineWidth;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            line.enabled = false;
            return line;
        }

        private void RefreshVisuals()
        {
            if (!isBuilt || phase == AbilityAreaPresentationPhase.Hidden)
            {
                return;
            }

            float normalized = NormalizedTime;
            Color primary = GetPrimaryColor(style);
            Color secondary = GetSecondaryColor(style);
            bool isLightning = style == AbilityAreaPresentationStyle.Lightning;
            Vector3 normal = transform.up.sqrMagnitude > 0.000001f
                ? transform.up.normalized
                : Vector3.up;
            Vector3 center = transform.position + normal * surfaceOffset;

            if (
                phase == AbilityAreaPresentationPhase.Targeting ||
                phase == AbilityAreaPresentationPhase.Telegraph
            )
            {
                float pulse = 0.68f + 0.32f * Mathf.Sin(elapsed * 16f);
                float sweep = phase == AbilityAreaPresentationPhase.Targeting
                    ? Mathf.Repeat(elapsed * 1.45f, 1f)
                    : Mathf.Clamp01(normalized);
                DrawCircle(boundaryRing, center, normal, radius);
                DrawCircle(
                    energyRing,
                    center + normal * 0.012f,
                    normal,
                    radius * Mathf.Lerp(0.12f, 0.96f, sweep)
                );
                SetLine(boundaryRing, primary, lineWidth * 1.25f, pulse);
                SetLine(energyRing, secondary, lineWidth * 0.8f, 0.85f);
                SetLineVisible(radialBurst, false);
                SetLineVisible(lightningBolt, false);
                SetLineVisible(aftermathRing, false);
                impactLight.enabled = false;
                return;
            }

            float fade = 1f - Mathf.SmoothStep(0.45f, 1f, normalized);
            float shockScale = Mathf.Lerp(0.08f, 1.12f, EaseOut(normalized));
            DrawCircle(boundaryRing, center, normal, radius);
            DrawCircle(
                energyRing,
                center + normal * 0.018f,
                normal,
                radius * shockScale
            );
            DrawRadialBurst(
                radialBurst,
                center + normal * 0.025f,
                normal,
                radius * Mathf.Lerp(0.12f, 1.05f, EaseOut(normalized))
            );
            SetLine(boundaryRing, primary, lineWidth * 1.65f, fade * 0.85f);
            SetLine(energyRing, secondary, lineWidth * 2.4f, fade);
            SetLine(radialBurst, secondary, lineWidth * 1.1f, fade * 0.9f);
            DrawCircle(
                aftermathRing,
                center + normal * 0.009f,
                normal,
                radius
            );
            float aftermathFade = 1f - Mathf.SmoothStep(0.72f, 1f, normalized);
            SetLine(
                aftermathRing,
                Color.Lerp(primary, Color.black, 0.42f),
                lineWidth * 1.9f,
                aftermathFade * 0.9f
            );

            if (isLightning)
            {
                DrawLightningBolt(lightningBolt, center, normal, radius, elapsed);
                SetLine(
                    lightningBolt,
                    Color.white,
                    lineWidth * 2.8f,
                    fade
                );
            }
            else
            {
                SetLineVisible(lightningBolt, false);
            }

            impactLight.enabled = createImpactLight && fade > 0.01f;
            if (impactLight.enabled)
            {
                impactLight.transform.position = center + normal * 0.3f;
                impactLight.color = secondary;
                impactLight.range = Mathf.Max(2f, radius * 1.75f);
                impactLight.intensity = Mathf.Lerp(7f, 0f, normalized);
            }
        }

        private static void DrawCircle(
            LineRenderer line,
            Vector3 center,
            Vector3 normal,
            float circleRadius
        )
        {
            CreateBasis(normal, out Vector3 tangent, out Vector3 bitangent);
            for (int index = 0; index <= CircleSegments; index++)
            {
                float angle = index * (Mathf.PI * 2f / CircleSegments);
                line.SetPosition(
                    index,
                    center +
                    (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) *
                    circleRadius
                );
            }
        }

        private static void DrawRadialBurst(
            LineRenderer line,
            Vector3 center,
            Vector3 normal,
            float burstRadius
        )
        {
            CreateBasis(normal, out Vector3 tangent, out Vector3 bitangent);
            line.SetPosition(0, center);
            for (int ray = 0; ray < BurstRayCount; ray++)
            {
                float angle = ray * (Mathf.PI * 2f / BurstRayCount);
                Vector3 direction =
                    tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle);
                line.SetPosition(ray * 2 + 1, center + direction * burstRadius);
                line.SetPosition(ray * 2 + 2, center);
            }
        }

        private static void DrawLightningBolt(
            LineRenderer line,
            Vector3 center,
            Vector3 normal,
            float areaRadius,
            float time
        )
        {
            CreateBasis(normal, out Vector3 tangent, out Vector3 bitangent);
            float height = Mathf.Max(8f, areaRadius * 2.25f);
            for (int index = 0; index < BoltSegments; index++)
            {
                float t = index / (BoltSegments - 1f);
                float envelope = Mathf.Sin(t * Mathf.PI);
                float jitterX = Mathf.Sin(index * 12.9898f + time * 41f);
                float jitterY = Mathf.Cos(index * 7.233f + time * 37f);
                Vector3 lateral =
                    (tangent * jitterX + bitangent * jitterY) *
                    areaRadius * 0.09f * envelope;
                line.SetPosition(index, center + normal * height * t + lateral);
            }
        }

        private static void CreateBasis(
            Vector3 normal,
            out Vector3 tangent,
            out Vector3 bitangent
        )
        {
            tangent = Vector3.Cross(
                Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.98f
                    ? Vector3.right
                    : Vector3.up,
                normal
            ).normalized;
            bitangent = Vector3.Cross(normal, tangent).normalized;
        }

        private static void SetLine(
            LineRenderer line,
            Color color,
            float width,
            float alpha
        )
        {
            color.a *= Mathf.Clamp01(alpha);
            line.startColor = color;
            line.endColor = color;
            line.widthMultiplier = Mathf.Max(0.005f, width);
            line.enabled = color.a > 0.005f;
        }

        private static void SetLineVisible(LineRenderer line, bool visible)
        {
            if (line != null)
            {
                line.enabled = visible;
            }
        }

        private static Color GetPrimaryColor(AbilityAreaPresentationStyle value)
        {
            switch (value)
            {
                case AbilityAreaPresentationStyle.Lightning:
                    return new Color(0.08f, 0.72f, 1f, 0.95f);
                case AbilityAreaPresentationStyle.TargetInvalid:
                    return new Color(1f, 0.08f, 0.025f, 0.92f);
                case AbilityAreaPresentationStyle.TargetValid:
                    return new Color(0.1f, 0.94f, 1f, 0.92f);
                default:
                    return new Color(1f, 0.22f, 0.025f, 0.95f);
            }
        }

        private static Color GetSecondaryColor(AbilityAreaPresentationStyle value)
        {
            switch (value)
            {
                case AbilityAreaPresentationStyle.Lightning:
                    return new Color(0.62f, 0.22f, 1f, 1f);
                case AbilityAreaPresentationStyle.TargetInvalid:
                    return new Color(1f, 0.42f, 0.08f, 1f);
                case AbilityAreaPresentationStyle.TargetValid:
                    return new Color(0.3f, 1f, 0.72f, 1f);
                default:
                    return new Color(1f, 0.78f, 0.08f, 1f);
            }
        }

        private static float EaseOut(float value)
        {
            float inverse = 1f - Mathf.Clamp01(value);
            return 1f - inverse * inverse * inverse;
        }

        private static float SanitizeRadius(float value)
        {
            return IsFinite(value) ? Mathf.Max(0.01f, value) : 1f;
        }

        private static float SanitizeDuration(float value, float fallback)
        {
            return IsFinite(value) && value > 0f
                ? value
                : Mathf.Max(0.01f, fallback);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void OnDestroy()
        {
            if (presentationMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(presentationMaterial);
            }
            else
            {
                DestroyImmediate(presentationMaterial);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            lineWidth = Mathf.Max(0.01f, lineWidth);
            surfaceOffset = Mathf.Max(0f, surfaceOffset);
            defaultImpactSeconds = Mathf.Max(0.01f, defaultImpactSeconds);
        }
#endif
    }
}
