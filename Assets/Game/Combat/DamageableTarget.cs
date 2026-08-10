using System;
using Powersuit.Combat;
using Powersuit.Combat.UnityAdapters;
using UnityEngine;

public sealed class DamageableTarget : MonoBehaviour, IDamageReceiver
{
    [SerializeField] private float maximumHealth = 100f;
    [SerializeField] private float deathDelay = 0.5f;
    [SerializeField] private CombatFaction faction = CombatFaction.Enemy;
    [SerializeField] private bool allowFriendlyFire;

    private float currentHealth;
    private bool isDead;

    public float CurrentHealth => currentHealth;
    public float MaximumHealth => maximumHealth;
    public bool IsDead => isDead;
    public CombatFaction Faction =>
        faction == CombatFaction.None ? CombatFaction.Enemy : faction;
    public bool CanReceiveDamage => !isDead;

    public event Action<float, float> OnHealthChanged;
    public event Action<Vector3, Vector3, float> OnHit;
    public event Action OnDeath;

    private void Awake()
    {
        currentHealth = maximumHealth;
    }

    public void TakeDamage(float damage)
    {
        TakeDamage(damage, transform.position + Vector3.up * 1.2f, -transform.forward);
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        ApplyDamageInternal(damage, hitPoint, hitDirection);
    }

    public DamageResult ApplyDamage(DamageInfo damage)
    {
        if (
            !CanReceiveDamage ||
            !CombatFactionPolicy.CanDamage(
                damage.Faction,
                Faction,
                allowFriendlyFire
            )
        )
        {
            return DamageResult.Ignored;
        }

        return ApplyDamageInternal(
            damage.Amount,
            CombatVectorConversion.ToUnity(damage.Position),
            CombatVectorConversion.ToUnity(damage.Direction)
        );
    }

    private DamageResult ApplyDamageInternal(
        float damage,
        Vector3 hitPoint,
        Vector3 hitDirection
    )
    {
        float requestedDamage = Mathf.Max(0f, damage);
        if (isDead || requestedDamage <= 0f)
        {
            return DamageResult.Ignored;
        }

        float healthBefore = currentHealth;
        currentHealth = Mathf.Max(0f, currentHealth - requestedDamage);
        float appliedDamage = healthBefore - currentHealth;
        OnHealthChanged?.Invoke(currentHealth, maximumHealth);
        OnHit?.Invoke(hitPoint, hitDirection, appliedDamage);

        DamageNumberManager.SpawnDamageNumber(
            hitPoint + Vector3.up * 0.3f,
            appliedDamage,
            isPlayerDamage: false
        );

        EnemyHitReaction reaction = GetComponent<EnemyHitReaction>();
        if (reaction != null && currentHealth > 0f)
        {
            reaction.TriggerReaction(hitDirection);
        }

        if (currentHealth <= 0f && !isDead)
        {
            HandleDeath();
        }

        return DamageResult.Applied(appliedDamage, isDead);
    }

    private void HandleDeath()
    {
        isDead = true;
        currentHealth = 0f;

        OnDeath?.Invoke();

        // 1. Hide EnemyHealthBar immediately
        EnemyHealthBar healthBar = GetComponentInChildren<EnemyHealthBar>(true);
        if (healthBar != null)
        {
            if (healthBar.Canvas != null)
            {
                healthBar.Canvas.enabled = false;
            }
            healthBar.enabled = false;
        }

        // 2. Disable main collider so corpse doesn't block shots/movement
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
        }

        // 3. Notify SimpleEnemy to stop AI, movement, and shooting
        SimpleEnemy enemyAI = GetComponent<SimpleEnemy>();
        if (enemyAI != null)
        {
            enemyAI.HandleDeathSequence(deathDelay);
        }
        else
        {
            Destroy(gameObject, deathDelay);
        }
    }
}
