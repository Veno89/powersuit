using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class DamageNumberManager : MonoBehaviour
{
    private static DamageNumberManager instance;
    public static DamageNumberManager Instance => instance;

    [Header("Damage Number Settings")]
    [SerializeField] private float floatSpeed = 1.8f;
    [SerializeField] private float lifetime = 0.85f;
    [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 1.2f, 0f);
    [SerializeField] private Color enemyDamageColor = new Color(1f, 0.88f, 0.25f, 1f);
    [SerializeField] private Color playerDamageColor = new Color(1f, 0.3f, 0.3f, 1f);
    [SerializeField] private int fontSize = 28;

    private readonly Queue<DamageNumberItem> itemPool = new Queue<DamageNumberItem>();
    private Camera activeCamera;
    private Canvas worldCanvas;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        activeCamera = Camera.main;
        EnsureCanvas();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void LateUpdate()
    {
        if (activeCamera == null)
        {
            activeCamera = Camera.main;
        }
    }

    public static void SpawnDamageNumber(Vector3 worldPosition, float damage, bool isPlayerDamage)
    {
        if (instance == null)
        {
            GameObject managerObject = new GameObject("DamageNumberManager");
            instance = managerObject.AddComponent<DamageNumberManager>();
        }

        instance.SpawnInternal(worldPosition, damage, isPlayerDamage);
    }

    private void SpawnInternal(Vector3 worldPosition, float damage, bool isPlayerDamage)
    {
        EnsureCanvas();

        DamageNumberItem item = GetPooledItem();
        Color targetColor = isPlayerDamage ? playerDamageColor : enemyDamageColor;
        item.Activate(worldPosition + spawnOffset + Random.insideUnitSphere * 0.2f, damage, targetColor, fontSize, floatSpeed, lifetime, activeCamera);
    }

    private void EnsureCanvas()
    {
        if (worldCanvas != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("DamageNumberCanvas");
        canvasObject.transform.SetParent(transform, false);

        worldCanvas = canvasObject.AddComponent<Canvas>();
        worldCanvas.renderMode = RenderMode.WorldSpace;
        worldCanvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
    }

    private DamageNumberItem GetPooledItem()
    {
        if (itemPool.Count > 0)
        {
            DamageNumberItem item = itemPool.Dequeue();
            if (item != null)
            {
                return item;
            }
        }

        GameObject textObject = new GameObject("DamageNumberText");
        textObject.transform.SetParent(worldCanvas.transform, false);

        Text textComponent = textObject.AddComponent<Text>();
        textComponent.alignment = TextAnchor.MiddleCenter;
        textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Font.CreateDynamicFontFromOSFont("Arial", 28);
        textComponent.raycastTarget = false;

        DamageNumberItem newItem = textObject.AddComponent<DamageNumberItem>();
        newItem.Initialize(this, textComponent);
        return newItem;
    }

    public void ReturnToPool(DamageNumberItem item)
    {
        if (item == null) return;
        item.gameObject.SetActive(false);
        itemPool.Enqueue(item);
    }

    public sealed class DamageNumberItem : MonoBehaviour
    {
        private DamageNumberManager manager;
        private Text textComponent;
        private Vector3 currentWorldPos;
        private float floatSpeed;
        private float duration;
        private float elapsedTime;
        private Color baseColor;
        private Camera activeCamera;

        public void Initialize(DamageNumberManager mgr, Text textComp)
        {
            manager = mgr;
            textComponent = textComp;
            RectTransform rect = GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200f, 60f);
            rect.localScale = Vector3.one * 0.015f;
        }

        public void Activate(Vector3 startPos, float damage, Color color, int size, float speed, float maxTime, Camera cam)
        {
            currentWorldPos = startPos;
            baseColor = color;
            floatSpeed = speed;
            duration = maxTime;
            elapsedTime = 0f;
            activeCamera = cam;

            textComponent.text = Mathf.RoundToInt(damage).ToString();
            textComponent.fontSize = size;
            textComponent.color = color;

            transform.position = currentWorldPos;
            gameObject.SetActive(true);
            UpdateRotation();
        }

        private void Update()
        {
            elapsedTime += Time.deltaTime;
            if (elapsedTime >= duration)
            {
                manager.ReturnToPool(this);
                return;
            }

            currentWorldPos += Vector3.up * (floatSpeed * Time.deltaTime);
            transform.position = currentWorldPos;

            float alpha = Mathf.Clamp01(1f - (elapsedTime / duration));
            Color c = baseColor;
            c.a = alpha;
            textComponent.color = c;

            UpdateRotation();
        }

        private void UpdateRotation()
        {
            if (activeCamera != null)
            {
                transform.rotation = activeCamera.transform.rotation;
            }
        }
    }
}
