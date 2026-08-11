using UnityEngine;

/// <summary>
/// Lightweight post-animation ground contact for the Generic powered-suit rig.
/// Only a foot already close to a surface is corrected, preserving swing phases.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(11000)]
public sealed class PowerSuitFootPlanting : MonoBehaviour
{
    private const int HitCapacity = 8;

    [SerializeField] private PowerSuitController controller;
    [SerializeField] private Animator animator;
    [SerializeField, Min(0f)] private float rayStartHeight = 0.18f;
    [SerializeField, Min(0.01f)] private float rayDistance = 0.38f;
    [SerializeField, Range(0f, 0.2f)] private float maximumVerticalCorrection = 0.11f;
    [SerializeField, Range(0f, 0.3f)] private float maximumPlanarCorrection = 0.18f;
    [SerializeField, Range(0f, 0.12f)] private float soleClearance = 0.025f;
    [SerializeField, Min(0f)] private float positionSharpness = 24f;
    [SerializeField, Min(0f)] private float rotationSharpness = 18f;

    private readonly RaycastHit[] leftHits = new RaycastHit[HitCapacity];
    private readonly RaycastHit[] rightHits = new RaycastHit[HitCapacity];
    [SerializeField, HideInInspector] private Transform leftFoot;
    [SerializeField, HideInInspector] private Transform rightFoot;
    private readonly FootState leftState = new FootState();
    private readonly FootState rightState = new FootState();

    public Animator Animator => animator;
    public Transform LeftFoot => leftFoot;
    public Transform RightFoot => rightFoot;

    public void Bind(PowerSuitController owner, Animator modelAnimator)
    {
        controller = owner;
        animator = modelAnimator;
        ResolveFeet();
    }

    private void Awake()
    {
        if (controller == null)
        {
            controller = GetComponent<PowerSuitController>();
        }
        ResolveFeet();
    }

    private void OnEnable()
    {
        ResolveFeet();
    }

    private void LateUpdate()
    {
        if (controller == null || animator == null || leftFoot == null || rightFoot == null)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        if (!(deltaTime > 0f) || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
        {
            return;
        }

        bool allowPlanting = controller.IsGrounded && !controller.IsFlying;
        ApplyFoot(leftFoot, leftHits, allowPlanting, deltaTime, leftState);
        ApplyFoot(rightFoot, rightHits, allowPlanting, deltaTime, rightState);
    }

    private void OnDisable()
    {
        leftState.Reset();
        rightState.Reset();
    }

    private void ApplyFoot(
        Transform foot,
        RaycastHit[] hits,
        bool allowPlanting,
        float deltaTime,
        FootState state
    )
    {
        RaycastHit hit = default;
        bool hasSurface = allowPlanting && TryFindSurface(foot, hits, out hit);
        float surfaceHeight = hasSurface ? hit.point.y + soleClearance : foot.position.y;
        float rawVerticalCorrection = surfaceHeight - foot.position.y;
        bool isContact = hasSurface &&
            Mathf.Abs(rawVerticalCorrection) <= maximumVerticalCorrection;
        float desiredOffset = isContact ? rawVerticalCorrection : 0f;
        float desiredRotationWeight = isContact
            ? 1f
            : 0f;

        if (isContact && !state.IsPlanted)
        {
            state.IsPlanted = true;
            state.PlantedWorldPosition = new Vector3(
                foot.position.x,
                surfaceHeight,
                foot.position.z
            );
        }
        else if (!isContact)
        {
            state.IsPlanted = false;
        }

        Vector3 desiredPlanarOffset = state.IsPlanted
            ? Vector3.ProjectOnPlane(
                state.PlantedWorldPosition - foot.position,
                Vector3.up
            )
            : Vector3.zero;
        desiredPlanarOffset = Vector3.ClampMagnitude(
            desiredPlanarOffset,
            maximumPlanarCorrection
        );

        state.VerticalOffset = ExponentialStep(
            state.VerticalOffset,
            desiredOffset,
            positionSharpness,
            deltaTime
        );
        state.PlanarOffset = Vector3.Lerp(
            state.PlanarOffset,
            desiredPlanarOffset,
            1f - Mathf.Exp(-positionSharpness * deltaTime)
        );
        state.RotationWeight = ExponentialStep(
            state.RotationWeight,
            desiredRotationWeight,
            rotationSharpness,
            deltaTime
        );
        foot.position += state.PlanarOffset + Vector3.up * state.VerticalOffset;

        if (isContact && state.RotationWeight > 0.0001f)
        {
            Quaternion surfaceRotation = Quaternion.FromToRotation(foot.up, hit.normal) * foot.rotation;
            foot.rotation = Quaternion.Slerp(foot.rotation, surfaceRotation, state.RotationWeight);
        }
    }

    private bool TryFindSurface(Transform foot, RaycastHit[] hits, out RaycastHit closest)
    {
        Vector3 origin = foot.position + Vector3.up * rayStartHeight;
        int count = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            hits,
            rayDistance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore
        );
        float closestDistance = float.PositiveInfinity;
        closest = default;
        for (int index = 0; index < count; index++)
        {
            RaycastHit candidate = hits[index];
            if (
                candidate.collider == null ||
                candidate.collider.transform.IsChildOf(transform) ||
                candidate.distance >= closestDistance
            )
            {
                continue;
            }
            closest = candidate;
            closestDistance = candidate.distance;
        }
        return closestDistance < float.PositiveInfinity;
    }

    private void ResolveFeet()
    {
        if (animator == null)
        {
            return;
        }
        leftFoot = FindChild(animator.transform, "Foot.L");
        rightFoot = FindChild(animator.transform, "Foot.R");
    }

    private static Transform FindChild(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
            {
                return child;
            }
        }
        return null;
    }

    private static float ExponentialStep(float current, float target, float sharpness, float deltaTime)
    {
        return sharpness > 0f
            ? Mathf.Lerp(current, target, 1f - Mathf.Exp(-sharpness * deltaTime))
            : target;
    }

    private sealed class FootState
    {
        public bool IsPlanted;
        public Vector3 PlantedWorldPosition;
        public Vector3 PlanarOffset;
        public float VerticalOffset;
        public float RotationWeight;

        public void Reset()
        {
            IsPlanted = false;
            PlantedWorldPosition = Vector3.zero;
            PlanarOffset = Vector3.zero;
            VerticalOffset = 0f;
            RotationWeight = 0f;
        }
    }
}

public static class PowerSuitFootPlantingMath
{
    public static float CalculateVerticalCorrection(
        float footHeight,
        float surfaceHeight,
        float maximumCorrection
    )
    {
        if (
            float.IsNaN(footHeight) || float.IsInfinity(footHeight) ||
            float.IsNaN(surfaceHeight) || float.IsInfinity(surfaceHeight) ||
            !(maximumCorrection > 0f)
        )
        {
            return 0f;
        }

        float correction = surfaceHeight - footHeight;
        return Mathf.Abs(correction) <= maximumCorrection ? correction : 0f;
    }
}
