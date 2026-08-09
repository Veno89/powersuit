using UnityEngine;

/// <summary>
/// Bridges accepted weapon-runtime actions to presentation triggers. Combat
/// timing remains owned by the plain-C# weapon state; the Animator only shows it.
/// </summary>
public sealed class PowerSuitWeaponAnimationDriver : MonoBehaviour
{
    public const string ReloadTriggerName = "ReloadWeapon";
    public const string CycleTriggerName = "CycleWeapon";
    public const string WeaponActionLayerName = "Weapon Actions";
    public const string NoWeaponActionStateName = "No Weapon Action";

    [SerializeField] private PowerSuitWeapon weapon;
    [SerializeField] private Animator animator;

    private static readonly int ReloadTrigger =
        Animator.StringToHash(ReloadTriggerName);

    private static readonly int CycleTrigger =
        Animator.StringToHash(CycleTriggerName);

    private static readonly int NoWeaponActionState =
        Animator.StringToHash(NoWeaponActionStateName);

    private bool hasReloadTrigger;
    private bool hasCycleTrigger;
    private bool subscribed;
    private bool actionRequestedThisFrame;
    private int weaponActionLayerIndex = -1;

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
        weaponActionLayerIndex = animator.GetLayerIndex(WeaponActionLayerName);

        // A full-weight Generic override layer can retain the last upper-body
        // pose after returning to a motionless state. Start neutral and only
        // give the layer weight while an action is actually playing.
        ReleaseWeaponActionLayer();
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

    private void Update()
    {
        SynchronizeWeaponActionLayer();
    }

    private void OnDisable()
    {
        Unsubscribe();
        ReleaseWeaponActionLayer();
    }

    private void OnReloadStarted()
    {
        if (hasReloadTrigger)
        {
            BeginWeaponAction();
            animator.SetTrigger(ReloadTrigger);
        }
    }

    private void OnCycleStarted()
    {
        if (hasCycleTrigger)
        {
            BeginWeaponAction();
            animator.SetTrigger(CycleTrigger);
        }
    }

    /// <summary>
    /// Enables the masked override layer immediately before an action trigger.
    /// Presentation adapters use this for draw and sheathe; reload and cycle
    /// call it through their accepted weapon-runtime events above.
    /// </summary>
    public void BeginWeaponAction()
    {
        if (animator == null || weaponActionLayerIndex < 0)
        {
            return;
        }

        actionRequestedThisFrame = true;
        animator.SetLayerWeight(weaponActionLayerIndex, 1f);
    }

    private void SynchronizeWeaponActionLayer()
    {
        if (animator == null || weaponActionLayerIndex < 0)
        {
            return;
        }

        // Presentation runs before this adapter. Do not immediately undo a
        // BeginWeaponAction call while its trigger is still waiting for the
        // Animator's evaluation later in the same frame.
        if (actionRequestedThisFrame)
        {
            actionRequestedThisFrame = false;
            animator.SetLayerWeight(weaponActionLayerIndex, 1f);
            return;
        }

        if (!animator.isInitialized)
        {
            return;
        }

        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(
            weaponActionLayerIndex
        );
        bool isStableEmpty =
            !animator.IsInTransition(weaponActionLayerIndex) &&
            current.shortNameHash == NoWeaponActionState;

        animator.SetLayerWeight(
            weaponActionLayerIndex,
            isStableEmpty ? 0f : 1f
        );
    }

    private void ReleaseWeaponActionLayer()
    {
        actionRequestedThisFrame = false;
        if (animator != null && weaponActionLayerIndex >= 0)
        {
            animator.SetLayerWeight(weaponActionLayerIndex, 0f);
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
