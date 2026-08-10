using UnityEngine;

namespace Powersuit.Enemies.UnityAdapters
{
    /// <summary>
    /// Lightweight pooled-enemy health presentation. The authored mesh bar
    /// follows the enemy without Canvas rebuilds, subscribes only while the
    /// pooled instance is active, and hides on death or beyond its cull range.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyHealthBarPresenter : MonoBehaviour
    {
        [SerializeField] private EnemyArchetypeController target;
        [SerializeField] private Transform barRoot;
        [SerializeField] private Transform fill;
        [SerializeField, Min(0f)] private float maximumDisplayDistance = 55f;

        private Camera activeCamera;
        private Vector3 fillFullScale = Vector3.one;
        private Vector3 fillFullPosition;
        private bool subscribed;

        public EnemyArchetypeController Target => target;
        public Transform BarRoot => barRoot;
        public Transform Fill => fill;

        public void Configure(
            EnemyArchetypeController healthTarget,
            Transform presentationRoot,
            Transform fillTransform,
            float displayDistance = 55f
        )
        {
            target = healthTarget;
            barRoot = presentationRoot;
            fill = fillTransform;
            maximumDisplayDistance = Mathf.Max(0f, displayDistance);
            CaptureFillPose();

            if (isActiveAndEnabled)
            {
                Subscribe();
                Refresh();
            }
        }

        private void Awake()
        {
            target ??= GetComponent<EnemyArchetypeController>();
            CaptureFillPose();
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void LateUpdate()
        {
            if (barRoot == null || target == null)
            {
                return;
            }

            activeCamera ??= Camera.main;
            bool visible = !target.IsDead;
            if (visible && activeCamera != null && maximumDisplayDistance > 0f)
            {
                visible = Vector3.SqrMagnitude(
                    activeCamera.transform.position - target.transform.position
                ) <= maximumDisplayDistance * maximumDisplayDistance;
            }

            if (barRoot.gameObject.activeSelf != visible)
            {
                barRoot.gameObject.SetActive(visible);
            }

            if (visible && activeCamera != null)
            {
                barRoot.rotation = Quaternion.LookRotation(
                    barRoot.position - activeCamera.transform.position,
                    activeCamera.transform.up
                );
            }
        }

        private void Subscribe()
        {
            if (subscribed || target == null)
            {
                return;
            }

            target.HealthChanged += HandleHealthChanged;
            target.Died += HandleDied;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || target == null)
            {
                subscribed = false;
                return;
            }

            target.HealthChanged -= HandleHealthChanged;
            target.Died -= HandleDied;
            subscribed = false;
        }

        private void HandleHealthChanged(float current, float maximum)
        {
            SetFill(maximum > 0f ? current / maximum : 0f);
            if (barRoot != null && target != null && !target.IsDead)
            {
                barRoot.gameObject.SetActive(true);
            }
        }

        private void HandleDied()
        {
            if (barRoot != null)
            {
                barRoot.gameObject.SetActive(false);
            }
        }

        private void Refresh()
        {
            if (target == null)
            {
                return;
            }

            HandleHealthChanged(target.CurrentHealth, target.MaximumHealth);
        }

        private void CaptureFillPose()
        {
            if (fill == null)
            {
                return;
            }

            fillFullScale = fill.localScale;
            fillFullPosition = fill.localPosition;
        }

        private void SetFill(float fraction)
        {
            if (fill == null)
            {
                return;
            }

            float normalized = Mathf.Clamp01(fraction);
            Vector3 scale = fillFullScale;
            scale.x *= normalized;
            fill.localScale = scale;

            Vector3 position = fillFullPosition;
            position.x -= fillFullScale.x * (1f - normalized) * 0.5f;
            fill.localPosition = position;
        }
    }
}
