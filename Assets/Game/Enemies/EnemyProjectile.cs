using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public sealed class EnemyProjectile : MonoBehaviour
{
    private float speed;
    private float damage;
    private Transform sourceRoot;
    private Vector3 direction;

    public void Initialize(
        Vector3 travelDirection,
        float travelSpeed,
        float projectileDamage,
        Transform source
    )
    {
        direction = travelDirection.normalized;
        speed = travelSpeed;
        damage = projectileDamage;
        sourceRoot = source;

        Destroy(gameObject, 5f);
    }

    private void FixedUpdate()
    {
        transform.position +=
            direction * speed * Time.fixedDeltaTime;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (
            sourceRoot != null &&
            (
                other.transform == sourceRoot ||
                other.transform.IsChildOf(sourceRoot)
            )
        )
        {
            return;
        }

        PlayerHealth player =
            other.GetComponentInParent<PlayerHealth>();

        if (player != null)
        {
            player.TakeDamage(damage);
        }

        Destroy(gameObject);
    }
}