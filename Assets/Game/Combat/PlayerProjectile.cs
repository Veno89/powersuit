using UnityEngine;

public sealed class PlayerProjectile : MonoBehaviour
{
    [Header("Projectile Properties")]
    [SerializeField] private float speed = 45f;
    [SerializeField] private float damage = 25f;
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private float radius = 0.15f;
    [SerializeField] private Color projectileColor = new Color(0.2f, 0.8f, 1f, 1f);

    private Vector3 direction;
    private Transform sourceRoot;
    private bool isInitialized;
    private float spawnTime;

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

        Destroy(gameObject, lifetime);
        ApplyVisuals();
    }

    private void Start()
    {
        if (!isInitialized)
        {
            direction = transform.forward;
            spawnTime = Time.time;
            Destroy(gameObject, lifetime);
            ApplyVisuals();
        }
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

        DamageableTarget enemyTarget = hit.collider.GetComponentInParent<DamageableTarget>();
        if (enemyTarget != null)
        {
            enemyTarget.TakeDamage(damage);
        }

        Destroy(gameObject);
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
