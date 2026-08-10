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
    public const string BoltCycleLayerName = "Bolt Cycle Action";
    public const string NoBoltCycleStateName = "No Bolt Cycle";

    [SerializeField] private PowerSuitWeapon weapon;
    [SerializeField] private Animator animator;
    [SerializeField, Min(0f)] private float postShotPoseHoldDuration = 0.25f;

    private static readonly int ReloadTrigger =
        Animator.StringToHash(ReloadTriggerName);

    private static readonly int CycleTrigger =
        Animator.StringToHash(CycleTriggerName);

    private static readonly int NoWeaponActionState =
        Animator.StringToHash(NoWeaponActionStateName);

    private static readonly int NoBoltCycleState =
        Animator.StringToHash(NoBoltCycleStateName);

    private bool hasReloadTrigger;
    private bool hasCycleTrigger;
    private bool subscribed;
    private bool actionRequestedThisFrame;
    private bool cycleRequestedThisFrame;
    private bool cycleInProgress;
    private float forwardPoseHoldRemaining;
    private int forwardWeaponPoseLayerIndex = -1;
    private int weaponActionLayerIndex = -1;
    private int boltCycleLayerIndex = -1;

    public bool RequiresForwardWeaponPose =>
        (weapon != null && weapon.IsCycling) ||
        cycleInProgress ||
        forwardPoseHoldRemaining > 0f;

    private void Awake()
    {
        ResolveDependencies();

        if (weapon == null || animator == null)
        {
            Debug.LogError(
                "Could not find the PowerSuitWeapon or Animator for weapon animation.",
                this
            );
            enabled = false;
            return;
        }

        CacheAnimatorBindings();
    }

    private void ResolveDependencies()
    {
        if (weapon == null)
        {
            weapon = GetComponent<PowerSuitWeapon>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }
    }

    private void CacheAnimatorBindings()
    {
        hasReloadTrigger = HasTrigger(ReloadTrigger);
        hasCycleTrigger = HasTrigger(CycleTrigger);
        forwardWeaponPoseLayerIndex = animator.GetLayerIndex(
            PowerSuitAnimationDriver.ForwardWeaponPoseLayerName
        );
        weaponActionLayerIndex = animator.GetLayerIndex(WeaponActionLayerName);
        boltCycleLayerIndex = animator.GetLayerIndex(BoltCycleLayerName);

        // A full-weight Generic override layer can retain the last upper-body
        // pose after returning to a motionless state. Start neutral and only
        // give the layer weight while an action is actually playing.
        ReleaseWeaponActionLayer();
        ReleaseBoltCycleLayer();
        cycleInProgress = weapon != null && weapon.IsCycling;
        if (cycleInProgress)
        {
            RefreshForwardWeaponPose();
        }
    }

    private void OnEnable()
    {
        // Recover nonserialized layer/parameter caches after a Play Mode
        // script reload before subscribing to runtime weapon events.
        ResolveDependencies();
        if (weapon != null && animator != null)
        {
            CacheAnimatorBindings();
        }
        Subscribe();
    }

    private void Start()
    {
        // The guard keeps dependency/reload subscription idempotent.
        Subscribe();
    }

    private void Update()
    {
        if (forwardPoseHoldRemaining > 0f)
        {
            forwardPoseHoldRemaining = Mathf.Max(
                0f,
                forwardPoseHoldRemaining - Time.deltaTime
            );
        }

        SynchronizeWeaponActionLayer();
        SynchronizeBoltCycleLayer();
    }

    private void OnDisable()
    {
        Unsubscribe();
        ReleaseWeaponActionLayer();
        ReleaseBoltCycleLayer();
        cycleInProgress = false;
        forwardPoseHoldRemaining = 0f;
    }

    private void OnReloadStarted()
    {
        if (hasReloadTrigger)
        {
            cycleInProgress = false;
            forwardPoseHoldRemaining = 0f;
            ReleaseBoltCycleLayer();
            BeginWeaponAction();
            animator.SetTrigger(ReloadTrigger);
        }
    }

    private void OnCycleStarted()
    {
        cycleInProgress = true;
        RefreshForwardWeaponPose();
        if (hasCycleTrigger)
        {
            BeginBoltCycle();
            animator.SetTrigger(CycleTrigger);
        }
    }

    private void OnShotAccepted(Powersuit.Combat.WeaponFireResult result)
    {
        RefreshForwardWeaponPose();
    }

    private void OnCycleCompleted()
    {
        cycleInProgress = false;
        RefreshForwardWeaponPose();
    }

    private void RefreshForwardWeaponPose()
    {
        forwardPoseHoldRemaining = Mathf.Max(
            forwardPoseHoldRemaining,
            postShotPoseHoldDuration
        );
    }

    /// <summary>
    /// Gives a non-aim input shot one Animator evaluation with the rifle in its
    /// forward pose before gameplay samples the animated muzzle. Returns false
    /// when the generated forward-pose layer is not available, allowing the
    /// weapon adapter to fall back to immediate fire.
    /// </summary>
    public bool PrepareForwardWeaponPose()
    {
        if (animator == null || forwardWeaponPoseLayerIndex < 0)
        {
            return false;
        }

        RefreshForwardWeaponPose();
        animator.SetLayerWeight(forwardWeaponPoseLayerIndex, 1f);
        return true;
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

    private void BeginBoltCycle()
    {
        if (animator == null || boltCycleLayerIndex < 0)
        {
            return;
        }

        cycleRequestedThisFrame = true;
        animator.SetLayerWeight(boltCycleLayerIndex, 1f);
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

    private void SynchronizeBoltCycleLayer()
    {
        if (animator == null || boltCycleLayerIndex < 0)
        {
            return;
        }

        if (cycleRequestedThisFrame)
        {
            cycleRequestedThisFrame = false;
            animator.SetLayerWeight(boltCycleLayerIndex, 1f);
            return;
        }

        if (!animator.isInitialized)
        {
            return;
        }

        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(
            boltCycleLayerIndex
        );
        bool isStableEmpty =
            !animator.IsInTransition(boltCycleLayerIndex) &&
            current.shortNameHash == NoBoltCycleState;
        animator.SetLayerWeight(
            boltCycleLayerIndex,
            isStableEmpty ? 0f : 1f
        );
    }

    private void ReleaseBoltCycleLayer()
    {
        cycleRequestedThisFrame = false;
        if (animator != null && boltCycleLayerIndex >= 0)
        {
            animator.SetLayerWeight(boltCycleLayerIndex, 0f);
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
        weapon.CycleCompleted += OnCycleCompleted;
        weapon.ShotAccepted += OnShotAccepted;
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
        weapon.CycleCompleted -= OnCycleCompleted;
        weapon.ShotAccepted -= OnShotAccepted;
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
