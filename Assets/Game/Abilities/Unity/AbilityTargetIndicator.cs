using UnityEngine;

namespace Powersuit.Abilities.UnityAdapters
{
    /// <summary>
    /// Reusable projected-area indicator. Renderer references and the property
    /// block are cached once; moving or recolouring the preview does not create
    /// materials or managed garbage each frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AbilityTargetIndicator : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Transform indicatorRoot;
        [SerializeField] private Renderer[] renderers;
        [SerializeField] private Color validColor =
            new Color(0.2f, 0.9f, 1f, 0.48f);
        [SerializeField] private Color invalidColor =
            new Color(1f, 0.18f, 0.12f, 0.48f);
        [SerializeField, Min(0.001f)] private float surfaceOffset = 0.025f;
        [SerializeField, Min(0.001f)] private float authoredDiameter = 1f;

        private MaterialPropertyBlock propertyBlock;
        private AbilityAreaEffectPresentation areaPresentation;
        private bool visible;

        public bool IsVisible => visible;

        private void Awake()
        {
            CachePresentation();
            EnsureAreaPresentation();
            SetVisible(false);
        }

        public void SetTarget(
            Vector3 point,
            Vector3 surfaceNormal,
            float radius,
            bool isValid
        )
        {
            CachePresentation();
            Vector3 normal = surfaceNormal.sqrMagnitude > 0.000001f
                ? surfaceNormal.normalized
                : Vector3.up;
            Transform root = indicatorRoot != null
                ? indicatorRoot
                : transform;
            root.position = point + normal * surfaceOffset;
            root.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            float diameterScale = Mathf.Max(0.001f, radius * 2f) /
                Mathf.Max(0.001f, authoredDiameter);
            root.localScale = new Vector3(
                diameterScale,
                root.localScale.y,
                diameterScale
            );

            Color color = isValid ? validColor : invalidColor;
            propertyBlock.Clear();
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            foreach (Renderer targetRenderer in renderers)
            {
                if (targetRenderer != null)
                {
                    targetRenderer.SetPropertyBlock(propertyBlock);
                }
            }
            EnsureAreaPresentation();
            areaPresentation.ShowTarget(radius, isValid);
            SetVisible(true);
        }

        public void SetVisible(bool value)
        {
            CachePresentation();
            visible = value;
            foreach (Renderer targetRenderer in renderers)
            {
                if (targetRenderer != null)
                {
                    targetRenderer.enabled = value;
                }
            }
            EnsureAreaPresentation();
            if (!value)
            {
                areaPresentation.HideTarget();
            }
        }

        private void CachePresentation()
        {
            if (indicatorRoot == null)
            {
                indicatorRoot = transform;
            }
            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
            }
            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }
        }

        private void EnsureAreaPresentation()
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

#if UNITY_EDITOR
        private void OnValidate()
        {
            surfaceOffset = Mathf.Max(0.001f, surfaceOffset);
            authoredDiameter = Mathf.Max(0.001f, authoredDiameter);
        }
#endif
    }
}
