using System.Collections;
using UnityEngine;

public sealed class EnemyHitReaction : MonoBehaviour
{
    [Header("Flash Settings")]
    [SerializeField] private Color flashColor = new Color(1f, 0.4f, 0.4f, 1f);
    [SerializeField] private float flashDuration = 0.1f;

    [Header("Flinch Settings")]
    [SerializeField] private float flinchDistance = 0.15f;
    [SerializeField] private float flinchDuration = 0.12f;

    private Renderer[] cachedRenderers;
    private MaterialPropertyBlock propertyBlock;
    private Transform visualChild;
    private Vector3 initialChildLocalPosition;

    private Coroutine flashCoroutine;
    private Coroutine flinchCoroutine;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        propertyBlock = new MaterialPropertyBlock();

        Transform visual = transform.Find("VisualRoot") ?? transform.Find("Model");
        if (visual == null && transform.childCount > 0)
        {
            foreach (Transform child in transform)
            {
                if (child.name != "HealthBarUI" && child.GetComponent<Canvas>() == null)
                {
                    visual = child;
                    break;
                }
            }
        }

        visualChild = visual;
        if (visualChild != null)
        {
            initialChildLocalPosition = visualChild.localPosition;
        }
    }

    public void TriggerReaction(Vector3 hitDirection)
    {
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
        }
        flashCoroutine = StartCoroutine(DoFlash());

        if (visualChild != null)
        {
            if (flinchCoroutine != null)
            {
                StopCoroutine(flinchCoroutine);
            }
            flinchCoroutine = StartCoroutine(DoFlinch(hitDirection));
        }
    }

    public void StopReaction()
    {
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }

        if (flinchCoroutine != null)
        {
            StopCoroutine(flinchCoroutine);
            flinchCoroutine = null;
        }

        ResetRenderers();
        if (visualChild != null)
        {
            visualChild.localPosition = initialChildLocalPosition;
        }
    }

    private IEnumerator DoFlash()
    {
        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - (elapsed / flashDuration);

            foreach (Renderer rend in cachedRenderers)
            {
                if (rend == null) continue;
                rend.GetPropertyBlock(propertyBlock);

                Color currentFlash = Color.Lerp(Color.white, flashColor, t);
                propertyBlock.SetColor(BaseColorId, currentFlash);
                propertyBlock.SetColor(ColorId, currentFlash);
                propertyBlock.SetColor(EmissionColorId, flashColor * (t * 1.5f));

                rend.SetPropertyBlock(propertyBlock);
            }

            yield return null;
        }

        ResetRenderers();
    }

    private IEnumerator DoFlinch(Vector3 hitDirection)
    {
        if (visualChild == null) yield break;

        Vector3 localDir = transform.InverseTransformDirection(hitDirection.normalized);
        localDir.y = 0f;
        if (localDir.sqrMagnitude < 0.001f)
        {
            localDir = -Vector3.forward;
        }

        Vector3 targetPos = initialChildLocalPosition + localDir.normalized * flinchDistance;
        float elapsed = 0f;

        while (elapsed < flinchDuration)
        {
            elapsed += Time.deltaTime;
            float pct = elapsed / flinchDuration;

            if (pct < 0.3f)
            {
                visualChild.localPosition = Vector3.Lerp(initialChildLocalPosition, targetPos, pct / 0.3f);
            }
            else
            {
                visualChild.localPosition = Vector3.Lerp(targetPos, initialChildLocalPosition, (pct - 0.3f) / 0.7f);
            }

            yield return null;
        }

        visualChild.localPosition = initialChildLocalPosition;
    }

    private void ResetRenderers()
    {
        if (cachedRenderers == null) return;
        propertyBlock.Clear();
        foreach (Renderer rend in cachedRenderers)
        {
            if (rend != null)
            {
                rend.SetPropertyBlock(propertyBlock);
            }
        }
    }

    private void OnDisable()
    {
        StopReaction();
    }
}
