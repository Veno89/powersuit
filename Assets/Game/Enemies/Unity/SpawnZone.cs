using System;
using Powersuit.Combat;
using UnityEngine;

namespace Powersuit.Enemies.UnityAdapters
{
    /// <summary>
    /// Explicit authoring and physics adapter for spawn candidates. The global
    /// director can gather these snapshots into a caller-owned buffer without
    /// learning about cameras, transforms, or Physics APIs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnZone : MonoBehaviour
    {
        private const float MinimumBoundsSize = 0.01f;
        private const float ProbeEpsilon = 0.001f;

        [Header("Identity")]
        [SerializeField] private string zoneId = "spawn-zone";
        [SerializeField] private SpawnZoneCompatibility compatibility =
            SpawnZoneCompatibility.Ground;
        [SerializeField] private bool candidatesEnabled = true;

        [Header("Authored candidates")]
        [Tooltip("When empty, the bounds center is used as one candidate.")]
        [SerializeField] private Transform[] spawnPoints = Array.Empty<Transform>();
        [SerializeField] private Vector3 localBoundsCenter;
        [SerializeField] private Vector3 localBoundsSize = new Vector3(12f, 8f, 12f);

        [Header("Ground validation")]
        [SerializeField] private bool requireGroundSurface = true;
        [Min(0f)] [SerializeField] private float groundProbeHeight = 2f;
        [Min(0.01f)] [SerializeField] private float groundProbeDistance = 5f;
        [Range(0f, 89f)] [SerializeField] private float maximumGroundSlope = 50f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Visibility and clearance")]
        [Min(0f)] [SerializeField] private float clearanceRadius = 0.75f;
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField] private LayerMask visibilityOcclusionMask = ~0;
        [SerializeField] private Vector3 visibilityProbeOffset = Vector3.up;

        private readonly RaycastHit[] raycastHits = new RaycastHit[12];
        private readonly Collider[] overlapHits = new Collider[16];
        private string[] candidateIds = Array.Empty<string>();

        public string ZoneId => zoneId;
        public SpawnZoneCompatibility Compatibility => compatibility;
        public bool CandidatesEnabled => candidatesEnabled && isActiveAndEnabled;
        public Bounds LocalBounds => new Bounds(localBoundsCenter, localBoundsSize);
        public int CandidateCapacity => spawnPoints != null && spawnPoints.Length > 0
            ? spawnPoints.Length
            : 1;

        private void Awake()
        {
            RebuildCandidateIds();
        }

        /// <summary>
        /// Runtime/test initialization boundary. Values are copied once so the
        /// director may safely reuse its candidate buffers.
        /// </summary>
        public void Configure(
            string id,
            SpawnZoneCompatibility zoneCompatibility,
            Transform[] explicitPoints,
            Bounds bounds,
            bool requireGroundProbe = true
        )
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A non-empty zone id is required.", nameof(id));
            }

            if (
                zoneCompatibility == SpawnZoneCompatibility.None ||
                (zoneCompatibility & ~SpawnZoneCompatibility.GroundAndFlight) != 0
            )
            {
                throw new ArgumentOutOfRangeException(nameof(zoneCompatibility));
            }

            zoneId = id;
            compatibility = zoneCompatibility;
            spawnPoints = explicitPoints != null
                ? (Transform[])explicitPoints.Clone()
                : Array.Empty<Transform>();
            localBoundsCenter = bounds.center;
            localBoundsSize = new Vector3(
                Mathf.Max(MinimumBoundsSize, Mathf.Abs(bounds.size.x)),
                Mathf.Max(MinimumBoundsSize, Mathf.Abs(bounds.size.y)),
                Mathf.Max(MinimumBoundsSize, Mathf.Abs(bounds.size.z))
            );
            requireGroundSurface = requireGroundProbe;
            RebuildCandidateIds();
        }

        public int FillCandidates(
            SpawnPointCandidate[] output,
            int startIndex,
            Camera viewCamera = null
        )
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            if (startIndex < 0 || startIndex > output.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }

            int written = 0;
            int capacity = CandidateCapacity;
            for (
                int pointIndex = 0;
                pointIndex < capacity && startIndex + written < output.Length;
                pointIndex++
            )
            {
                if (TryBuildCandidate(pointIndex, viewCamera, out SpawnPointCandidate candidate))
                {
                    output[startIndex + written] = candidate;
                    written++;
                }
            }

            return written;
        }

        public bool TryBuildCandidate(
            int pointIndex,
            Camera viewCamera,
            out SpawnPointCandidate candidate
        )
        {
            if (pointIndex < 0 || pointIndex >= CandidateCapacity)
            {
                candidate = default;
                return false;
            }

            EnsureCandidateIds();
            bool hasExplicitPoints = spawnPoints != null && spawnPoints.Length > 0;
            Transform point = hasExplicitPoints
                ? spawnPoints[pointIndex]
                : null;
            Vector3 position = point != null
                ? point.position
                : transform.TransformPoint(localBoundsCenter);
            bool pointEnabled = !hasExplicitPoints ||
                (point != null && point.gameObject.activeInHierarchy);
            bool insideBounds = ContainsWorldPoint(position);
            bool groundValid =
                (compatibility & SpawnZoneCompatibility.Ground) == 0 ||
                (insideBounds && IsGroundPositionValid(position));
            bool flightValid =
                (compatibility & SpawnZoneCompatibility.Flight) == 0 ||
                insideBounds;

            candidate = new SpawnPointCandidate(
                candidateIds[pointIndex],
                ToCombatVector(position),
                compatibility,
                isEnabled: CandidatesEnabled && pointEnabled,
                isInsideCameraView: IsInsideCameraView(viewCamera, position),
                isGroundPositionValid: groundValid,
                isWithinFlightBounds: flightValid,
                isObstacleFree: IsObstacleFree(position)
            );
            return true;
        }

        public bool ContainsWorldPoint(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition) - localBoundsCenter;
            Vector3 extents = localBoundsSize * 0.5f;
            return
                Mathf.Abs(local.x) <= extents.x + ProbeEpsilon &&
                Mathf.Abs(local.y) <= extents.y + ProbeEpsilon &&
                Mathf.Abs(local.z) <= extents.z + ProbeEpsilon;
        }

        public bool IsGroundPositionValid(Vector3 worldPosition)
        {
            if (!requireGroundSurface)
            {
                return true;
            }

            Vector3 origin = worldPosition + Vector3.up * groundProbeHeight;
            int hitCount = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                raycastHits,
                groundProbeHeight + groundProbeDistance,
                groundMask,
                QueryTriggerInteraction.Ignore
            );
            float minimumGroundDot = Mathf.Cos(maximumGroundSlope * Mathf.Deg2Rad);
            float nearestDistance = float.PositiveInfinity;
            bool validSurface = false;

            for (int index = 0; index < hitCount; index++)
            {
                Collider collider = raycastHits[index].collider;
                if (collider == null || IsOwnedCollider(collider))
                {
                    continue;
                }

                float hitDistance = raycastHits[index].distance;
                if (hitDistance < nearestDistance)
                {
                    nearestDistance = hitDistance;
                    validSurface = Vector3.Dot(raycastHits[index].normal, Vector3.up)
                        >= minimumGroundDot;
                }
            }

            return validSurface;
        }

        public bool IsObstacleFree(Vector3 worldPosition)
        {
            if (clearanceRadius <= ProbeEpsilon || obstacleMask.value == 0)
            {
                return true;
            }

            int count = Physics.OverlapSphereNonAlloc(
                worldPosition,
                clearanceRadius,
                overlapHits,
                obstacleMask,
                QueryTriggerInteraction.Ignore
            );
            for (int index = 0; index < count; index++)
            {
                Collider collider = overlapHits[index];
                if (collider != null && !IsOwnedCollider(collider))
                {
                    return false;
                }
            }

            return true;
        }

        public bool IsInsideCameraView(Camera viewCamera, Vector3 worldPosition)
        {
            if (viewCamera == null || !viewCamera.isActiveAndEnabled)
            {
                return false;
            }

            Vector3 viewport = viewCamera.WorldToViewportPoint(
                worldPosition + visibilityProbeOffset
            );
            if (
                viewport.z <= 0f ||
                viewport.x < 0f || viewport.x > 1f ||
                viewport.y < 0f || viewport.y > 1f
            )
            {
                return false;
            }

            Vector3 origin = viewCamera.transform.position;
            Vector3 delta = worldPosition + visibilityProbeOffset - origin;
            float distance = delta.magnitude;
            if (distance <= ProbeEpsilon || visibilityOcclusionMask.value == 0)
            {
                return true;
            }

            int hitCount = Physics.RaycastNonAlloc(
                origin,
                delta / distance,
                raycastHits,
                distance,
                visibilityOcclusionMask,
                QueryTriggerInteraction.Ignore
            );
            for (int index = 0; index < hitCount; index++)
            {
                Collider collider = raycastHits[index].collider;
                if (collider != null && !IsOwnedCollider(collider))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsOwnedCollider(Collider collider)
        {
            Transform colliderTransform = collider.transform;
            return colliderTransform == transform || colliderTransform.IsChildOf(transform);
        }

        private void EnsureCandidateIds()
        {
            if (candidateIds == null || candidateIds.Length != CandidateCapacity)
            {
                RebuildCandidateIds();
            }
        }

        private void RebuildCandidateIds()
        {
            int count = CandidateCapacity;
            candidateIds = new string[count];
            if (count == 1 && (spawnPoints == null || spawnPoints.Length == 0))
            {
                candidateIds[0] = zoneId;
                return;
            }

            for (int index = 0; index < count; index++)
            {
                candidateIds[index] = zoneId + ":" + index;
            }
        }

        private static CombatVector3 ToCombatVector(Vector3 value)
        {
            return new CombatVector3(value.x, value.y, value.z);
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(zoneId))
            {
                zoneId = "spawn-zone";
            }

            if (
                compatibility == SpawnZoneCompatibility.None ||
                (compatibility & ~SpawnZoneCompatibility.GroundAndFlight) != 0
            )
            {
                compatibility = SpawnZoneCompatibility.Ground;
            }

            localBoundsSize = new Vector3(
                Mathf.Max(MinimumBoundsSize, Mathf.Abs(localBoundsSize.x)),
                Mathf.Max(MinimumBoundsSize, Mathf.Abs(localBoundsSize.y)),
                Mathf.Max(MinimumBoundsSize, Mathf.Abs(localBoundsSize.z))
            );
            groundProbeHeight = Mathf.Max(0f, groundProbeHeight);
            groundProbeDistance = Mathf.Max(0.01f, groundProbeDistance);
            maximumGroundSlope = Mathf.Clamp(maximumGroundSlope, 0f, 89f);
            clearanceRadius = Mathf.Max(0f, clearanceRadius);
            RebuildCandidateIds();
        }

        private void OnDrawGizmosSelected()
        {
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = compatibility == SpawnZoneCompatibility.Flight
                ? new Color(0.2f, 0.7f, 1f, 0.65f)
                : new Color(0.3f, 1f, 0.4f, 0.65f);
            Gizmos.DrawWireCube(localBoundsCenter, localBoundsSize);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
