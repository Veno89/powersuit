using UnityEngine;

namespace Powersuit.Enemies.UnityAdapters
{
    /// <summary>
    /// Procedural, asset-free combat readability for pooled enemies. Damage
    /// produces a short silhouette pulse/flash; attacks draw an origin-to-target
    /// warning and target ring for the complete authoritative telegraph window.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyArchetypeController))]
    public sealed class EnemyCombatReadabilityPresenter : MonoBehaviour
    {
        private const int RingSegments = 48;

        [SerializeField] private EnemyArchetypeController controller;
        [SerializeField] private Transform visualRoot;
        [SerializeField, Range(0f, 0.25f)] private float damageScalePunch = 0.075f;
        [SerializeField, Min(0f)] private float damagePulseSharpness = 13f;
        [SerializeField, Min(0.01f)] private float telegraphLineWidth = 0.075f;
        [SerializeField, Min(0.1f)] private float telegraphTargetRadius = 0.75f;

        private Renderer[] renderers;
        private Color[] authoredColors;
        private MaterialPropertyBlock propertyBlock;
        private Vector3 authoredScale = Vector3.one;
        private LineRenderer telegraphLine;
        private LineRenderer targetRing;
        private Material lineMaterial;
        private float damagePulse;
        private float telegraphRemaining;
        private float telegraphDuration;
        private float lastHealth = float.NaN;
        private Vector3 telegraphOrigin;
        private Vector3 telegraphTarget;
        private bool isSubscribed;

        public EnemyArchetypeController Controller => controller;
        public Transform VisualRoot => visualRoot;
        public bool IsTelegraphVisible => telegraphLine != null && telegraphLine.enabled;
        public float DamagePulse => damagePulse;

        public void Configure(EnemyArchetypeController owner, Transform visual)
        {
            Unsubscribe();
            controller = owner;
            visualRoot = visual;
            CacheVisuals();
            Subscribe();
        }

        private void Awake()
        {
            controller ??= GetComponent<EnemyArchetypeController>();
            if (visualRoot == null)
            {
                visualRoot = transform.Find("Visual");
            }
            CacheVisuals();
            EnsureTelegraphVisuals();
        }

        private void OnEnable()
        {
            controller ??= GetComponent<EnemyArchetypeController>();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ResetPresentation();
        }

        private void Subscribe()
        {
            if (controller == null || isSubscribed)
            {
                return;
            }
            controller.HealthChanged += HandleHealthChanged;
            controller.StaggerStarted += HandleStagger;
            controller.AttackTelegraphStarted += HandleTelegraph;
            controller.AttackRequested += HandleAttack;
            controller.Died += HandleDeath;
            lastHealth = controller.CurrentHealth;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (controller == null || !isSubscribed)
            {
                isSubscribed = false;
                return;
            }
            controller.HealthChanged -= HandleHealthChanged;
            controller.StaggerStarted -= HandleStagger;
            controller.AttackTelegraphStarted -= HandleTelegraph;
            controller.AttackRequested -= HandleAttack;
            controller.Died -= HandleDeath;
            isSubscribed = false;
        }

        private void OnDestroy()
        {
            if (lineMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(lineMaterial);
                }
                else
                {
                    DestroyImmediate(lineMaterial);
                }
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (!(deltaTime > 0f))
            {
                return;
            }

            damagePulse = Mathf.Lerp(
                damagePulse,
                0f,
                1f - Mathf.Exp(-damagePulseSharpness * deltaTime)
            );
            ApplyDamagePulse();

            if (telegraphRemaining <= 0f)
            {
                SetTelegraphVisible(false);
                return;
            }

            telegraphRemaining = Mathf.Max(0f, telegraphRemaining - deltaTime);
            float progress = 1f - telegraphRemaining / Mathf.Max(0.001f, telegraphDuration);
            RefreshTelegraph(progress);
        }

        private void HandleHealthChanged(float current, float maximum)
        {
            if (!float.IsNaN(lastHealth) && current < lastHealth - 0.001f)
            {
                damagePulse = 1f;
            }
            lastHealth = current;
        }

        private void HandleStagger(float duration)
        {
            damagePulse = 1.25f;
        }

        private void HandleTelegraph(EnemyTelegraphSignal signal)
        {
            EnsureTelegraphVisuals();
            telegraphOrigin = signal.Origin;
            telegraphTarget = signal.IntendedTarget;
            telegraphDuration = Mathf.Max(0.05f, signal.DurationSeconds);
            telegraphRemaining = telegraphDuration;
            SetTelegraphVisible(true);
            RefreshTelegraph(0f);
        }

        private void HandleAttack(EnemyAttackSignal signal)
        {
            telegraphRemaining = 0f;
            SetTelegraphVisible(false);
        }

        private void HandleDeath()
        {
            damagePulse = 1.6f;
            telegraphRemaining = 0f;
            SetTelegraphVisible(false);
        }

        private void CacheVisuals()
        {
            if (visualRoot == null)
            {
                return;
            }
            authoredScale = visualRoot.localScale;
            renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            authoredColors = new Color[renderers.Length];
            for (int index = 0; index < renderers.Length; index++)
            {
                Material material = renderers[index].sharedMaterial;
                authoredColors[index] = material != null && material.HasProperty("_BaseColor")
                    ? material.GetColor("_BaseColor")
                    : material != null && material.HasProperty("_Color")
                        ? material.color
                        : Color.white;
            }
            propertyBlock ??= new MaterialPropertyBlock();
        }

        private void ApplyDamagePulse()
        {
            if (visualRoot != null)
            {
                visualRoot.localScale = authoredScale * (1f + damagePulse * damageScalePunch);
            }
            if (renderers == null || propertyBlock == null)
            {
                return;
            }
            float flash = Mathf.Clamp01(damagePulse);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }
                renderer.GetPropertyBlock(propertyBlock);
                Color color = Color.Lerp(authoredColors[index], Color.white, flash * 0.78f);
                propertyBlock.SetColor("_BaseColor", color);
                propertyBlock.SetColor("_Color", color);
                propertyBlock.SetColor("_EmissionColor", new Color(1f, 0.16f, 0.04f) * flash * 2.5f);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void EnsureTelegraphVisuals()
        {
            if (telegraphLine != null && targetRing != null)
            {
                return;
            }
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                return;
            }
            lineMaterial = new Material(shader)
            {
                name = "Enemy Telegraph (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            telegraphLine = CreateLine("Attack Warning", 2, false);
            targetRing = CreateLine("Attack Target Ring", RingSegments + 1, true);
            SetTelegraphVisible(false);
        }

        private LineRenderer CreateLine(string name, int positions, bool loop)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = lineMaterial;
            line.useWorldSpace = true;
            line.positionCount = positions;
            line.loop = loop;
            line.widthMultiplier = telegraphLineWidth;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private void RefreshTelegraph(float progress)
        {
            if (telegraphLine == null || targetRing == null)
            {
                return;
            }
            float pulse = 0.58f + 0.42f * Mathf.Sin(progress * Mathf.PI * 12f);
            Color color = Color.Lerp(new Color(1f, 0.72f, 0.08f), Color.red, progress);
            color.a = Mathf.Clamp01(pulse);
            telegraphLine.startColor = telegraphLine.endColor = color;
            targetRing.startColor = targetRing.endColor = color;
            telegraphLine.widthMultiplier = telegraphLineWidth * Mathf.Lerp(0.75f, 1.75f, progress);
            targetRing.widthMultiplier = telegraphLineWidth * 1.35f;
            telegraphLine.SetPosition(0, telegraphOrigin);
            telegraphLine.SetPosition(1, telegraphTarget);
            float radius = telegraphTargetRadius * Mathf.Lerp(1.25f, 0.72f, progress);
            for (int index = 0; index <= RingSegments; index++)
            {
                float angle = index * Mathf.PI * 2f / RingSegments;
                targetRing.SetPosition(
                    index,
                    telegraphTarget + new Vector3(Mathf.Cos(angle), 0.045f, Mathf.Sin(angle)) * radius
                );
            }
        }

        private void SetTelegraphVisible(bool visible)
        {
            if (telegraphLine != null) telegraphLine.enabled = visible;
            if (targetRing != null) targetRing.enabled = visible;
        }

        private void ResetPresentation()
        {
            damagePulse = 0f;
            telegraphRemaining = 0f;
            if (visualRoot != null)
            {
                visualRoot.localScale = authoredScale;
            }
            if (renderers != null)
            {
                foreach (Renderer renderer in renderers)
                {
                    if (renderer != null) renderer.SetPropertyBlock(null);
                }
            }
            SetTelegraphVisible(false);
        }
    }
}
