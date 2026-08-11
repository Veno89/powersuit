using System;
using System.Collections.Generic;
using Powersuit.Abilities.UnityAdapters;
using Powersuit.Enemies.UnityAdapters;
using UnityEngine;

/// <summary>
/// Owns one generated demo-world instance for one player. This component never
/// searches the scene globally and never mutates scene objects it did not create.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(150)]
public sealed class PowerSuitDemoBootstrap : MonoBehaviour
{
    [Header("Demo Content")]
    [SerializeField] private GameObject demoWorldPrefab;
    [SerializeField] private bool initializeOnStart = true;
    [SerializeField] private bool suppressLegacySceneEnemies = true;

    [Header("Explicit Runtime Ownership")]
    [SerializeField] private Transform owningPlayer;
    [SerializeField] private Camera controllerCamera;
    [SerializeField] private PowerSuitHudPresenter hudPresenter;

    private GameObject worldInstance;
    private EnemySpawnDirector spawnDirector;
    private PowerSuitEncounterDirector encounterDirector;
    private bool worldCreationAttempted;
    private readonly List<SimpleEnemy> suppressedLegacyEnemies =
        new List<SimpleEnemy>(8);

    public GameObject DemoWorldPrefab => demoWorldPrefab;
    public Transform OwningPlayer => owningPlayer;
    public Camera ControllerCamera => controllerCamera;
    public PowerSuitHudPresenter HudPresenter => hudPresenter;
    public GameObject WorldInstance => worldInstance;
    public EnemySpawnDirector SpawnDirector => spawnDirector;
    public PowerSuitEncounterDirector EncounterDirector => encounterDirector;
    public bool IsInitialized =>
        worldInstance != null &&
        spawnDirector != null &&
        spawnDirector.IsInitialized;
    public string LastInitializationError { get; private set; } = string.Empty;
    public int SuppressedLegacyEnemyCount => suppressedLegacyEnemies.Count;

    private void Awake()
    {
        if (owningPlayer == null)
        {
            owningPlayer = transform;
        }

        if (hudPresenter == null && owningPlayer != null)
        {
            hudPresenter = owningPlayer.GetComponent<PowerSuitHudPresenter>();
        }
    }

    private void Start()
    {
        if (initializeOnStart)
        {
            TryInitializeDemo();
        }
    }

    private void OnDestroy()
    {
        CleanupOwnedWorld();
    }

    /// <summary>
    /// Editor/runtime configuration boundary. It does not instantiate content.
    /// Reapplying the same ownership is idempotent; changing material ownership
    /// after a world exists is rejected so the live scene cannot become orphaned.
    /// </summary>
    public void Configure(
        GameObject worldPrefab,
        Transform player,
        Camera playerCamera,
        PowerSuitHudPresenter optionalHud = null,
        bool shouldInitializeOnStart = true
    )
    {
        if (worldPrefab == null)
        {
            throw new ArgumentNullException(nameof(worldPrefab));
        }

        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (playerCamera == null)
        {
            throw new ArgumentNullException(nameof(playerCamera));
        }

        if (
            worldInstance != null &&
            (demoWorldPrefab != worldPrefab ||
             owningPlayer != player ||
             controllerCamera != playerCamera)
        )
        {
            throw new InvalidOperationException(
                "Clean up the owned demo world before changing its prefab, player, " +
                "or camera ownership."
            );
        }

        demoWorldPrefab = worldPrefab;
        owningPlayer = player;
        controllerCamera = playerCamera;
        hudPresenter = optionalHud != null
            ? optionalHud
            : player.GetComponent<PowerSuitHudPresenter>();
        initializeOnStart = shouldInitializeOnStart;

        if (IsInitialized)
        {
            BindHudIfPresent();
        }
    }

    /// <summary>
    /// Prefab-generation boundary for a player whose scene camera is resolved
    /// by <see cref="PowerSuitController"/> during Awake. The bootstrap keeps
    /// the same explicit player ownership and acquires only that controller's
    /// camera before it creates the world.
    /// </summary>
    public void ConfigureForPlayerPrefab(
        GameObject worldPrefab,
        Transform player,
        PowerSuitHudPresenter optionalHud = null,
        bool shouldInitializeOnStart = true
    )
    {
        if (worldPrefab == null)
        {
            throw new ArgumentNullException(nameof(worldPrefab));
        }

        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (
            worldInstance != null &&
            (demoWorldPrefab != worldPrefab || owningPlayer != player)
        )
        {
            throw new InvalidOperationException(
                "Clean up the owned demo world before changing its prefab or " +
                "player ownership."
            );
        }

        demoWorldPrefab = worldPrefab;
        owningPlayer = player;
        controllerCamera = null;
        hudPresenter = optionalHud != null
            ? optionalHud
            : player.GetComponentInChildren<PowerSuitHudPresenter>(true);
        initializeOnStart = shouldInitializeOnStart;
    }

    /// <summary>
    /// Instantiates the configured prefab at world origin once, resolves its
    /// authored child director once, and initializes that director explicitly.
    /// Repeated calls reuse the same instance and never duplicate the world.
    /// </summary>
    public bool TryInitializeDemo()
    {
        LastInitializationError = string.Empty;
        if (demoWorldPrefab == null)
        {
            return Fail("A demo world prefab is required.");
        }

        if (owningPlayer == null)
        {
            owningPlayer = transform;
        }

        if (controllerCamera == null && owningPlayer != null)
        {
            PowerSuitController controller =
                owningPlayer.GetComponent<PowerSuitController>();
            controllerCamera = controller != null
                ? controller.PlayerCamera
                : null;
        }

        if (controllerCamera == null)
        {
            return Fail(
                "The owning controller camera must be assigned explicitly."
            );
        }

        if (worldInstance == null)
        {
            if (worldCreationAttempted)
            {
                return Fail(
                    "The owned demo world was removed externally. Call " +
                    "CleanupOwnedWorld before intentionally creating a replacement."
                );
            }

            worldCreationAttempted = true;
            worldInstance = Instantiate(
                demoWorldPrefab,
                Vector3.zero,
                Quaternion.identity
            );
            spawnDirector = worldInstance.GetComponentInChildren<EnemySpawnDirector>(
                includeInactive: true
            );
            encounterDirector = worldInstance.GetComponentInChildren<
                PowerSuitEncounterDirector
            >(includeInactive: true);
        }

        if (spawnDirector == null)
        {
            return Fail(
                "The configured demo world has no child EnemySpawnDirector."
            );
        }

        if (
            !spawnDirector.IsInitialized &&
            !spawnDirector.TryInitializeForPlayer(owningPlayer, controllerCamera)
        )
        {
            string directorError = spawnDirector.LastValidationError;
            return Fail(
                string.IsNullOrWhiteSpace(directorError)
                    ? "The demo world's EnemySpawnDirector failed to initialize."
                    : directorError
            );
        }

        if (encounterDirector != null)
        {
            encounterDirector.BindPlayer(
                owningPlayer,
                owningPlayer.GetComponent<PlayerHealth>()
            );
        }
        BindHudIfPresent();
        SuppressLegacyEnemiesInOwningScene();
        return true;
    }

    /// <summary>
    /// Resets combat spawning inside the existing world. It never reinstantiates
    /// the prefab and does not affect unrelated scene objects.
    /// </summary>
    public bool ResetDemo(
        bool clearExistingEnemies = true,
        bool shouldSpawnImmediately = true
    )
    {
        if (!IsInitialized)
        {
            return false;
        }

        spawnDirector.ResetDirector(
            clearExistingEnemies,
            shouldSpawnImmediately
        );
        encounterDirector?.ResetEncounter();
        BindHudIfPresent();
        return true;
    }

    /// <summary>
    /// Releases only the world created by this component. Disabling the player
    /// intentionally does not call this method, so temporary disable/respawn
    /// flows preserve the live demo state.
    /// </summary>
    public bool CleanupOwnedWorld()
    {
        bool hadOwnedWorld = worldInstance != null;
        if (spawnDirector != null && spawnDirector.IsInitialized)
        {
            spawnDirector.ClearActiveEnemies();
        }

        if (worldInstance != null)
        {
            worldInstance.SetActive(false);
            if (Application.isPlaying)
            {
                Destroy(worldInstance);
            }
            else
            {
                DestroyImmediate(worldInstance);
            }
        }

        worldInstance = null;
        spawnDirector = null;
        encounterDirector = null;
        worldCreationAttempted = false;
        RestoreSuppressedLegacyEnemies();
        LastInitializationError = string.Empty;
        return hadOwnedWorld;
    }

    private void BindHudIfPresent()
    {
        if (owningPlayer == null)
        {
            return;
        }

        if (hudPresenter == null)
        {
            hudPresenter = owningPlayer.GetComponentInChildren<
                PowerSuitHudPresenter
            >(true);
        }

        if (hudPresenter == null)
        {
            return;
        }

        PlayerHealth health = owningPlayer.GetComponent<PlayerHealth>();
        PowerSuitWeapon weapon = owningPlayer.GetComponent<PowerSuitWeapon>();
        PowerSuitAbilityController abilities =
            owningPlayer.GetComponent<PowerSuitAbilityController>();
        ShoulderRocketAbility rocket = abilities != null
            ? abilities.ShoulderRocket
            : owningPlayer.GetComponent<ShoulderRocketAbility>();
        LightningStrikeAbility lightning = abilities != null
            ? abilities.LightningStrike
            : owningPlayer.GetComponent<LightningStrikeAbility>();
        VoidUltimateAbility ultimate = abilities != null
            ? abilities.VoidUltimate
            : owningPlayer.GetComponent<VoidUltimateAbility>();

        hudPresenter.BindSources(
            health,
            weapon,
            rocket,
            lightning,
            ultimate
        );
        hudPresenter.BindEncounter(encounterDirector);
    }

    private bool Fail(string message)
    {
        LastInitializationError = message ?? string.Empty;
        return false;
    }

    /// <summary>
    /// The original AimDemo contains three rollback-era SimpleEnemy roots.
    /// Preserve those authored scene objects, but suspend them at runtime while
    /// the generated six-archetype director owns encounters. Cleanup restores
    /// exactly the objects this bootstrap suspended.
    /// </summary>
    private void SuppressLegacyEnemiesInOwningScene()
    {
        if (
            !suppressLegacySceneEnemies ||
            owningPlayer == null ||
            !owningPlayer.gameObject.scene.IsValid()
        )
        {
            return;
        }

        GameObject[] roots = owningPlayer.gameObject.scene.GetRootGameObjects();
        for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            SimpleEnemy[] enemies = roots[rootIndex]
                .GetComponentsInChildren<SimpleEnemy>(includeInactive: false);
            for (int enemyIndex = 0; enemyIndex < enemies.Length; enemyIndex++)
            {
                SimpleEnemy enemy = enemies[enemyIndex];
                if (enemy == null || !enemy.gameObject.activeSelf)
                {
                    continue;
                }

                if (!suppressedLegacyEnemies.Contains(enemy))
                {
                    suppressedLegacyEnemies.Add(enemy);
                }
                enemy.gameObject.SetActive(false);
            }
        }
    }

    private void RestoreSuppressedLegacyEnemies()
    {
        for (int index = 0; index < suppressedLegacyEnemies.Count; index++)
        {
            SimpleEnemy enemy = suppressedLegacyEnemies[index];
            if (enemy != null)
            {
                enemy.gameObject.SetActive(true);
            }
        }
        suppressedLegacyEnemies.Clear();
    }
}
