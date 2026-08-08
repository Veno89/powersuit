using System;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class PowerSuitWeapon : MonoBehaviour
{
    [Header("Weapon Configuration")]
    [SerializeField] private Transform muzzleTransform;
    [SerializeField] private PlayerProjectile projectilePrefab;

    [Header("Projectile Parameters")]
    [SerializeField] private float damage = 25f;
    [SerializeField] private float projectileSpeed = 50f;
    [SerializeField] private float projectileLifetime = 4f;
    [SerializeField] private float projectileRadius = 0.15f;
    [SerializeField] private float shotsPerSecond = 5f;

    [Header("Muzzle Flash Feedback")]
    [SerializeField] private GameObject muzzleFlashPrefab;
    [SerializeField] private Color muzzleFlashColor = new Color(0.3f, 0.85f, 1f, 1f);
    [SerializeField] private float flashLightIntensity = 4f;
    [SerializeField] private float flashDuration = 0.05f;

    [Header("Recoil Settings")]
    [SerializeField] private float aimRecoilPitch = 1.2f;
    [SerializeField] private float aimRecoilYaw = 0.35f;
    [SerializeField] private float hipRecoilPitch = 0.7f;
    [SerializeField] private float hipRecoilYaw = 0.2f;

    [Header("Audio Feedback Hooks")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip fireSound;
    [SerializeField] private float minPitch = 0.95f;
    [SerializeField] private float maxPitch = 1.05f;

    [Header("Reticle Visuals")]
    [SerializeField] private Color normalCrosshairColor = Color.white;
    [SerializeField] private Color aimingReticleColor = new Color(0.2f, 0.9f, 1f, 1f);

    private PowerSuitController controller;
    private Camera playerCamera;
    private float nextShotTime;
    private Light muzzleFlashLight;
    private float muzzleLightTimer;

    public Transform MuzzleTransform
    {
        get => muzzleTransform;
        set => muzzleTransform = value;
    }

    public PlayerProjectile ProjectilePrefab
    {
        get => projectilePrefab;
        set => projectilePrefab = value;
    }

    public GameObject MuzzleFlashPrefab
    {
        get => muzzleFlashPrefab;
        set => muzzleFlashPrefab = value;
    }

    private void Awake()
    {
        controller = GetComponent<PowerSuitController>();
        playerCamera = Camera.main;

        if (playerCamera == null)
        {
            Debug.LogError("No Main Camera found.", this);
            enabled = false;
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        EnsureMuzzleFlashLight();
    }

    private void Update()
    {
        if (IsFireHeld() && Time.time >= nextShotTime)
        {
            nextShotTime = Time.time + 1f / shotsPerSecond;
            Fire();
        }

        if (muzzleFlashLight != null && muzzleLightTimer > 0f)
        {
            muzzleLightTimer -= Time.deltaTime;
            if (muzzleLightTimer <= 0f)
            {
                muzzleFlashLight.enabled = false;
            }
        }
    }

    private void Fire()
    {
        Vector3 muzzlePos = GetMuzzlePosition();
        Quaternion muzzleRot = (muzzleTransform != null) ? muzzleTransform.rotation : transform.rotation;

        Vector3 aimPoint = (controller != null)
            ? controller.GetAimPoint(muzzlePos)
            : (playerCamera.transform.position + playerCamera.transform.forward * 100f);

        Vector3 fireDirection = (aimPoint - muzzlePos).normalized;
        if (fireDirection.sqrMagnitude < 0.001f)
        {
            fireDirection = transform.forward;
        }

        // 1. Spawn Projectile
        if (projectilePrefab != null)
        {
            PlayerProjectile proj = Instantiate(
                projectilePrefab,
                muzzlePos,
                Quaternion.LookRotation(fireDirection)
            );

            proj.Initialize(
                fireDirection,
                projectileSpeed,
                damage,
                projectileLifetime,
                projectileRadius,
                transform
            );
        }
        else
        {
            SpawnFallbackProjectile(muzzlePos, fireDirection);
        }

        // 2. Play Muzzle Flash Feedback
        TriggerMuzzleFlash(muzzlePos, muzzleRot);

        // 3. Recoil Impulse
        if (controller != null)
        {
            bool isAiming = controller.IsAiming;
            float pitch = isAiming ? aimRecoilPitch : hipRecoilPitch;
            float yaw = isAiming ? aimRecoilYaw : hipRecoilYaw;
            controller.AddRecoil(pitch, yaw);
        }

        // 4. Audio Feedback Hook
        PlayFireAudio();
    }

    private void TriggerMuzzleFlash(Vector3 position, Quaternion rotation)
    {
        if (muzzleFlashPrefab != null)
        {
            GameObject flashObj = CombatFeedbackPool.Spawn(muzzleFlashPrefab, position, rotation);
            if (muzzleTransform != null && flashObj != null)
            {
                flashObj.transform.SetParent(muzzleTransform, true);
            }
        }

        if (muzzleFlashLight != null)
        {
            muzzleFlashLight.transform.position = position;
            muzzleFlashLight.enabled = true;
            muzzleLightTimer = flashDuration;
        }
    }

    private void EnsureMuzzleFlashLight()
    {
        Transform muzzle = muzzleTransform ?? transform;
        Transform lightTrans = muzzle.Find("MuzzleFlashLight");
        if (lightTrans != null)
        {
            muzzleFlashLight = lightTrans.GetComponent<Light>();
        }

        if (muzzleFlashLight == null)
        {
            GameObject lightObj = new GameObject("MuzzleFlashLight");
            lightObj.transform.SetParent(muzzle, false);
            lightObj.transform.localPosition = Vector3.zero;

            muzzleFlashLight = lightObj.AddComponent<Light>();
            muzzleFlashLight.type = LightType.Point;
            muzzleFlashLight.range = 4f;
            muzzleFlashLight.color = muzzleFlashColor;
            muzzleFlashLight.intensity = flashLightIntensity;
            muzzleFlashLight.enabled = false;
        }
    }

    private void PlayFireAudio()
    {
        if (audioSource != null && fireSound != null)
        {
            audioSource.pitch = UnityEngine.Random.Range(minPitch, maxPitch);
            audioSource.PlayOneShot(fireSound);
        }
    }

    private Vector3 GetMuzzlePosition()
    {
        if (muzzleTransform != null)
        {
            return muzzleTransform.position;
        }

        return transform.position + Vector3.up * 1.35f + transform.forward * 0.6f;
    }

    private void SpawnFallbackProjectile(Vector3 position, Vector3 direction)
    {
        GameObject projObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projObj.name = "Player Projectile";
        projObj.transform.position = position;
        projObj.transform.rotation = Quaternion.LookRotation(direction);
        projObj.transform.localScale = Vector3.one * (projectileRadius * 2f);

        SphereCollider col = projObj.GetComponent<SphereCollider>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        Renderer rend = projObj.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material.color = aimingReticleColor;
        }

        PlayerProjectile proj = projObj.AddComponent<PlayerProjectile>();
        proj.Initialize(
            direction,
            projectileSpeed,
            damage,
            projectileLifetime,
            projectileRadius,
            transform
        );
    }

    private bool IsFireHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null &&
               Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }

    private void OnGUI()
    {
        if (controller == null)
        {
            return;
        }

        bool isAiming = controller.IsAiming;
        Vector2 reticlePos = isAiming ? controller.ReticleScreenPosition : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        float guiX = reticlePos.x;
        float guiY = Screen.height - reticlePos.y;

        Color savedColor = GUI.color;
        GUI.color = isAiming ? aimingReticleColor : normalCrosshairColor;

        if (isAiming)
        {
            const float size = 12f;
            const float thickness = 2f;
            const float gap = 4f;

            GUI.DrawTexture(new Rect(guiX - thickness * 0.5f, guiY - gap - size, thickness, size), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(guiX - thickness * 0.5f, guiY + gap, thickness, size), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(guiX - gap - size, guiY - thickness * 0.5f, size, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(guiX + gap, guiY - thickness * 0.5f, size, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(guiX - 1.5f, guiY - 1.5f, 3f, 3f), Texture2D.whiteTexture);
        }
        else
        {
            const float size = 8f;
            const float thickness = 2f;

            GUI.DrawTexture(new Rect(guiX - size, guiY - thickness * 0.5f, size * 2f, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(guiX - thickness * 0.5f, guiY - size, thickness, size * 2f), Texture2D.whiteTexture);
        }

        GUI.color = savedColor;
    }
}