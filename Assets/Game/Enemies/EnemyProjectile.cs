using Powersuit.Combat;
using Powersuit.Combat.UnityAdapters;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public sealed class EnemyProjectile : MonoBehaviour, ICombatProjectilePoolable
{
    [SerializeField, Min(0.01f)] private float maximumLifetime = 5f;

    private float speed;
    private float damage;
    private Transform sourceRoot;
    private Vector3 direction;
    private CombatFaction sourceFaction = CombatFaction.Enemy;
    private float age;
    private bool initialized;

    public void Initialize(
        Vector3 travelDirection,
        float travelSpeed,
        float projectileDamage,
        Transform source,
        CombatFaction faction = CombatFaction.Enemy
    )
    {
        direction = travelDirection.normalized;
        speed = travelSpeed;
        damage = projectileDamage;
        sourceRoot = source;
        sourceFaction = faction;
        age = 0f;
        initialized = true;
    }

    private void FixedUpdate()
    {
        if (!initialized)
        {
            return;
        }

        age += Time.fixedDeltaTime;
        if (age >= maximumLifetime)
        {
            RecycleSelf();
            return;
        }

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

        IDamageReceiver receiver =
            other.GetComponentInParent<IDamageReceiver>();
        if (receiver != null)
        {
            receiver.ApplyDamage(
                new DamageInfo(
                    sourceRoot != null ? sourceRoot.gameObject : gameObject,
                    sourceFaction,
                    DamageType.Kinetic,
                    damage,
                    CombatVectorConversion.ToCombat(transform.position),
                    CombatVectorConversion.ToCombat(direction)
                )
            );
        }

        RecycleSelf();
    }

    public void OnPoolSpawned()
    {
        ResetTransientState();
    }

    public void OnPoolRecycled()
    {
        ResetTransientState();
    }

    private void ResetTransientState()
    {
        speed = 0f;
        damage = 0f;
        sourceRoot = null;
        direction = Vector3.zero;
        sourceFaction = CombatFaction.Enemy;
        age = 0f;
        initialized = false;
    }

    private void RecycleSelf()
    {
        initialized = false;
        CombatFeedbackPool.Recycle(gameObject);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maximumLifetime = Mathf.Max(0.01f, maximumLifetime);
    }
#endif
}
