#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Powersuit.Enemies;
using Powersuit.Enemies.UnityAdapters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Powersuit.Editor
{
    /// <summary>
    /// Idempotent, generator-owned enemy and sandbox content. Every object is
    /// assembled in a preview scene, and existing asset paths are updated in
    /// place so their meta files and GUIDs remain stable.
    /// </summary>
    public static class PowerSuitDemoEnemyContentGenerator
    {
        public const string EnemyContentFolder = "Assets/Game/Content/Enemies";
        public const string EnemyDefinitionFolder = EnemyContentFolder + "/Definitions";
        public const string EnemyMaterialFolder = EnemyContentFolder + "/Materials";
        public const string EnemyPrefabFolder = "Assets/Game/Prefab/Enemies/Generated";
        public const string WorldPrefabFolder = "Assets/Game/Prefab/World";

        public const string ProjectilePrefabPath =
            EnemyPrefabFolder + "/EnemyAttackProjectile.prefab";
        public const string CombatSandboxPrefabPath =
            WorldPrefabFolder + "/PowerSuitCombatSandbox.prefab";

        public const string StationarySentryDefinitionPath =
            EnemyDefinitionFolder + "/StationarySentry.asset";
        public const string PatrolRiflemanDefinitionPath =
            EnemyDefinitionFolder + "/PatrolRifleman.asset";
        public const string PursuerDefinitionPath =
            EnemyDefinitionFolder + "/Pursuer.asset";
        public const string HeavyArtilleryDefinitionPath =
            EnemyDefinitionFolder + "/HeavyArtillery.asset";
        public const string FlyingHarrierDefinitionPath =
            EnemyDefinitionFolder + "/FlyingHarrier.asset";
        public const string SkirmisherDefinitionPath =
            EnemyDefinitionFolder + "/Skirmisher.asset";

        public const string StationarySentryPrefabPath =
            EnemyPrefabFolder + "/StationarySentry.prefab";
        public const string PatrolRiflemanPrefabPath =
            EnemyPrefabFolder + "/PatrolRifleman.prefab";
        public const string PursuerPrefabPath =
            EnemyPrefabFolder + "/Pursuer.prefab";
        public const string HeavyArtilleryPrefabPath =
            EnemyPrefabFolder + "/HeavyArtillery.prefab";
        public const string FlyingHarrierPrefabPath =
            EnemyPrefabFolder + "/FlyingHarrier.prefab";
        public const string SkirmisherPrefabPath =
            EnemyPrefabFolder + "/Skirmisher.prefab";

        private const string ProjectileMaterialPath =
            EnemyMaterialFolder + "/EnemyProjectile.mat";
        private const string FoundryMaterialPath =
            EnemyMaterialFolder + "/SandboxFoundry.mat";
        private const string CausewayMaterialPath =
            EnemyMaterialFolder + "/SandboxCauseway.mat";
        private const string AirfieldMaterialPath =
            EnemyMaterialFolder + "/SandboxAirfield.mat";
        private const string HealthBarBackgroundMaterialPath =
            EnemyMaterialFolder + "/HealthBarBackground.mat";
        private const string HealthBarFillMaterialPath =
            EnemyMaterialFolder + "/HealthBarFill.mat";

        private static readonly EnemyRole[] Roles =
        {
            EnemyRole.StationarySentry,
            EnemyRole.PatrolRifleman,
            EnemyRole.Pursuer,
            EnemyRole.HeavyArtillery,
            EnemyRole.FlyingHarrier,
            EnemyRole.Skirmisher
        };

        private static readonly string[] DefinitionPathArray =
        {
            StationarySentryDefinitionPath,
            PatrolRiflemanDefinitionPath,
            PursuerDefinitionPath,
            HeavyArtilleryDefinitionPath,
            FlyingHarrierDefinitionPath,
            SkirmisherDefinitionPath
        };

        private static readonly string[] EnemyPrefabPathArray =
        {
            StationarySentryPrefabPath,
            PatrolRiflemanPrefabPath,
            PursuerPrefabPath,
            HeavyArtilleryPrefabPath,
            FlyingHarrierPrefabPath,
            SkirmisherPrefabPath
        };

        private static readonly string[] RequiredAssetPathArray =
        {
            StationarySentryDefinitionPath,
            PatrolRiflemanDefinitionPath,
            PursuerDefinitionPath,
            HeavyArtilleryDefinitionPath,
            FlyingHarrierDefinitionPath,
            SkirmisherDefinitionPath,
            ProjectilePrefabPath,
            StationarySentryPrefabPath,
            PatrolRiflemanPrefabPath,
            PursuerPrefabPath,
            HeavyArtilleryPrefabPath,
            FlyingHarrierPrefabPath,
            SkirmisherPrefabPath,
            CombatSandboxPrefabPath
        };

        private static readonly Color[] EnemyColors =
        {
            new Color(1f, 0.42f, 0.08f),
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.12f, 0.09f),
            new Color(0.68f, 0.54f, 0.08f),
            new Color(0.05f, 0.82f, 0.95f),
            new Color(0.66f, 0.18f, 0.92f)
        };

        public static IReadOnlyList<string> DefinitionPaths => DefinitionPathArray;
        public static IReadOnlyList<string> EnemyPrefabPaths => EnemyPrefabPathArray;
        public static IReadOnlyList<string> RequiredAssetPaths => RequiredAssetPathArray;

        public sealed class GeneratedContent
        {
            internal GeneratedContent(
                EnemyArchetypeDefinition[] definitions,
                GameObject[] enemyPrefabs,
                EnemyAttackProjectile projectilePrefab,
                GameObject combatSandboxPrefab
            )
            {
                Definitions = Array.AsReadOnly(definitions);
                EnemyPrefabs = Array.AsReadOnly(enemyPrefabs);
                ProjectilePrefab = projectilePrefab;
                CombatSandboxPrefab = combatSandboxPrefab;
            }

            public IReadOnlyList<EnemyArchetypeDefinition> Definitions { get; }
            public IReadOnlyList<GameObject> EnemyPrefabs { get; }
            public EnemyAttackProjectile ProjectilePrefab { get; }
            public GameObject CombatSandboxPrefab { get; }
        }

        public sealed class ValidationReport
        {
            internal ValidationReport(List<string> errors)
            {
                Errors = errors.AsReadOnly();
            }

            public IReadOnlyList<string> Errors { get; }
            public bool IsValid => Errors.Count == 0;
            public string Summary => IsValid
                ? "Enemy combat sandbox content is valid."
                : string.Join("\n", Errors);
        }

        [MenuItem("PowerSuit/Content/Generate Enemy Combat Sandbox")]
        public static void GenerateFromMenu()
        {
            GeneratedContent generated = Generate();
            Selection.activeObject = generated.CombatSandboxPrefab;
            Debug.Log(
                "Generated six enemy archetypes, six pooled enemy prefabs, "
                + "their projectile, and PowerSuitCombatSandbox."
            );
        }

        public static GeneratedContent Generate()
        {
            EnsureAssetFolder(EnemyDefinitionFolder);
            EnsureAssetFolder(EnemyMaterialFolder);
            EnsureAssetFolder(EnemyPrefabFolder);
            EnsureAssetFolder(WorldPrefabFolder);

            EnemyArchetypeDefinition[] definitions =
                new EnemyArchetypeDefinition[Roles.Length];
            Material[] enemyMaterials = new Material[Roles.Length];
            for (int index = 0; index < Roles.Length; index++)
            {
                definitions[index] = CreateOrUpdateDefinition(
                    Roles[index],
                    DefinitionPathArray[index]
                );
                enemyMaterials[index] = CreateOrUpdateMaterial(
                    EnemyMaterialFolder + "/" + Roles[index] + ".mat",
                    EnemyColors[index],
                    emission: index == (int)EnemyRole.FlyingHarrier
                );
            }

            Material projectileMaterial = CreateOrUpdateMaterial(
                ProjectileMaterialPath,
                new Color(1f, 0.24f, 0.035f),
                emission: true
            );
            Material foundryMaterial = CreateOrUpdateMaterial(
                FoundryMaterialPath,
                new Color(0.30f, 0.20f, 0.14f),
                emission: false
            );
            Material causewayMaterial = CreateOrUpdateMaterial(
                CausewayMaterialPath,
                new Color(0.17f, 0.24f, 0.32f),
                emission: false
            );
            Material airfieldMaterial = CreateOrUpdateMaterial(
                AirfieldMaterialPath,
                new Color(0.10f, 0.31f, 0.33f),
                emission: false
            );
            Material healthBarBackgroundMaterial = CreateOrUpdateMaterial(
                HealthBarBackgroundMaterialPath,
                new Color(0.025f, 0.03f, 0.035f),
                emission: false
            );
            Material healthBarFillMaterial = CreateOrUpdateMaterial(
                HealthBarFillMaterialPath,
                new Color(0.12f, 0.95f, 0.32f),
                emission: true
            );

            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                EnemyAttackProjectile projectile = CreateOrUpdateProjectilePrefab(
                    previewScene,
                    projectileMaterial
                );
                GameObject[] enemyPrefabs = new GameObject[Roles.Length];
                for (int index = 0; index < Roles.Length; index++)
                {
                    enemyPrefabs[index] = CreateOrUpdateEnemyPrefab(
                        previewScene,
                        Roles[index],
                        definitions[index],
                        projectile,
                        enemyMaterials[index],
                        healthBarBackgroundMaterial,
                        healthBarFillMaterial,
                        EnemyPrefabPathArray[index]
                    );
                }

                GameObject sandbox = CreateOrUpdateCombatSandboxPrefab(
                    previewScene,
                    definitions,
                    enemyPrefabs,
                    foundryMaterial,
                    causewayMaterial,
                    airfieldMaterial
                );

                // Saving prefabs can cause Unity to normalize local material
                // keywords on referenced assets. Reassert the two authored
                // emissive materials after every prefab save so regeneration
                // cannot silently make the Harrier/projectile unlit.
                CreateOrUpdateMaterial(
                    EnemyMaterialFolder + "/" + EnemyRole.FlyingHarrier + ".mat",
                    EnemyColors[(int)EnemyRole.FlyingHarrier],
                    emission: true
                );
                CreateOrUpdateMaterial(
                    ProjectileMaterialPath,
                    new Color(1f, 0.24f, 0.035f),
                    emission: true
                );

                ValidationReport report = Validate();
                if (!report.IsValid)
                {
                    throw new InvalidOperationException(report.Summary);
                }

                return new GeneratedContent(
                    definitions,
                    enemyPrefabs,
                    projectile,
                    sandbox
                );
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        public static ValidationReport Validate()
        {
            List<string> errors = new List<string>();
            HashSet<EnemyRole> discoveredRoles = new HashSet<EnemyRole>();
            EnemyAttackProjectile projectile = AssetDatabase
                .LoadAssetAtPath<GameObject>(ProjectilePrefabPath)
                ?.GetComponent<EnemyAttackProjectile>();

            if (projectile == null)
            {
                errors.Add("Missing pooled EnemyAttackProjectile prefab.");
            }
            else if (projectile.GetComponent<TrailRenderer>() == null)
            {
                errors.Add("EnemyAttackProjectile prefab has no TrailRenderer.");
            }

            ValidateEmissionMaterial(
                EnemyMaterialFolder + "/" + EnemyRole.FlyingHarrier + ".mat",
                errors
            );
            ValidateEmissionMaterial(ProjectileMaterialPath, errors);

            for (int index = 0; index < Roles.Length; index++)
            {
                EnemyArchetypeDefinition definition =
                    AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>(
                        DefinitionPathArray[index]
                    );
                if (definition == null)
                {
                    errors.Add("Missing definition: " + DefinitionPathArray[index]);
                    continue;
                }

                if (!definition.TryCreateRuntimeConfig(out EnemyArchetypeConfig config, out string error))
                {
                    errors.Add(DefinitionPathArray[index] + ": " + error);
                }
                else
                {
                    if (config.Role != Roles[index])
                    {
                        errors.Add(DefinitionPathArray[index] + " has the wrong role.");
                    }
                    discoveredRoles.Add(config.Role);
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    EnemyPrefabPathArray[index]
                );
                ValidateEnemyPrefab(
                    prefab,
                    definition,
                    projectile,
                    EnemyPrefabPathArray[index],
                    errors
                );
            }

            if (discoveredRoles.Count != Roles.Length)
            {
                errors.Add("The generated definitions do not cover all six enemy roles.");
            }

            ValidateSandboxPrefab(errors);
            return new ValidationReport(errors);
        }

        private static void ValidateEmissionMaterial(
            string assetPath,
            List<string> errors
        )
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null || material.shader == null)
            {
                errors.Add("Missing emissive material: " + assetPath);
                return;
            }

            UnityEngine.Rendering.LocalKeyword keyword =
                new UnityEngine.Rendering.LocalKeyword(material.shader, "_EMISSION");
            if (!keyword.isValid || !material.IsKeywordEnabled(keyword))
            {
                errors.Add(assetPath + " does not retain its _EMISSION keyword.");
            }
        }

        private static EnemyArchetypeDefinition CreateOrUpdateDefinition(
            EnemyRole role,
            string assetPath
        )
        {
            EnemyArchetypeDefinition definition =
                AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>(assetPath);
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (definition == null && existing != null)
            {
                throw new InvalidOperationException(
                    assetPath + " is occupied by " + existing.GetType().Name + "."
                );
            }

            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<EnemyArchetypeDefinition>();
                definition.name = role.ToString();
                AssetDatabase.CreateAsset(definition, assetPath);
                definition.ApplyRolePreset(role);
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssetIfDirty(definition);
            }

            // Existing definitions are user-facing tuning assets. Integration
            // validates them but never silently reapplies presets over authored
            // health, movement, attack, or threat values.
            return definition;
        }

        private static Material CreateOrUpdateMaterial(
            string assetPath,
            Color color,
            bool emission
        )
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (material == null && existing != null)
            {
                throw new InvalidOperationException(
                    assetPath + " is occupied by " + existing.GetType().Name + "."
                );
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException("No supported lit shader is available.");
            }

            if (material == null)
            {
                material = new Material(shader)
                {
                    name = System.IO.Path.GetFileNameWithoutExtension(assetPath)
                };
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.enableInstancing = true;
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", emission ? 0.72f : 0.38f);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", emission ? 0.18f : 0.58f);
            }
            if (material.HasProperty("_EmissionColor"))
            {
                Color emissionColor = emission ? color * 2.4f : Color.black;
                material.SetColor("_EmissionColor", emissionColor);
                // URP declares _EMISSION as a local shader keyword. Using the
                // render-pipeline helper keeps it in the material's serialized
                // valid-keyword set on current Unity versions; the legacy
                // string-only API can silently drop it during reimport.
                UnityEngine.Rendering.LocalKeyword emissionKeyword =
                    new UnityEngine.Rendering.LocalKeyword(
                        material.shader,
                        "_EMISSION"
                    );
                if (emissionKeyword.isValid)
                {
                    material.SetKeyword(emissionKeyword, emission);
                }
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static EnemyAttackProjectile CreateOrUpdateProjectilePrefab(
            Scene previewScene,
            Material material
        )
        {
            GameObject root = CreatePrimitiveInScene(
                previewScene,
                null,
                PrimitiveType.Sphere,
                "EnemyAttackProjectile",
                Vector3.zero,
                Vector3.one * 0.22f,
                Quaternion.identity,
                material,
                keepCollider: false
            );
            EnemyAttackProjectile projectile = root.AddComponent<EnemyAttackProjectile>();
            TrailRenderer trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            trail.time = 0.24f;
            trail.startWidth = 0.15f;
            trail.endWidth = 0.015f;
            trail.minVertexDistance = 0.03f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;

            GameObject saved = SavePrefab(root, ProjectilePrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            EnemyAttackProjectile savedProjectile =
                saved.GetComponent<EnemyAttackProjectile>();
            if (savedProjectile == null)
            {
                throw new InvalidOperationException(
                    "EnemyAttackProjectile prefab did not retain its component."
                );
            }
            return savedProjectile;
        }

        private static GameObject CreateOrUpdateEnemyPrefab(
            Scene previewScene,
            EnemyRole role,
            EnemyArchetypeDefinition definition,
            EnemyAttackProjectile projectile,
            Material material,
            Material healthBarBackgroundMaterial,
            Material healthBarFillMaterial,
            string prefabPath
        )
        {
            GameObject root = CreateObjectInScene(previewScene, role.ToString());
            GetControllerDimensions(role, out float height, out float radius);

            CharacterController character = root.AddComponent<CharacterController>();
            character.height = height;
            character.radius = radius;
            character.center = Vector3.up * height * 0.5f;
            character.skinWidth = Mathf.Min(0.12f, radius * 0.16f);
            character.stepOffset = Mathf.Min(0.5f, height * 0.18f);
            character.slopeLimit = role == EnemyRole.FlyingHarrier ? 89f : 52f;

            EnemyArchetypeController controller =
                root.AddComponent<EnemyArchetypeController>();
            EnemyAttackEmitter emitter = root.AddComponent<EnemyAttackEmitter>();
            emitter.ProjectilePrefab = projectile;

            Transform visualRoot = CreateObjectInScene(
                previewScene,
                "Visual",
                root.transform
            ).transform;
            BuildEnemyVisual(previewScene, visualRoot, role, material);
            EnemyCombatReadabilityPresenter readability =
                root.AddComponent<EnemyCombatReadabilityPresenter>();
            readability.Configure(controller, visualRoot);
            BuildEnemyHealthBar(
                previewScene,
                root,
                controller,
                height,
                healthBarBackgroundMaterial,
                healthBarFillMaterial
            );

            Transform eye = CreateObjectInScene(
                previewScene,
                "EyePoint",
                root.transform
            ).transform;
            eye.localPosition = new Vector3(0f, height * 0.72f, 0.08f);
            Transform muzzle = CreateObjectInScene(
                previewScene,
                "AttackOrigin",
                root.transform
            ).transform;
            muzzle.localPosition = new Vector3(
                role == EnemyRole.HeavyArtillery ? 0.55f : 0f,
                height * 0.60f,
                radius + (role == EnemyRole.HeavyArtillery ? 1.2f : 0.72f)
            );

            SerializedObject controllerObject = new SerializedObject(controller);
            SetObjectReference(controllerObject, "definition", definition);
            SetObjectReference(controllerObject, "eyePoint", eye);
            SetObjectReference(controllerObject, "attackOrigin", muzzle);
            if (role == EnemyRole.FlyingHarrier)
            {
                SetObjectReference(controllerObject, "bankVisual", visualRoot);
                SetFloat(controllerObject, "maximumBankDegrees", 24f);
            }
            controllerObject.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = SavePrefab(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        private static void BuildEnemyHealthBar(
            Scene previewScene,
            GameObject enemyRoot,
            EnemyArchetypeController controller,
            float enemyHeight,
            Material backgroundMaterial,
            Material fillMaterial
        )
        {
            Transform barRoot = CreateObjectInScene(
                previewScene,
                "HealthBar",
                enemyRoot.transform
            ).transform;
            barRoot.localPosition = new Vector3(0f, enemyHeight + 0.42f, 0f);

            CreatePrimitiveInScene(
                previewScene,
                barRoot,
                PrimitiveType.Cube,
                "Background",
                new Vector3(0f, 0f, 0.025f),
                new Vector3(1.28f, 0.13f, 0.045f),
                Quaternion.identity,
                backgroundMaterial,
                keepCollider: false
            );
            Transform fill = CreatePrimitiveInScene(
                previewScene,
                barRoot,
                PrimitiveType.Cube,
                "Fill",
                new Vector3(0f, 0f, -0.025f),
                new Vector3(1.16f, 0.075f, 0.05f),
                Quaternion.identity,
                fillMaterial,
                keepCollider: false
            ).transform;

            EnemyHealthBarPresenter presenter =
                enemyRoot.AddComponent<EnemyHealthBarPresenter>();
            presenter.Configure(controller, barRoot, fill, 55f);
        }

        private static GameObject CreateOrUpdateCombatSandboxPrefab(
            Scene previewScene,
            EnemyArchetypeDefinition[] definitions,
            GameObject[] enemyPrefabs,
            Material foundryMaterial,
            Material causewayMaterial,
            Material airfieldMaterial
        )
        {
            GameObject root = CreateObjectInScene(
                previewScene,
                "PowerSuitCombatSandbox"
            );
            Transform environment = CreateObjectInScene(
                previewScene,
                "Environment",
                root.transform
            ).transform;

            Transform foundry = CreateObjectInScene(
                previewScene,
                "Zone_FoundryYard",
                environment
            ).transform;
            foundry.localPosition = new Vector3(-18f, 0f, 0f);
            BuildFoundryArea(previewScene, foundry, foundryMaterial);

            Transform causeway = CreateObjectInScene(
                previewScene,
                "Zone_CentralCauseway",
                environment
            ).transform;
            BuildCausewayArea(previewScene, causeway, causewayMaterial);

            Transform airfield = CreateObjectInScene(
                previewScene,
                "Zone_AerialBasin",
                environment
            ).transform;
            airfield.localPosition = new Vector3(18f, 0f, 0f);
            BuildAirfieldArea(previewScene, airfield, airfieldMaterial);

            Transform zonesRoot = CreateObjectInScene(
                previewScene,
                "SpawnZones",
                root.transform
            ).transform;
            SpawnZone[] zones =
            {
                CreateSpawnZone(
                    previewScene,
                    zonesRoot,
                    "FoundryGround",
                    SpawnZoneCompatibility.Ground,
                    new Vector3(-18f, 0f, 0f),
                    new Bounds(new Vector3(0f, 2f, 0f), new Vector3(17f, 6f, 20f)),
                    new[]
                    {
                        new Vector3(-7f, 0.15f, -8f),
                        new Vector3(7f, 0.15f, -8f),
                        new Vector3(-7f, 0.15f, 8f),
                        new Vector3(7f, 0.15f, 8f)
                    }
                ),
                CreateSpawnZone(
                    previewScene,
                    zonesRoot,
                    "CausewayGround",
                    SpawnZoneCompatibility.Ground,
                    Vector3.zero,
                    new Bounds(new Vector3(0f, 2f, 0f), new Vector3(17f, 6f, 20f)),
                    new[]
                    {
                        new Vector3(-7f, 0.15f, -8f),
                        new Vector3(7f, 0.15f, -8f),
                        new Vector3(-7f, 0.15f, 8f),
                        new Vector3(0f, 0.15f, -8f)
                    }
                ),
                CreateSpawnZone(
                    previewScene,
                    zonesRoot,
                    "AirfieldGround",
                    SpawnZoneCompatibility.Ground,
                    new Vector3(18f, 0f, 0f),
                    new Bounds(new Vector3(0f, 2f, 0f), new Vector3(17f, 6f, 20f)),
                    new[]
                    {
                        new Vector3(-7f, 0.15f, -9f),
                        new Vector3(7f, 0.15f, -8f),
                        new Vector3(-7f, 0.15f, 8f),
                        new Vector3(7f, 0.15f, -4f)
                    }
                ),
                CreateSpawnZone(
                    previewScene,
                    zonesRoot,
                    "WesternAirspace",
                    SpawnZoneCompatibility.Flight,
                    new Vector3(-9f, 0f, 0f),
                    new Bounds(new Vector3(0f, 8f, 0f), new Vector3(18f, 12f, 20f)),
                    new[]
                    {
                        new Vector3(-6f, 7f, -7f),
                        new Vector3(5f, 10f, -5f),
                        new Vector3(-4f, 9f, 7f)
                    }
                ),
                CreateSpawnZone(
                    previewScene,
                    zonesRoot,
                    "EasternAirspace",
                    SpawnZoneCompatibility.Flight,
                    new Vector3(17f, 0f, 0f),
                    new Bounds(new Vector3(0f, 9f, 0f), new Vector3(20f, 14f, 22f)),
                    new[]
                    {
                        new Vector3(-6f, 8f, -7f),
                        new Vector3(5f, 11f, -5f),
                        new Vector3(-4f, 9f, 8f),
                        new Vector3(7f, 7f, 6f)
                    }
                )
            };

            GameObject directorObject = CreateObjectInScene(
                previewScene,
                "Enemy Spawn Director",
                root.transform
            );
            EnemySpawnDirector director =
                directorObject.AddComponent<EnemySpawnDirector>();
            ConfigureDirector(director, zones, definitions, enemyPrefabs);
            PowerSuitEncounterDirector encounter =
                directorObject.AddComponent<PowerSuitEncounterDirector>();
            encounter.ConfigureAuthored(
                director,
                CreateAuthoredEncounterPhases(),
                authoredIntermissionSeconds: 2.5f
            );

            GameObject saved = SavePrefab(root, CombatSandboxPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        private static PowerSuitEncounterPhase[] CreateAuthoredEncounterPhases()
        {
            return new[]
            {
                CreateEncounterPhase(
                    "causeway",
                    "CENTRAL CAUSEWAY",
                    Vector3.zero,
                    10f,
                    new[] { "CausewayGround" },
                    CreateEncounterSpawn("patrol-rifleman", 3),
                    CreateEncounterSpawn("stationary-sentry", 2),
                    CreateEncounterSpawn("pursuer", 2)
                ),
                CreateEncounterPhase(
                    "foundry",
                    "FOUNDRY YARD",
                    new Vector3(-18f, 0f, 0f),
                    10f,
                    new[] { "FoundryGround", "WesternAirspace" },
                    CreateEncounterSpawn("pursuer", 3),
                    CreateEncounterSpawn("skirmisher", 3),
                    CreateEncounterSpawn("heavy-artillery", 1)
                ),
                CreateEncounterPhase(
                    "airfield",
                    "AERIAL BASIN",
                    new Vector3(18f, 0f, 0f),
                    11f,
                    new[] { "AirfieldGround", "EasternAirspace" },
                    CreateEncounterSpawn("flying-harrier", 4),
                    CreateEncounterSpawn("skirmisher", 3),
                    CreateEncounterSpawn("heavy-artillery", 2)
                )
            };
        }

        private static PowerSuitEncounterPhase CreateEncounterPhase(
            string id,
            string displayName,
            Vector3 center,
            float radius,
            string[] zoneIds,
            params PowerSuitEncounterSpawnEntry[] spawns
        )
        {
            PowerSuitEncounterPhase phase = new PowerSuitEncounterPhase();
            phase.Configure(
                id,
                displayName,
                center,
                radius,
                zoneIds,
                spawns
            );
            return phase;
        }

        private static PowerSuitEncounterSpawnEntry CreateEncounterSpawn(
            string archetypeId,
            int count
        )
        {
            PowerSuitEncounterSpawnEntry entry =
                new PowerSuitEncounterSpawnEntry();
            entry.Configure(archetypeId, count);
            return entry;
        }

        private static void ConfigureDirector(
            EnemySpawnDirector director,
            SpawnZone[] zones,
            EnemyArchetypeDefinition[] definitions,
            GameObject[] enemyPrefabs
        )
        {
            SerializedObject serialized = new SerializedObject(director);
            SetObjectReferenceArray(serialized, "spawnZones", zones);

            SerializedProperty entries = serialized.FindProperty("spawnEntries");
            entries.arraySize = Roles.Length;
            for (int index = 0; index < Roles.Length; index++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("definition").objectReferenceValue =
                    definitions[index];
                entry.FindPropertyRelative("prefab").objectReferenceValue =
                    enemyPrefabs[index];
                entry.FindPropertyRelative("isEnabled").boolValue = true;
                entry.FindPropertyRelative("weightMultiplier").floatValue = 1f;
                entry.FindPropertyRelative("prewarmCount").intValue =
                    Roles[index] == EnemyRole.HeavyArtillery ? 1 : 2;
            }

            // Keep the default encounter readable for a first-time player.
            // The developer console can deliberately raise this cap for stress
            // testing without making an idle launch immediately lethal.
            SetInteger(serialized, "activeEnemyCap", 10);
            SetFloat(serialized, "spawnIntervalSeconds", 4.4f);
            SetInteger(serialized, "minimumGroupSize", 1);
            SetInteger(serialized, "maximumGroupSize", 3);
            SetFloat(serialized, "groupThreatBudget", 5.5f);
            SetFloat(serialized, "groupActivationSpacingSeconds", 0.24f);
            SetFloat(serialized, "playerSafeRadius", 10f);
            SetBoolean(serialized, "avoidCameraView", true);
            SetFloat(serialized, "spawnProtectionSeconds", 1.25f);
            SetFloat(serialized, "maximumInitialAttackStaggerSeconds", 2.8f);
            SetFloat(serialized, "deathRecycleDelaySeconds", 0.65f);
            SetBoolean(serialized, "useDeterministicSeed", true);
            SetInteger(serialized, "deterministicSeed", 109);
            SetBoolean(serialized, "spawnImmediately", true);
            SetBoolean(serialized, "prewarmPools", true);
            SetBoolean(serialized, "initializeOnStart", false);
            SetBoolean(serialized, "automaticTick", true);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SpawnZone CreateSpawnZone(
            Scene previewScene,
            Transform parent,
            string id,
            SpawnZoneCompatibility compatibility,
            Vector3 localPosition,
            Bounds localBounds,
            Vector3[] pointPositions
        )
        {
            GameObject zoneObject = CreateObjectInScene(
                previewScene,
                id,
                parent
            );
            zoneObject.transform.localPosition = localPosition;
            SpawnZone zone = zoneObject.AddComponent<SpawnZone>();
            Transform[] points = new Transform[pointPositions.Length];
            for (int index = 0; index < pointPositions.Length; index++)
            {
                Transform point = CreateObjectInScene(
                    previewScene,
                    "Point_" + (index + 1),
                    zoneObject.transform
                ).transform;
                point.localPosition = pointPositions[index];
                points[index] = point;
            }

            zone.Configure(
                id,
                compatibility,
                points,
                localBounds,
                requireGroundProbe: true
            );
            return zone;
        }

        private static void BuildFoundryArea(
            Scene previewScene,
            Transform parent,
            Material material
        )
        {
            AddWorldBox(previewScene, parent, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(18f, 1f, 22f), material);
            AddWorldBox(previewScene, parent, "FurnaceBlock", new Vector3(-4.8f, 1.5f, -1f), new Vector3(3.5f, 3f, 5f), material);
            AddWorldBox(previewScene, parent, "CargoStack", new Vector3(4.5f, 1f, 4.5f), new Vector3(4f, 2f, 3f), material);
            AddWorldBox(previewScene, parent, "LowCoverA", new Vector3(3f, 0.65f, -5.5f), new Vector3(5f, 1.3f, 1f), material);
            AddWorldBox(previewScene, parent, "LowCoverB", new Vector3(-2f, 0.65f, 6f), new Vector3(4f, 1.3f, 1f), material);
            AddWorldBox(previewScene, parent, "UpperCatwalk", new Vector3(-1.5f, 3.5f, 0f), new Vector3(8f, 0.45f, 2.2f), material);
            AddWorldBox(previewScene, parent, "CatwalkSupportWest", new Vector3(-5f, 1.7f, 0f), new Vector3(0.7f, 3.4f, 0.7f), material);
            AddWorldBox(previewScene, parent, "CatwalkSupportEast", new Vector3(2f, 1.7f, 0f), new Vector3(0.7f, 3.4f, 0.7f), material);
            AddWorldBox(previewScene, parent, "AoEPracticePad", new Vector3(5.4f, 0.06f, 0f), new Vector3(5.5f, 0.12f, 5.5f), material);
        }

        private static void BuildCausewayArea(
            Scene previewScene,
            Transform parent,
            Material material
        )
        {
            AddWorldBox(previewScene, parent, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(18f, 1f, 22f), material);
            // The player scene starts at the causeway origin and its normal
            // third-person camera initially orbits straight back along -Z.
            // Keep the lane divider offset from that authored spawn/camera
            // corridor; centring this collider on the origin starts the camera
            // sphere cast inside it and collapses the view to its 0.2 m safety
            // distance on the first frame.
            AddWorldBox(previewScene, parent, "MedianWall", new Vector3(-3f, 0.8f, 0f), new Vector3(1.2f, 1.6f, 10f), material);
            AddWorldBox(previewScene, parent, "CoverNorth", new Vector3(-5f, 0.7f, 5f), new Vector3(3.5f, 1.4f, 1f), material);
            AddWorldBox(previewScene, parent, "CoverSouth", new Vector3(5f, 0.7f, -5f), new Vector3(3.5f, 1.4f, 1f), material);
            AddWorldBox(previewScene, parent, "Overlook", new Vector3(5.7f, 1.25f, 6.8f), new Vector3(5f, 2.5f, 4f), material);
            AddWorldBox(previewScene, parent, "ElevatedBridge", new Vector3(0f, 3.2f, 6.2f), new Vector3(9f, 0.5f, 2.4f), material);
            AddWorldBox(previewScene, parent, "BridgePillarWest", new Vector3(-3.6f, 1.5f, 6.2f), new Vector3(0.65f, 3f, 0.65f), material);
            AddWorldBox(previewScene, parent, "BridgePillarEast", new Vector3(3.6f, 1.5f, 6.2f), new Vector3(0.65f, 3f, 0.65f), material);
            AddWorldBox(previewScene, parent, "AoECourtyard", new Vector3(2.5f, 0.05f, -1f), new Vector3(6f, 0.1f, 6f), material);
        }

        private static void BuildAirfieldArea(
            Scene previewScene,
            Transform parent,
            Material material
        )
        {
            AddWorldBox(previewScene, parent, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(18f, 1f, 22f), material);
            AddWorldBox(previewScene, parent, "TowerWest", new Vector3(-5.7f, 3f, -5.8f), new Vector3(2.2f, 6f, 2.2f), material);
            AddWorldBox(previewScene, parent, "TowerEast", new Vector3(5.6f, 4f, 5.5f), new Vector3(2.2f, 8f, 2.2f), material);
            AddWorldBox(previewScene, parent, "LandingPad", new Vector3(0f, 0.35f, 0f), new Vector3(7f, 0.7f, 7f), material);
            AddWorldBox(previewScene, parent, "BlastShield", new Vector3(-4f, 1f, 5f), new Vector3(5f, 2f, 0.8f), material);
            AddWorldBox(previewScene, parent, "HoverPlatformNorth", new Vector3(0f, 4.2f, 7.2f), new Vector3(5.5f, 0.5f, 3.2f), material);
            AddWorldBox(previewScene, parent, "HoverPlatformSouth", new Vector3(4.7f, 6.4f, -6.8f), new Vector3(4.2f, 0.5f, 3.2f), material);
            AddWorldBox(previewScene, parent, "FlightGateWest", new Vector3(-6.8f, 5f, 0f), new Vector3(0.7f, 10f, 3.8f), material);
            AddWorldBox(previewScene, parent, "FlightGateEast", new Vector3(6.8f, 5f, 0f), new Vector3(0.7f, 10f, 3.8f), material);
            AddWorldBox(previewScene, parent, "FlightGateTop", new Vector3(0f, 9.6f, 0f), new Vector3(13.6f, 0.7f, 3.8f), material);
        }

        private static void AddWorldBox(
            Scene previewScene,
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material
        )
        {
            CreatePrimitiveInScene(
                previewScene,
                parent,
                PrimitiveType.Cube,
                name,
                localPosition,
                localScale,
                Quaternion.identity,
                material,
                keepCollider: true
            );
        }

        private static void BuildEnemyVisual(
            Scene previewScene,
            Transform root,
            EnemyRole role,
            Material material
        )
        {
            switch (role)
            {
                case EnemyRole.StationarySentry:
                    AddVisualPart(previewScene, root, PrimitiveType.Cylinder, "Base", new Vector3(0f, 0.35f, 0f), new Vector3(1.35f, 0.35f, 1.35f), Quaternion.identity, material);
                    AddVisualPart(previewScene, root, PrimitiveType.Sphere, "Turret", new Vector3(0f, 1.15f, 0f), new Vector3(1.15f, 0.75f, 1.15f), Quaternion.identity, material);
                    AddVisualPart(previewScene, root, PrimitiveType.Cylinder, "RapidBarrel", new Vector3(0f, 1.15f, 0.9f), new Vector3(0.22f, 0.9f, 0.22f), Quaternion.Euler(90f, 0f, 0f), material);
                    break;
                case EnemyRole.PatrolRifleman:
                    AddVisualPart(previewScene, root, PrimitiveType.Capsule, "Body", new Vector3(0f, 1.25f, 0f), new Vector3(0.8f, 1.25f, 0.8f), Quaternion.identity, material);
                    AddVisualPart(previewScene, root, PrimitiveType.Sphere, "Head", new Vector3(0f, 2.45f, 0f), Vector3.one * 0.62f, Quaternion.identity, material);
                    AddVisualPart(previewScene, root, PrimitiveType.Cube, "Rifle", new Vector3(0.42f, 1.55f, 0.65f), new Vector3(0.22f, 0.2f, 1.45f), Quaternion.identity, material);
                    break;
                case EnemyRole.Pursuer:
                    AddVisualPart(previewScene, root, PrimitiveType.Capsule, "Core", new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 1f), Quaternion.Euler(18f, 0f, 0f), material);
                    AddVisualPart(previewScene, root, PrimitiveType.Cube, "Shoulders", new Vector3(0f, 1.55f, 0.18f), new Vector3(1.8f, 0.35f, 0.8f), Quaternion.identity, material);
                    AddVisualPart(previewScene, root, PrimitiveType.Cube, "Ram", new Vector3(0f, 0.9f, 0.95f), new Vector3(0.7f, 0.5f, 1.15f), Quaternion.Euler(20f, 0f, 0f), material);
                    break;
                case EnemyRole.HeavyArtillery:
                    AddVisualPart(previewScene, root, PrimitiveType.Cube, "ArmoredHull", new Vector3(0f, 1.2f, 0f), new Vector3(2.7f, 2.2f, 2.2f), Quaternion.identity, material);
                    AddVisualPart(previewScene, root, PrimitiveType.Cylinder, "LeftPod", new Vector3(-1.15f, 2.25f, 0.35f), new Vector3(0.55f, 1.25f, 0.55f), Quaternion.Euler(90f, 0f, 0f), material);
                    AddVisualPart(previewScene, root, PrimitiveType.Cylinder, "RightPod", new Vector3(1.15f, 2.25f, 0.35f), new Vector3(0.55f, 1.25f, 0.55f), Quaternion.Euler(90f, 0f, 0f), material);
                    AddVisualPart(previewScene, root, PrimitiveType.Cube, "HeavyBarrel", new Vector3(0.55f, 2f, 1.35f), new Vector3(0.42f, 0.42f, 2f), Quaternion.identity, material);
                    break;
                case EnemyRole.FlyingHarrier:
                    AddVisualPart(previewScene, root, PrimitiveType.Sphere, "FlightCore", new Vector3(0f, 1.2f, 0f), new Vector3(1.25f, 0.65f, 1.65f), Quaternion.identity, material);
                    AddVisualPart(previewScene, root, PrimitiveType.Cube, "LeftWing", new Vector3(-1.35f, 1.2f, -0.05f), new Vector3(2.1f, 0.16f, 1.1f), Quaternion.Euler(0f, 12f, -8f), material);
                    AddVisualPart(previewScene, root, PrimitiveType.Cube, "RightWing", new Vector3(1.35f, 1.2f, -0.05f), new Vector3(2.1f, 0.16f, 1.1f), Quaternion.Euler(0f, -12f, 8f), material);
                    AddVisualPart(previewScene, root, PrimitiveType.Cylinder, "Thruster", new Vector3(0f, 1.15f, -1f), new Vector3(0.38f, 0.65f, 0.38f), Quaternion.Euler(90f, 0f, 0f), material);
                    break;
                case EnemyRole.Skirmisher:
                    AddVisualPart(previewScene, root, PrimitiveType.Capsule, "SlimCore", new Vector3(0f, 1.2f, 0f), new Vector3(0.65f, 1.2f, 0.65f), Quaternion.identity, material);
                    AddVisualPart(previewScene, root, PrimitiveType.Cube, "LeftFin", new Vector3(-0.75f, 1.4f, -0.15f), new Vector3(1.1f, 0.18f, 1.45f), Quaternion.Euler(0f, 20f, -24f), material);
                    AddVisualPart(previewScene, root, PrimitiveType.Cube, "RightFin", new Vector3(0.75f, 1.4f, -0.15f), new Vector3(1.1f, 0.18f, 1.45f), Quaternion.Euler(0f, -20f, 24f), material);
                    AddVisualPart(previewScene, root, PrimitiveType.Cube, "BurstRifle", new Vector3(0.32f, 1.4f, 0.8f), new Vector3(0.18f, 0.18f, 1.55f), Quaternion.identity, material);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        private static void AddVisualPart(
            Scene previewScene,
            Transform parent,
            PrimitiveType primitive,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material
        )
        {
            CreatePrimitiveInScene(
                previewScene,
                parent,
                primitive,
                name,
                localPosition,
                localScale,
                localRotation,
                material,
                keepCollider: false
            );
        }

        private static GameObject CreatePrimitiveInScene(
            Scene previewScene,
            Transform parent,
            PrimitiveType primitive,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material,
            bool keepCollider
        )
        {
            GameObject created = GameObject.CreatePrimitive(primitive);
            SceneManager.MoveGameObjectToScene(created, previewScene);
            created.name = name;
            if (parent != null)
            {
                created.transform.SetParent(parent, false);
            }
            created.transform.localPosition = localPosition;
            created.transform.localRotation = localRotation;
            created.transform.localScale = localScale;

            Renderer renderer = created.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
            if (!keepCollider)
            {
                Collider collider = created.GetComponent<Collider>();
                if (collider != null)
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }
            }
            return created;
        }

        private static GameObject CreateObjectInScene(
            Scene previewScene,
            string name,
            Transform parent = null
        )
        {
            GameObject created = new GameObject(name);
            SceneManager.MoveGameObjectToScene(created, previewScene);
            if (parent != null)
            {
                created.transform.SetParent(parent, false);
            }
            return created;
        }

        private static GameObject SavePrefab(GameObject root, string assetPath)
        {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            if (saved == null)
            {
                throw new InvalidOperationException("Could not save prefab: " + assetPath);
            }
            return saved;
        }

        private static void ValidateEnemyPrefab(
            GameObject prefab,
            EnemyArchetypeDefinition definition,
            EnemyAttackProjectile projectile,
            string path,
            List<string> errors
        )
        {
            if (prefab == null)
            {
                errors.Add("Missing enemy prefab: " + path);
                return;
            }

            EnemyArchetypeController controller =
                prefab.GetComponent<EnemyArchetypeController>();
            CharacterController character = prefab.GetComponent<CharacterController>();
            EnemyAttackEmitter emitter = prefab.GetComponent<EnemyAttackEmitter>();
            EnemyHealthBarPresenter healthBar =
                prefab.GetComponent<EnemyHealthBarPresenter>();
            EnemyCombatReadabilityPresenter readability =
                prefab.GetComponent<EnemyCombatReadabilityPresenter>();
            if (controller == null)
            {
                errors.Add(path + " has no root EnemyArchetypeController.");
            }
            else if (controller.Definition != definition)
            {
                errors.Add(path + " references the wrong definition.");
            }
            if (character == null)
            {
                errors.Add(path + " has no CharacterController.");
            }
            if (emitter == null || emitter.ProjectilePrefab != projectile)
            {
                errors.Add(path + " does not reference the shared projectile.");
            }
            if (
                healthBar == null ||
                healthBar.Target != controller ||
                healthBar.BarRoot == null ||
                healthBar.Fill == null
            )
            {
                errors.Add(path + " has no configured pooled health bar.");
            }
            if (
                readability == null ||
                readability.Controller != controller ||
                readability.VisualRoot == null
            )
            {
                errors.Add(path + " has no configured hit/telegraph readability presenter.");
            }
            if (prefab.GetComponent<DamageableTarget>() != null)
            {
                errors.Add(path + " still contains legacy DamageableTarget.");
            }
            if (prefab.GetComponentsInChildren<Renderer>(true).Length < 2)
            {
                errors.Add(path + " does not have a distinct generated silhouette.");
            }
        }

        private static void ValidateSandboxPrefab(List<string> errors)
        {
            GameObject sandbox = AssetDatabase.LoadAssetAtPath<GameObject>(
                CombatSandboxPrefabPath
            );
            if (sandbox == null)
            {
                errors.Add("Missing PowerSuitCombatSandbox prefab.");
                return;
            }

            Transform environment = sandbox.transform.Find("Environment");
            if (
                environment == null ||
                environment.Find("Zone_FoundryYard") == null ||
                environment.Find("Zone_CentralCauseway") == null ||
                environment.Find("Zone_AerialBasin") == null
            )
            {
                errors.Add("Combat sandbox does not contain all three connected areas.");
            }
            else if (
                environment.Find("Zone_FoundryYard/UpperCatwalk") == null ||
                environment.Find("Zone_FoundryYard/AoEPracticePad") == null ||
                environment.Find("Zone_CentralCauseway/ElevatedBridge") == null ||
                environment.Find("Zone_CentralCauseway/AoECourtyard") == null ||
                environment.Find("Zone_AerialBasin/HoverPlatformNorth") == null ||
                environment.Find("Zone_AerialBasin/HoverPlatformSouth") == null ||
                environment.Find("Zone_AerialBasin/FlightGateTop") == null
            )
            {
                errors.Add(
                    "Combat sandbox is missing cover, elevation, AoE, or flight-lane landmarks."
                );
            }

            SpawnZone[] zones = sandbox.GetComponentsInChildren<SpawnZone>(true);
            int groundZones = 0;
            int flightZones = 0;
            for (int index = 0; index < zones.Length; index++)
            {
                if ((zones[index].Compatibility & SpawnZoneCompatibility.Ground) != 0)
                {
                    groundZones++;
                }
                if ((zones[index].Compatibility & SpawnZoneCompatibility.Flight) != 0)
                {
                    flightZones++;
                }
                if (zones[index].CandidateCapacity < 3)
                {
                    errors.Add(zones[index].name + " has too few explicit spawn points.");
                }
            }
            if (groundZones < 3 || flightZones < 2)
            {
                errors.Add("Combat sandbox needs at least three ground and two flight zones.");
            }

            EnemySpawnDirector director =
                sandbox.GetComponentInChildren<EnemySpawnDirector>(true);
            if (director == null)
            {
                errors.Add("Combat sandbox has no EnemySpawnDirector.");
                return;
            }

            SerializedObject serialized = new SerializedObject(director);
            SerializedProperty entries = serialized.FindProperty("spawnEntries");
            SerializedProperty configuredZones = serialized.FindProperty("spawnZones");
            if (entries == null || entries.arraySize != Roles.Length)
            {
                errors.Add("EnemySpawnDirector does not contain all six spawn entries.");
            }
            if (configuredZones == null || configuredZones.arraySize != zones.Length)
            {
                errors.Add("EnemySpawnDirector does not reference every generated zone.");
            }
            if (serialized.FindProperty("initializeOnStart").boolValue)
            {
                errors.Add("Sandbox director must wait for external player/camera binding.");
            }
            if (
                serialized.FindProperty("activeEnemyCap").intValue != 10 ||
                Mathf.Abs(
                    serialized.FindProperty("spawnIntervalSeconds").floatValue - 4.4f
                ) > 0.001f ||
                serialized.FindProperty("maximumGroupSize").intValue != 3
            )
            {
                errors.Add("Sandbox encounter pacing does not match the polished demo profile.");
            }

            PowerSuitEncounterDirector encounter =
                sandbox.GetComponentInChildren<PowerSuitEncounterDirector>(true);
            if (
                encounter == null ||
                encounter.SpawnDirector != director ||
                encounter.PhaseCount != 3
            )
            {
                errors.Add(
                    "Combat sandbox is missing its authored three-zone encounter flow."
                );
            }
            else
            {
                SerializedObject encounterSettings =
                    new SerializedObject(encounter);
                SerializedProperty phases =
                    encounterSettings.FindProperty("phases");
                int[] expectedDefeatBudgets = { 7, 7, 9 };
                for (int phaseIndex = 0; phaseIndex < phases.arraySize; phaseIndex++)
                {
                    SerializedProperty spawns = phases
                        .GetArrayElementAtIndex(phaseIndex)
                        .FindPropertyRelative("spawnEntries");
                    int budget = 0;
                    for (int spawnIndex = 0; spawnIndex < spawns.arraySize; spawnIndex++)
                    {
                        budget += Mathf.Max(
                            0,
                            spawns.GetArrayElementAtIndex(spawnIndex)
                                .FindPropertyRelative("count").intValue
                        );
                    }
                    if (budget != expectedDefeatBudgets[phaseIndex])
                    {
                        errors.Add(
                            "Combat sandbox encounter defeat budgets must be 7/7/9."
                        );
                        break;
                    }
                }
            }
        }

        private static void GetControllerDimensions(
            EnemyRole role,
            out float height,
            out float radius
        )
        {
            switch (role)
            {
                case EnemyRole.StationarySentry:
                    height = 1.9f;
                    radius = 0.72f;
                    return;
                case EnemyRole.PatrolRifleman:
                    height = 2.8f;
                    radius = 0.55f;
                    return;
                case EnemyRole.Pursuer:
                    height = 2.4f;
                    radius = 0.78f;
                    return;
                case EnemyRole.HeavyArtillery:
                    height = 3.4f;
                    radius = 1.35f;
                    return;
                case EnemyRole.FlyingHarrier:
                    height = 1.8f;
                    radius = 1.05f;
                    return;
                case EnemyRole.Skirmisher:
                    height = 2.65f;
                    radius = 0.48f;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        private static void SetObjectReference(
            SerializedObject serialized,
            string propertyName,
            UnityEngine.Object value
        )
        {
            SerializedProperty property = RequireProperty(serialized, propertyName);
            property.objectReferenceValue = value;
        }

        private static void SetObjectReferenceArray<T>(
            SerializedObject serialized,
            string propertyName,
            T[] values
        ) where T : UnityEngine.Object
        {
            SerializedProperty property = RequireProperty(serialized, propertyName);
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            }
        }

        private static void SetFloat(
            SerializedObject serialized,
            string propertyName,
            float value
        )
        {
            RequireProperty(serialized, propertyName).floatValue = value;
        }

        private static void SetInteger(
            SerializedObject serialized,
            string propertyName,
            int value
        )
        {
            RequireProperty(serialized, propertyName).intValue = value;
        }

        private static void SetBoolean(
            SerializedObject serialized,
            string propertyName,
            bool value
        )
        {
            RequireProperty(serialized, propertyName).boolValue = value;
        }

        private static SerializedProperty RequireProperty(
            SerializedObject serialized,
            string propertyName
        )
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    serialized.targetObject.GetType().Name
                    + " no longer has serialized field "
                    + propertyName
                    + "."
                );
            }
            return property;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string[] segments = folderPath.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }
                current = next;
            }
        }
    }
}
#endif
