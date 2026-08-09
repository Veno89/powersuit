using UnityEngine;

/// <summary>
/// Keeps the imported FBX Animator GameObject at its authored local transform.
/// Unity's Generic override layers can apply an axis-conversion root pose even
/// when the action clip and AvatarMask contain no Animator-root binding. The
/// wrapper above this object owns world-facing conversion; this component is
/// the runtime boundary that prevents animation evaluation from moving it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(10000)]
public sealed class PowerSuitAnimatorRootLock : MonoBehaviour
{
    private const float PositionScaleEpsilonSquared = 0.0000000001f;
    private const float RotationDotEpsilon = 0.9999999f;

    [SerializeField, HideInInspector] private bool hasLock;
    [SerializeField, HideInInspector] private Vector3 lockedLocalPosition;
    [SerializeField, HideInInspector] private Quaternion lockedLocalRotation =
        Quaternion.identity;
    [SerializeField, HideInInspector] private Vector3 lockedLocalScale =
        Vector3.one;

    public bool HasLock => hasLock;
    public Vector3 LockedLocalPosition => lockedLocalPosition;
    public Quaternion LockedLocalRotation => lockedLocalRotation;
    public Vector3 LockedLocalScale => lockedLocalScale;

    public void CaptureCurrentLocalTransform()
    {
        lockedLocalPosition = transform.localPosition;
        lockedLocalRotation = transform.localRotation;
        lockedLocalScale = transform.localScale;
        hasLock = true;
    }

    public void RestoreNow()
    {
        EnsureLock();

        if (
            (transform.localPosition - lockedLocalPosition).sqrMagnitude >
            PositionScaleEpsilonSquared
        )
        {
            transform.localPosition = lockedLocalPosition;
        }

        if (
            Mathf.Abs(
                Quaternion.Dot(transform.localRotation, lockedLocalRotation)
            ) < RotationDotEpsilon
        )
        {
            transform.localRotation = lockedLocalRotation;
        }

        if (
            (transform.localScale - lockedLocalScale).sqrMagnitude >
            PositionScaleEpsilonSquared
        )
        {
            transform.localScale = lockedLocalScale;
        }
    }

    private void Awake()
    {
        EnsureLock();
    }

    private void OnEnable()
    {
        EnsureLock();
    }

    private void OnAnimatorMove()
    {
        RestoreNow();
    }

    private void LateUpdate()
    {
        RestoreNow();
    }

    private void EnsureLock()
    {
        if (!hasLock)
        {
            CaptureCurrentLocalTransform();
        }
    }
}
