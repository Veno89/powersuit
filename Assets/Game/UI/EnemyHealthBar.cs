using UnityEngine;
using UnityEngine.UI;

public sealed class EnemyHealthBar : MonoBehaviour
{
    [Header("Target & Component References")]
    [SerializeField] private DamageableTarget target;
    [SerializeField] private RectTransform healthBarRoot;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image fillImage;
    [SerializeField] private RectTransform fillRectTransform;
    [SerializeField] private Canvas canvas;

    [Header("Display Settings")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 2.5f, 0f);
    [SerializeField] private Vector2 barSize = new Vector2(160f, 18f);
    [SerializeField] private Vector3 canvasScale = new Vector3(0.01f, 0.01f, 0.01f);
    [SerializeField] private float maxDisplayDistance = 50f;

    private Camera activeCamera;

    public DamageableTarget Target
    {
        get => target;
        set => target = value;
    }

    public RectTransform HealthBarRoot => healthBarRoot;
    public Image BackgroundImage => backgroundImage;
    public Image FillImage => fillImage;
    public RectTransform FillRectTransform => fillRectTransform;
    public Canvas Canvas => canvas;

    private void Awake()
    {
        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        activeCamera = Camera.main;
    }

    private void Start()
    {
        if (!enabled || target == null)
        {
            return;
        }

        UpdateHealthBar(target.CurrentHealth, target.MaximumHealth);
    }

    private void OnEnable()
    {
        if (target == null)
        {
            target = GetComponentInParent<DamageableTarget>();
        }

        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        target.OnHealthChanged += HandleHealthChanged;
        UpdateHealthBar(target.CurrentHealth, target.MaximumHealth);
    }

    private void OnDisable()
    {
        if (target != null)
        {
            target.OnHealthChanged -= HandleHealthChanged;
        }
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            return;
        }

        if (activeCamera == null)
        {
            activeCamera = Camera.main;
        }

        transform.position = target.transform.position + offset;

        if (activeCamera != null)
        {
            transform.rotation = activeCamera.transform.rotation;
        }

        if (healthBarRoot != null)
        {
            healthBarRoot.sizeDelta = barSize;
            healthBarRoot.localScale = canvasScale;
        }

        UpdateVisibility();
    }

    private void HandleHealthChanged(float currentHealth, float maxHealth)
    {
        UpdateHealthBar(currentHealth, maxHealth);
    }

    private void UpdateHealthBar(float currentHealth, float maxHealth)
    {
        if (fillRectTransform != null)
        {
            float maxHp = Mathf.Max(1f, maxHealth);
            float pct = Mathf.Clamp01(currentHealth / maxHp);
            fillRectTransform.anchorMax = new Vector2(pct, 1f);
        }

        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (canvas == null || target == null)
        {
            return;
        }

        bool isAlive = target.CurrentHealth > 0f;

        if (activeCamera == null)
        {
            activeCamera = Camera.main;
        }

        float distance = activeCamera != null
            ? Vector3.Distance(activeCamera.transform.position, target.transform.position)
            : 0f;

        bool withinDistance = activeCamera == null || distance <= maxDisplayDistance;

        canvas.enabled = isAlive && withinDistance;
    }

    private bool ValidateReferences()
    {
        if (target == null)
        {
            target = GetComponentInParent<DamageableTarget>();
        }

        if (canvas == null)
        {
            canvas = GetComponent<Canvas>();
        }

        if (healthBarRoot == null)
        {
            healthBarRoot = GetComponent<RectTransform>();
        }

        if (target == null || canvas == null || healthBarRoot == null || fillRectTransform == null)
        {
            Debug.LogError(
                $"[EnemyHealthBar] Missing required UI references on '{gameObject.name}'. " +
                $"Please run 'Tools > Powered Suit > Set Up Combat And Aiming' to repair.",
                this
            );
            return false;
        }

        return true;
    }

    public void AssignReferences(
        DamageableTarget targetRef,
        RectTransform rootRef,
        Image bgImageRef,
        Image fillImageRef,
        RectTransform fillRectRef,
        Canvas canvasRef
    )
    {
        target = targetRef;
        healthBarRoot = rootRef;
        backgroundImage = bgImageRef;
        fillImage = fillImageRef;
        fillRectTransform = fillRectRef;
        canvas = canvasRef;
    }
}
