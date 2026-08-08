using UnityEngine;

public sealed class PlayerProjectile : MonoBehaviour
{
    [Header("Projectile Properties")]
    [SerializeField] private float speed = 45f;
    [SerializeField] private float damage = 25f;
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private float radius = 0.15f;
    [SerializeField] private Color projectileColor = new Color(0.2f, 0.8f, 1f, 1f);

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

    private static Material enemyFallbackMaterial;
    private static Material environmentFallbackMaterial;

    public void Initialize(
        Vector3 travelDirection,
        float travelSpeed,
        float projectileDamage,
        float maxLifetime,
        float projectileRadius,
        Transform sourceTransform
    )
    {
        direction = travelDirection.normalized;
        speed = travelSpeed;
        damage = projectileDamage;
        lifetime = maxLifetime;
        radius = Mathf.Max(0.05f, projectileRadius);
        sourceRoot = sourceTransform;
        isInitialized = true;
        spawnTime = Time.time;

        EnsureTrail();
        Destroy(gameObject, lifetime);
        ApplyVisuals();
    }

    private void Start()
    {
        if (!isInitialized)
        {
            direction = transform.forward;
            spawnTime = Time.time;
            EnsureTrail();
            Destroy(gameObject, lifetime);
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

        trailRenderer.time = trailTime;
        trailRenderer.startWidth = startWidth;
        trailRenderer.endWidth = endWidth;

        Renderer rend = GetComponent<Renderer>();
        if (rend != null && trailRenderer.material == null)
        {
            trailRenderer.material = rend.material;
        }

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(projectileColor, 0f), new GradientColorKey(projectileColor, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        trailRenderer.colorGradient = gradient;
        trailRenderer.Clear();
    }

    private void Update()
    {
        float stepDistance = speed * Time.deltaTime;
        Vector3 currentPosition = transform.position;
        Vector3 nextPosition = currentPosition + direction * stepDistance;

        RaycastHit hit;
        if (Physics.SphereCast(
                currentPosition,
                radius,
                direction,
                out hit,
                stepDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            ))
        {
            if (IsValidHit(hit.collider))
            {
                OnHit(hit);
                return;
            }
        }

        transform.position = nextPosition;
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

        DamageableTarget enemyTarget = hit.collider.GetComponentInParent<DamageableTarget>();
        if (enemyTarget != null)
        {
            enemyTarget.TakeDamage(damage, hit.point, direction);
            ReticleHitMarker.ShowHitMarker();

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

        Destroy(gameObject);
    }

    private void SpawnFallbackImpact(Vector3 position, Quaternion rotation, bool isEnemy)
    {
        GameObject sparkObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sparkObj.name = isEnemy ? "EnemyImpactFallback" : "EnvImpactFallback";
        sparkObj.transform.position = position;
        sparkObj.transform.rotation = rotation;
        sparkObj.transform.localScale = Vector3.one * (isEnemy ? 0.4f : 0.25f);

        SphereCollider col = sparkObj.GetComponent<SphereCollider>();
        if (col != null)
        {
            Destroy(col);
        }

        Renderer rend = sparkObj.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.sharedMaterial = GetFallbackImpactMaterial(isEnemy);
        }

        Destroy(sparkObj, 0.12f);
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
        Renderer rend = GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = rend.material;
            if (mat != null)
            {
                mat.color = projectileColor;
                if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", projectileColor);
                }

                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", projectileColor * 2f);
                }
            }
        }
    }
}
