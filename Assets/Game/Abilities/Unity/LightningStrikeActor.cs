using System;
using UnityEngine;

namespace Powersuit.Abilities.UnityAdapters
{
    /// <summary>
    /// Pooled lightning telegraph/strike actor. The cast location is fixed at
    /// release; after a short readable telegraph it executes exactly one area
    /// transaction and remains briefly for presentation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LightningStrikeActor : MonoBehaviour, ICombatPoolable
    {
        [SerializeField, Min(0f)] private float telegraphSeconds = 0.2f;
        [SerializeField, Min(0.01f)] private float visibleSeconds = 0.45f;
        [SerializeField] private LayerMask recipientMask = ~0;
        [SerializeField, Min(1)] private int queryCapacity = 64;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private AbilityAreaEffectPresentation areaPresentation;

        private AbilityAreaEffectExecutor executor;
        private LightningAreaCastCommand command;
        private float elapsed;
        private bool initialized;
        private bool resolved;

        public event Action<AbilityAreaEffectExecutionResult> StrikeResolved;

        public bool IsInitialized => initialized;
        public bool HasResolved => resolved;
        public AbilityAreaEffectPresentation AreaPresentation => areaPresentation;

        private void Awake()
        {
            EnsurePresentation();
        }

        public void Initialize(LightningAreaCastCommand castCommand)
        {
            command = castCommand;
            elapsed = 0f;
            resolved = false;
            initialized = true;
            transform.position = castCommand.Center;
            transform.rotation = Quaternion.FromToRotation(
                Vector3.up,
                castCommand.SurfaceNormal
            );
            if (visualRoot != null)
            {
                float authoredThickness = Mathf.Max(
                    0.01f,
                    visualRoot.localScale.y
                );
                visualRoot.localScale = new Vector3(
                    castCommand.Radius * 2f,
                    authoredThickness,
                    castCommand.Radius * 2f
                );
            }
            EnsurePresentation();
            areaPresentation.BeginTelegraph(
                castCommand.Radius,
                telegraphSeconds,
                AbilityAreaPresentationStyle.Lightning
            );
            EnsureExecutor();
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            elapsed += Time.deltaTime;
            if (!resolved && elapsed >= telegraphSeconds)
            {
                resolved = true;
                AbilityAreaEffectExecutionResult result = executor.Execute(
                    command.Effect,
                    recipientMask,
                    QueryTriggerInteraction.Ignore
                );
                EnsurePresentation();
                areaPresentation.PlayImpact(
                    command.Radius,
                    visibleSeconds,
                    AbilityAreaPresentationStyle.Lightning
                );
                StrikeResolved?.Invoke(result);
            }

            if (elapsed >= telegraphSeconds + visibleSeconds)
            {
                CombatFeedbackPool.Recycle(gameObject);
            }
        }

        private void EnsureExecutor()
        {
            int capacity = Mathf.Max(1, queryCapacity);
            if (executor == null || executor.Capacity != capacity)
            {
                executor = new AbilityAreaEffectExecutor(capacity);
            }
        }

        private void EnsurePresentation()
        {
            if (areaPresentation == null)
            {
                areaPresentation = GetComponent<AbilityAreaEffectPresentation>();
            }
            if (areaPresentation == null)
            {
                areaPresentation = gameObject.AddComponent<
                    AbilityAreaEffectPresentation
                >();
            }
        }

        public void OnPoolSpawned()
        {
            initialized = false;
            resolved = false;
            elapsed = 0f;
            EnsurePresentation();
            areaPresentation.ResetPresentation();
        }

        public void OnPoolRecycled()
        {
            initialized = false;
            resolved = false;
            elapsed = 0f;
            command = default;
            StrikeResolved = null;
            if (areaPresentation != null)
            {
                areaPresentation.ResetPresentation();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            telegraphSeconds = Mathf.Max(0f, telegraphSeconds);
            visibleSeconds = Mathf.Max(0.01f, visibleSeconds);
            queryCapacity = Mathf.Max(1, queryCapacity);
        }
#endif
    }
}
