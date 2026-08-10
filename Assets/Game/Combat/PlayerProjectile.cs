using System;
using Powersuit.Combat;
using Powersuit.Combat.UnityAdapters;
using UnityEngine;

public sealed class PlayerProjectile : MonoBehaviour, ICombatProjectilePoolable
{
    private const int InitialHitBufferSize = 16;
    private const int MaximumHitBufferSize = 128;
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorPropertyId =
        Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorPropertyId =
        Shader.PropertyToID("_EmissionColor");

    [Header("Projectile Properties")]
    [SerializeField] private float speed = 45f;
    [SerializeField] private float damage = 25f;
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private float radius = 0.15f;
    [SerializeField] private Color projectileColor = new Color(0.2f, 0.8f, 1f, 1f);
    [SerializeField] private CombatFaction sourceFaction = CombatFaction.Player;
    [SerializeField] private DamageType damageType = DamageType.Kinetic;

    [Header("Trail Visuals")]
    [SerializeField] private float trailTime = 0.15f;
    [SerializeField] private float startWidth = 0.15f;
    [SerializeField] private float endWidth = 0.02f;

    [Header("Impact Effect Prefabs")]
    [SerializeField] private GameObject enemyImpactPrefab;
    [SerializeField] private GameObject environmentImpactPrefab;

    private Vector3 direction;
    private Transform sourceRoot;
    private bool isInitialized;
    private float spawnTime;
    private TrailRenderer trailRenderer;
    private bool isCritical;
    private bool trailConfigured;
    private bool visualsApplied;
    private MaterialPropertyBlock visualPropertyBlock;
    private RaycastHit[] hitBuffer = new RaycastHit[InitialHitBufferSize];

    private static Material enemyFallbackMaterial;
    private static Material environmentFallbackMaterial;
    private static GameObject enemyFallbackPrefab;
    private static GameObject environmentFallbackPrefab;

    /// <summary>
    /// Reports the authoritative damage transaction before this pooled
    /// projectile is recycled. Owners can award contribution without probing
    /// target components or trusting presentation callbacks.
    /// </summary>
    public event Action<DamageResult> DamageResolved;

    private void Awake()
    {
        // Pool prewarming instantiates inactive objects. Doing one-time trail
        // and material setup here moves those allocations out of the first
        // combat frame instead of deferring them to Initialize.
        EnsureTrail();
        ApplyVisuals();
    }

    public void Initialize(
        Vector3 travelDirection,
        float travelSpeed,
        float projectileDamage,
        float maxLifetime,
        float projectileRadius,
        Transform sourceTransform,
        bool criticalHit = false
    )
    {
        direction = travelDirection.normalized;
        speed = travelSpeed;
        damage = projectileDamage;
        lifetime = maxLifetime;
        radius = Mathf.Max(0.05f, projectileRadius);
        sourceRoot = sourceTransform;
        isCritical = criticalHit;
        isInitialized = true;
        spawnTime = Time.time;

        EnsureTrail();
        ApplyVisuals();
    }

    private void Start()
    {
        if (!isInitialized)
        {
            direction = transform.forward;
            spawnTime = Time.time;
            isInitialized = true;
            EnsureTrail();
            ApplyVisuals();
        }
    }

    private void EnsureTrail()
    {
        trailRenderer = GetComponent<TrailRenderer>();
        if (trailRenderer == null)
        {
            trailRenderer = gameObject.AddComponent<TrailRenderer>();
        }

        if (!trailConfigured)
        {
            trailRenderer.time = trailTime;
            trailRenderer.startWidth = startWidth;
            trailRenderer.endWidth = endWidth;

            Renderer rend = GetComponent<Renderer>();
            if (rend != null && trailRenderer.sharedMaterial == null)
            {
                trailRenderer.sharedMaterial = rend.sharedMaterial;
            }

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(projectileColor, 0f),
                    new GradientColorKey(projectileColor, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            trailRenderer.colorGradient = gradient;
            trailConfigured = true;
        }
        trailRenderer.Clear();
    }

    private void Update()
    {
        if (!isInitialized)
        {
            return;
        }

        if (Time.time - spawnTime >= lifetime)
        {
            RecycleSelf();
            return;
        }

        float stepDistance = speed * Time.deltaTime;
        if (stepDistance <= 0f)
        {
            return;
        }

        Vector3 currentPosition = transform.position;
        Vector3 nextPosition = currentPosition + direction * stepDistance;

        int hitCount = CastStep(currentPosition, stepDistance);
        RaycastHit nearestHit = default;
        float nearestDistance = float.PositiveInfinity;
        for (int index = 0; index < hitCount; index++)
        {
            RaycastHit candidate = hitBuffer[index];
            if (
                IsValidHit(candidate.collider) &&
                candidate.distance < nearestDistance
            )
            {
                nearestDistance = candidate.distance;
                nearestHit = candidate;
            }
        }

        if (nearestDistance < float.PositiveInfinity)
        {
            OnHit(nearestHit);
            return;
        }

        transform.position = nextPosition;
    }

    private int CastStep(Vector3 origin, float distance)
    {
        int hitCount;
        do
        {
            hitCount = Physics.SphereCastNonAlloc(
                origin,
                radius,
                direction,
                hitBuffer,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            if (
                hitCount < hitBuffer.Length ||
                hitBuffer.Length >= MaximumHitBufferSize
            )
            {
                return hitCount;
            }

            hitBuffer = new RaycastHit[
                Mathf.Min(MaximumHitBufferSize, hitBuffer.Length * 2)
            ];
        }
        while (true);
    }

    private bool IsValidHit(Collider hitCollider)
    {
        if (hitCollider == null) return false;
        Transform hitTransform = hitCollider.transform;

        if (sourceRoot != null && (hitTransform == sourceRoot || hitTransform.IsChildOf(sourceRoot)))
        {
            return false;
        }

        return true;
    }

    private void OnHit(RaycastHit hit)
    {
        transform.position = hit.point;
        Quaternion impactRotation = Quaternion.LookRotation(hit.normal);

        IDamageReceiver receiver =
            hit.collider.GetComponentInParent<IDamageReceiver>();
        DamageResult damageResult = DamageResult.Ignored;
        if (receiver != null)
        {
            damageResult = receiver.ApplyDamage(
                new DamageInfo(
                    sourceRoot != null ? sourceRoot.gameObject : gameObject,
                    sourceFaction,
                    damageType,
                    damage,
                    CombatVectorConversion.ToCombat(hit.point),
                    CombatVectorConversion.ToCombat(direction),
                    isCritical
                )
            );
        }

        if (damageResult.WasApplied)
        {
            ReticleHitMarker.ShowHitMarker(damageResult.WasKilled);

            if (enemyImpactPrefab != null)
            {
                CombatFeedbackPool.Spawn(enemyImpactPrefab, hit.point, impactRotation);
            }
            else
            {
                SpawnFallbackImpact(hit.point, impactRotation, isEnemy: true);
            }
        }
        else
        {
            if (environmentImpactPrefab != null)
            {
                CombatFeedbackPool.Spawn(environmentImpactPrefab, hit.point, impactRotation);
            }
            else
            {
                SpawnFallbackImpact(hit.point, impactRotation, isEnemy: false);
            }
        }

        if (trailRenderer != null)
        {
            trailRenderer.Clear();
        }

        DamageResolved?.Invoke(damageResult);

        RecycleSelf();
    }

    private void SpawnFallbackImpact(Vector3 position, Quaternion rotation, bool isEnemy)
    {
        GameObject prefab = GetFallbackImpactPrefab(isEnemy);
        GameObject sparkObj = CombatFeedbackPool.Spawn(prefab, position, rotation);
        if (sparkObj == null)
        {
            return;
        }

        sparkObj.transform.localScale = Vector3.one * (isEnemy ? 0.4f : 0.25f);
    }

    private static GameObject GetFallbackImpactPrefab(bool isEnemy)
    {
        GameObject cached = isEnemy ? enemyFallbackPrefab : environmentFallbackPrefab;
        if (cached != null)
        {
            return cached;
        }

        cached = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cached.name = isEnemy
            ? "EnemyImpactFallbackTemplate"
            : "EnvironmentImpactFallbackTemplate";
        cached.hideFlags = HideFlags.HideAndDontSave;

        Collider collider = cached.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
        }

        Renderer rend = cached.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.sharedMaterial = GetFallbackImpactMaterial(isEnemy);
        }

        AutoRecycleEffect recycle = cached.AddComponent<AutoRecycleEffect>();
        recycle.SetDuration(0.12f);
        cached.SetActive(false);

        if (isEnemy)
        {
            enemyFallbackPrefab = cached;
        }
        else
        {
            environmentFallbackPrefab = cached;
        }

        return cached;
    }

    private static Material GetFallbackImpactMaterial(bool isEnemy)
    {
        Material cached = isEnemy ? enemyFallbackMaterial : environmentFallbackMaterial;
        if (cached != null)
        {
            return cached;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        cached = new Material(shader)
        {
            name = isEnemy ? "Enemy Impact Fallback" : "Environment Impact Fallback",
            color = isEnemy
                ? new Color(1f, 0.4f, 0.2f, 1f)
                : new Color(0.9f, 0.9f, 0.7f, 1f),
            hideFlags = HideFlags.HideAndDontSave
        };

        if (isEnemy)
        {
            enemyFallbackMaterial = cached;
        }
        else
        {
            environmentFallbackMaterial = cached;
        }

        return cached;
    }

    private void ApplyVisuals()
    {
        if (visualsApplied)
        {
            return;
        }

        Renderer rend = GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = rend.sharedMaterial;
            if (mat != null)
            {
                visualPropertyBlock ??= new MaterialPropertyBlock();
                rend.GetPropertyBlock(visualPropertyBlock);
                if (mat.HasProperty(ColorPropertyId))
                {
                    visualPropertyBlock.SetColor(
                        ColorPropertyId,
                        projectileColor
                    );
                }

                if (mat.HasProperty(BaseColorPropertyId))
                {
                    visualPropertyBlock.SetColor(
                        BaseColorPropertyId,
                        projectileColor
                    );
                }

                if (mat.HasProperty(EmissionColorPropertyId))
                {
                    visualPropertyBlock.SetColor(
                        EmissionColorPropertyId,
                        projectileColor * 2f
                    );
                }

                rend.SetPropertyBlock(visualPropertyBlock);
            }
        }

        visualsApplied = true;
    }

    public void OnPoolSpawned()
    {
        sourceRoot = null;
        isCritical = false;
        isInitialized = false;
        spawnTime = Time.time;
        if (trailRenderer != null)
        {
            trailRenderer.Clear();
            trailRenderer.emitting = true;
        }
    }

    public void OnPoolRecycled()
    {
        // Prewarm can instantiate an inactive template whose Awake has not run.
        // Ensure its one-time renderer setup is still paid during prewarm.
        EnsureTrail();
        ApplyVisuals();
        sourceRoot = null;
        DamageResolved = null;
        isCritical = false;
        isInitialized = false;
        if (trailRenderer != null)
        {
            trailRenderer.Clear();
            trailRenderer.emitting = false;
        }
    }

    private void RecycleSelf()
    {
        isInitialized = false;
        CombatFeedbackPool.Recycle(gameObject);
    }
}
