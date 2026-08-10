using System.Collections;
using UnityEngine;

public sealed class SimpleEnemy : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float movementSpeed = 3f;
    [SerializeField] private float stoppingDistance = 8f;
    [SerializeField] private float turningSpeed = 8f;

    [Header("Weapon")]
    [SerializeField] private float shootingRange = 18f;
    [SerializeField] private float shotsPerSecond = 1f;
    [SerializeField] private float projectileSpeed = 12f;
    [SerializeField] private float projectileDamage = 10f;
    [SerializeField] private EnemyProjectile projectilePrefab;
    [SerializeField, Min(0)] private int projectilePrewarmCount = 6;

    [Header("Audio Hooks")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip shootSound;
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private AudioClip deathSound;

    private Transform player;
    private float nextShotTime;
    private bool isDead;
    private Transform visualChild;

    private static GameObject fallbackProjectileTemplate;
    private static Material fallbackProjectileMaterial;

    private void Start()
    {
        PowerSuitController controller = FindAnyObjectByType<PowerSuitController>();
        if (controller == null)
        {
            Debug.LogError("Enemy could not find the player.", this);
            enabled = false;
            return;
        }

        player = controller.transform;

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        Transform visual = transform.Find("VisualRoot") ?? transform.Find("Model");
        if (visual == null && transform.childCount > 0)
        {
            foreach (Transform child in transform)
            {
                if (child.name != "HealthBarUI" && child.GetComponent<Canvas>() == null)
                {
                    visual = child;
                    break;
                }
            }
        }
        visualChild = visual ?? transform;

        GameObject projectileTemplate = GetProjectileTemplate();
        if (projectileTemplate != null)
        {
            CombatFeedbackPool.Prewarm(
                projectileTemplate,
                projectilePrewarmCount
            );
        }

        DamageableTarget target = GetComponent<DamageableTarget>();
        if (target != null)
        {
            target.OnHit += HandleHitAudio;
        }
    }

    private void OnDestroy()
    {
        DamageableTarget target = GetComponent<DamageableTarget>();
        if (target != null)
        {
            target.OnHit -= HandleHitAudio;
        }
    }

    private void HandleHitAudio(Vector3 hitPoint, Vector3 hitDirection, float damage)
    {
        if (audioSource != null && hitSound != null && !isDead)
        {
            audioSource.pitch = Random.Range(0.95f, 1.05f);
            audioSource.PlayOneShot(hitSound);
        }
    }

    private void Update()
    {
        if (isDead || player == null)
        {
            return;
        }

        Vector3 targetPosition = player.position;
        targetPosition.y = transform.position.y;

        Vector3 offset = targetPosition - transform.position;
        float distance = offset.magnitude;

        if (offset.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(offset.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turningSpeed * Time.deltaTime);
        }

        if (distance > stoppingDistance)
        {
            transform.position += transform.forward * (movementSpeed * Time.deltaTime);
        }

        if (distance <= shootingRange && Time.time >= nextShotTime)
        {
            nextShotTime = Time.time + 1f / shotsPerSecond;
            FireProjectile();
        }
    }

    private void FireProjectile()
    {
        Vector3 spawnPosition = transform.position + Vector3.up * 1.2f + transform.forward * 0.8f;
        Vector3 targetPosition = player.position + Vector3.up;

        GameObject projectile = CombatFeedbackPool.Spawn(
            GetProjectileTemplate(),
            spawnPosition,
            Quaternion.LookRotation(targetPosition - spawnPosition)
        );
        if (projectile == null)
        {
            return;
        }

        projectile.name = "Enemy Projectile";
        projectile.transform.localScale = Vector3.one * 0.3f;
        EnemyProjectile projectileBehaviour =
            projectile.GetComponent<EnemyProjectile>();
        if (projectileBehaviour == null)
        {
            CombatFeedbackPool.Recycle(projectile);
            return;
        }
        projectileBehaviour.Initialize(
            targetPosition - spawnPosition,
            projectileSpeed,
            projectileDamage,
            transform
        );

        if (audioSource != null && shootSound != null)
        {
            audioSource.pitch = Random.Range(0.9f, 1.1f);
            audioSource.PlayOneShot(shootSound);
        }
    }

    private GameObject GetProjectileTemplate()
    {
        if (projectilePrefab != null)
        {
            return projectilePrefab.gameObject;
        }

        if (fallbackProjectileTemplate != null)
        {
            return fallbackProjectileTemplate;
        }

        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        template.name = "LegacyEnemyProjectileFallbackTemplate";
        template.hideFlags = HideFlags.HideAndDontSave;
        template.transform.localScale = Vector3.one * 0.3f;

        SphereCollider projectileCollider =
            template.GetComponent<SphereCollider>();
        if (projectileCollider != null)
        {
            projectileCollider.isTrigger = true;
        }

        Rigidbody projectileRigidbody = template.AddComponent<Rigidbody>();
        projectileRigidbody.useGravity = false;
        projectileRigidbody.isKinematic = true;

        Renderer projectileRenderer = template.GetComponent<Renderer>();
        if (projectileRenderer != null)
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color");
            if (shader != null)
            {
                fallbackProjectileMaterial = new Material(shader)
                {
                    name = "Legacy Enemy Projectile Fallback",
                    color = Color.red,
                    hideFlags = HideFlags.HideAndDontSave
                };
                projectileRenderer.sharedMaterial = fallbackProjectileMaterial;
            }
        }

        template.AddComponent<EnemyProjectile>();
        template.SetActive(false);
        fallbackProjectileTemplate = template;
        return fallbackProjectileTemplate;
    }

    public void HandleDeathSequence(float delay)
    {
        if (isDead)
        {
            return;
        }

        isDead = true;

        EnemyHitReaction reaction = GetComponent<EnemyHitReaction>();
        if (reaction != null)
        {
            reaction.StopReaction();
        }

        if (audioSource != null && deathSound != null)
        {
            audioSource.pitch = Random.Range(0.95f, 1.05f);
            audioSource.PlayOneShot(deathSound);
        }

        StartCoroutine(DoDeathAnimation(Mathf.Max(0.01f, delay)));
        enabled = false;
    }

    private IEnumerator DoDeathAnimation(float delay)
    {
        float elapsed = 0f;
        Quaternion startRot = visualChild.localRotation;
        Quaternion targetRot = startRot * Quaternion.Euler(65f, 0f, 0f);
        Vector3 startPos = visualChild.localPosition;
        Vector3 targetPos = startPos - Vector3.up * 0.3f;

        while (elapsed < delay)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / delay;

            visualChild.localRotation = Quaternion.Slerp(startRot, targetRot, t);
            visualChild.localPosition = Vector3.Lerp(startPos, targetPos, t);

            yield return null;
        }

        Destroy(gameObject);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        projectilePrewarmCount = Mathf.Max(0, projectilePrewarmCount);
    }
#endif
}
