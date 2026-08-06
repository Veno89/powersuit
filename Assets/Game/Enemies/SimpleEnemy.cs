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

    private Transform player;
    private float nextShotTime;

    private void Start()
    {
        PowerSuitController controller =
            FindAnyObjectByType<PowerSuitController>();

        if (controller == null)
        {
            Debug.LogError(
                "Enemy could not find the player.",
                this
            );

            enabled = false;
            return;
        }

        player = controller.transform;
    }

    private void Update()
    {
        if (player == null)
        {
            return;
        }

        Vector3 targetPosition = player.position;
        targetPosition.y = transform.position.y;

        Vector3 offset =
            targetPosition - transform.position;

        float distance = offset.magnitude;

        if (offset.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(
                    offset.normalized
                );

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                turningSpeed * Time.deltaTime
            );
        }

        if (distance > stoppingDistance)
        {
            transform.position +=
                transform.forward *
                movementSpeed *
                Time.deltaTime;
        }

        if (
            distance <= shootingRange &&
            Time.time >= nextShotTime
        )
        {
            nextShotTime =
                Time.time + 1f / shotsPerSecond;

            FireProjectile();
        }
    }

    private void FireProjectile()
    {
        Vector3 spawnPosition =
            transform.position +
            Vector3.up * 1.2f +
            transform.forward * 0.8f;

        Vector3 targetPosition =
            player.position + Vector3.up;

        GameObject projectile =
            GameObject.CreatePrimitive(
                PrimitiveType.Sphere
            );

        projectile.name = "Enemy Projectile";
        projectile.transform.position = spawnPosition;
        projectile.transform.localScale =
            Vector3.one * 0.3f;

        SphereCollider projectileCollider =
            projectile.GetComponent<SphereCollider>();

        projectileCollider.isTrigger = true;

        Rigidbody projectileRigidbody =
            projectile.AddComponent<Rigidbody>();

        projectileRigidbody.useGravity = false;
        projectileRigidbody.isKinematic = true;

        Renderer projectileRenderer =
            projectile.GetComponent<Renderer>();

        projectileRenderer.material.color = Color.red;

        EnemyProjectile projectileBehaviour =
            projectile.AddComponent<EnemyProjectile>();

        projectileBehaviour.Initialize(
            targetPosition - spawnPosition,
            projectileSpeed,
            projectileDamage,
            transform
        );
    }
}