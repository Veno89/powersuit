using System;
using UnityEngine;

namespace Powersuit.Abilities.UnityAdapters
{
    /// <summary>
    /// Pooled visual and execution boundary for one void field. The ability's
    /// plain runtime state remains authoritative for tick/final timing; this
    /// actor applies the received commands and presents the active volume.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoidOrbFieldActor : MonoBehaviour, ICombatPoolable
    {
        [SerializeField] private LayerMask recipientMask = ~0;
        [SerializeField, Min(1)] private int queryCapacity = 64;
        [SerializeField, Min(0f)] private float rotationDegreesPerSecond = 70f;
        [SerializeField] private Transform visualRoot;

        private AbilityAreaEffectExecutor executor;
        private bool initialized;
        private float radius;

        public event Action<
            VoidUltimateTickCommand,
            AbilityAreaEffectExecutionResult
        > TickResolved;
        public event Action<AbilityAreaEffectExecutionResult> BurstResolved;

        public bool IsInitialized => initialized;

        public void Initialize(VoidUltimateActivationCommand command)
        {
            transform.position = command.Center;
            transform.rotation = Quaternion.FromToRotation(
                Vector3.up,
                command.SurfaceNormal
            );
            radius = command.Radius;
            initialized = true;
            if (visualRoot != null)
            {
                visualRoot.localScale = Vector3.one * (radius * 2f);
            }
            EnsureExecutor();
        }

        private void Update()
        {
            if (!initialized || visualRoot == null)
            {
                return;
            }

            visualRoot.Rotate(
                Vector3.up,
                rotationDegreesPerSecond * Time.deltaTime,
                Space.Self
            );
        }

        public AbilityAreaEffectExecutionResult ApplyTick(
            VoidUltimateTickCommand command
        )
        {
            EnsureInitialized();
            AbilityAreaEffectExecutionResult result = executor.Execute(
                command.Effect,
                recipientMask,
                QueryTriggerInteraction.Ignore
            );
            TickResolved?.Invoke(command, result);
            return result;
        }

        public AbilityAreaEffectExecutionResult ApplyFinalBurst(
            VoidUltimateBurstCommand command
        )
        {
            EnsureInitialized();
            AbilityAreaEffectExecutionResult result = executor.Execute(
                command.Effect,
                recipientMask,
                QueryTriggerInteraction.Ignore
            );
            BurstResolved?.Invoke(result);
            CombatFeedbackPool.Recycle(gameObject);
            return result;
        }

        public void Cancel()
        {
            if (initialized)
            {
                CombatFeedbackPool.Recycle(gameObject);
            }
        }

        private void EnsureInitialized()
        {
            if (!initialized)
            {
                throw new InvalidOperationException(
                    "The void field must be initialized before applying effects."
                );
            }
            EnsureExecutor();
        }

        private void EnsureExecutor()
        {
            int capacity = Mathf.Max(1, queryCapacity);
            if (executor == null || executor.Capacity != capacity)
            {
                executor = new AbilityAreaEffectExecutor(capacity);
            }
        }

        public void OnPoolSpawned()
        {
            initialized = false;
            radius = 0f;
        }

        public void OnPoolRecycled()
        {
            initialized = false;
            radius = 0f;
            TickResolved = null;
            BurstResolved = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            queryCapacity = Mathf.Max(1, queryCapacity);
            rotationDegreesPerSecond = Mathf.Max(0f, rotationDegreesPerSecond);
        }
#endif
    }
}
