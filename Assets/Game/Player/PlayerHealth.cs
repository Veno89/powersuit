using System.Collections;
using UnityEngine;

public sealed class PlayerHealth : MonoBehaviour
{
    [SerializeField] private float maximumHealth = 100f;
    [SerializeField] private float respawnDelay = 2f;

    private float currentHealth;
    private Vector3 startingPosition;
    private Quaternion startingRotation;
    private bool defeated;

    private PowerSuitController movement;
    private PowerSuitWeapon weapon;
    private CharacterController characterController;

    private void Awake()
    {
        currentHealth = maximumHealth;
        startingPosition = transform.position;
        startingRotation = transform.rotation;

        movement = GetComponent<PowerSuitController>();
        weapon = GetComponent<PowerSuitWeapon>();
        characterController = GetComponent<CharacterController>();
    }

    public float CurrentHealth => currentHealth;
    public float MaximumHealth => maximumHealth;
    public event System.Action<float, float> OnHealthChanged;

    public void TakeDamage(float damage)
    {
        if (defeated)
        {
            return;
        }

        currentHealth = Mathf.Max(0f, currentHealth - damage);
        DamageNumberManager.SpawnDamageNumber(transform.position + Vector3.up * 1.8f, damage, isPlayerDamage: true);
        OnHealthChanged?.Invoke(currentHealth, maximumHealth);

        Debug.Log(
            $"Player took {damage} damage. " +
            $"{currentHealth} health remains."
        );

        if (currentHealth <= 0f)
        {
            StartCoroutine(Respawn());
        }
    }

    private IEnumerator Respawn()
    {
        defeated = true;

        if (movement != null)
        {
            movement.enabled = false;
        }

        if (weapon != null)
        {
            weapon.enabled = false;
        }

        yield return new WaitForSeconds(respawnDelay);

        if (characterController != null)
        {
            characterController.enabled = false;
        }

        transform.SetPositionAndRotation(
            startingPosition,
            startingRotation
        );

        if (characterController != null)
        {
            characterController.enabled = true;
        }

        currentHealth = maximumHealth;

        if (movement != null)
        {
            movement.enabled = true;
        }

        if (weapon != null)
        {
            weapon.enabled = true;
        }

        defeated = false;
    }

    private void OnGUI()
    {
        GUI.Label(
            new Rect(20f, 20f, 250f, 30f),
            defeated
                ? "POWER SUIT DISABLED"
                : $"Health: {Mathf.CeilToInt(currentHealth)}"
        );
    }
}