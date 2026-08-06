using UnityEngine;

public sealed class PowerSuitAnimationDriver : MonoBehaviour
{
    [SerializeField] private PowerSuitController controller;
    [SerializeField] private Animator animator;

    private static readonly int IsMovingParameter =
        Animator.StringToHash("IsMoving");

    private static readonly int IsFlyingParameter =
        Animator.StringToHash("IsFlying");

    private static readonly int IsAimingParameter =
        Animator.StringToHash("IsAiming");

    private void Awake()
    {
        if (controller == null)
        {
            controller = GetComponent<PowerSuitController>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (controller == null || animator == null)
        {
            Debug.LogError(
                "Could not find the PowerSuitController or Animator.",
                this
            );

            enabled = false;
        }
    }

    private void Update()
    {
        animator.SetBool(
            IsMovingParameter,
            controller.IsMoving
        );

        animator.SetBool(
            IsFlyingParameter,
            controller.IsFlying
        );

        animator.SetBool(
            IsAimingParameter,
            controller.IsAiming
        );
    }
}