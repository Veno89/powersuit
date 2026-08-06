using UnityEngine;

public sealed class DamageableTarget : MonoBehaviour
{
    [SerializeField] private float maximumHealth = 100f;

    private float currentHealth;
    private Vector3 originalScale;

    public float CurrentHealth => currentHealth;
    public float MaximumHealth => maximumHealth;
    public event System.Action<float, float> OnHealthChanged;

    private void Awake()
    {
        currentHealth = maximumHealth;
        originalScale = transform.localScale;
    }

    public void TakeDamage(float damage)
    {
        currentHealth -= damage;
        OnHealthChanged?.Invoke(currentHealth, maximumHealth);

        DamageNumberManager.SpawnDamageNumber(transform.position + Vector3.up * 1.5f, damage, isPlayerDamage: false);

        transform.localScale = originalScale * 0.9f;
        CancelInvoke(nameof(RestoreScale));
        Invoke(nameof(RestoreScale), 0.08f);

        Debug.Log(
            $"{name} took {damage} damage. " +
            $"{Mathf.Max(currentHealth, 0f)} health remains."
        );

        if (currentHealth <= 0f)
        {
            Destroy(gameObject);
        }
    }

    private void RestoreScale()
    {
        transform.localScale = originalScale;
    }
}