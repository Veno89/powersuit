using UnityEngine;

/// <summary>
/// Bridges accepted weapon-runtime actions to presentation triggers. Combat
/// timing remains owned by the plain-C# weapon state; the Animator only shows it.
/// </summary>
public sealed class PowerSuitWeaponAnimationDriver : MonoBehaviour
{
    public const string ReloadTriggerName = "ReloadWeapon";
    public const string CycleTriggerName = "CycleWeapon";

    [SerializeField] private PowerSuitWeapon weapon;
    [SerializeField] private Animator animator;

    private static readonly int ReloadTrigger =
        Animator.StringToHash(ReloadTriggerName);

    private static readonly int CycleTrigger =
        Animator.StringToHash(CycleTriggerName);

    private bool hasReloadTrigger;
    private bool hasCycleTrigger;
    private bool subscribed;

    private void Awake()
    {
        if (weapon == null)
        {
            weapon = GetComponent<PowerSuitWeapon>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        if (weapon == null || animator == null)
        {
            Debug.LogError(
                "Could not find the PowerSuitWeapon or Animator for weapon animation.",
                this
            );
            enabled = false;
            return;
        }

        hasReloadTrigger = HasTrigger(ReloadTrigger);
        hasCycleTrigger = HasTrigger(CycleTrigger);
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void Start()
    {
        // OnEnable runs before Awake when a component starts enabled. Subscribe
        // again after dependency discovery; the guard keeps this idempotent.
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnReloadStarted()
    {
        if (hasReloadTrigger)
        {
            animator.SetTrigger(ReloadTrigger);
        }
    }

    private void OnCycleStarted()
    {
        if (hasCycleTrigger)
        {
            animator.SetTrigger(CycleTrigger);
        }
    }

    private void Subscribe()
    {
        if (subscribed || weapon == null)
        {
            return;
        }

        weapon.ReloadStarted += OnReloadStarted;
        weapon.CycleStarted += OnCycleStarted;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || weapon == null)
        {
            return;
        }

        weapon.ReloadStarted -= OnReloadStarted;
        weapon.CycleStarted -= OnCycleStarted;
        subscribed = false;
    }

    private bool HasTrigger(int parameterHash)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (
                parameter.nameHash == parameterHash &&
                parameter.type == AnimatorControllerParameterType.Trigger
            )
            {
                return true;
            }
        }

        return false;
    }
}
