#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Powersuit.Editor
{
    public static class PoweredSuitGenerator109Integration
    {
        public const string ModelPath =
            "Assets/Game/Models/PoweredSuit/powersuit_animated_with_aim.fbx";

        public const string ControllerPath =
            "Assets/Game/Animation/PowerSuitAnimator.controller";

        public const string BasePlayerPrefabPath =
            "Assets/Game/Prefab/Player/PlayerPrototype.prefab";

        public const string PlayerVariantPath =
            "Assets/Game/Prefab/Player/PlayerPrototype_Generator109.prefab";

        public const string DemoScenePath =
            "Assets/Scenes/PoweredSuitAimDemo.unity";

        public const string BuildOutputPath =
            "Builds/Windows/PoweredSuitGenerator109/PoweredSuitGenerator109.exe";

        private const string EnemyPrefabPath =
            "Assets/Game/Prefab/Enemies/EnemyPrototype.prefab";

        // The authored Blender asset uses +Y as weapon/character forward. The
        // standard Blender-to-Unity FBX conversion otherwise leaves the visual
        // facing opposite the Unity player root's +Z gameplay forward.
        private static readonly Quaternion ModelFacingCorrection =
            Quaternion.Euler(0f, 180f, 0f);

        private static readonly string[] RequiredClips =
        {
            "PS_Idle",
            "PS_Walk",
            "PS_Hover",
            "PS_Aim"
        };

        [MenuItem("Tools/Powered Suit/Integrate Generator 109")]
        public static void IntegrateFromMenu()
        {
            try
            {
                Integrate();
                EditorUtility.DisplayDialog(
                    "Powered Suit Generator 109",
                    "Integration completed. Open Assets/Scenes/PoweredSuitAimDemo.unity and press Play.",
                    "OK"
                );
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Powered Suit integration failed",
                    exception.Message,
                    "OK"
                );
                throw;
            }
        }

        [MenuItem("Tools/Powered Suit/Build Generator 109 Demo")]
        public static void BuildFromMenu()
        {
            BuildWindowsDevelopment();
        }

        public static void RunBatch()
        {
            Integrate();
        }

        public static void RunAllBatch()
        {
            Integrate();
            BuildWindowsDevelopment();
        }

        public static void Integrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Exit Play mode before integrating Generator 109.");
            }

            if (!File.Exists(ModelPath))
            {
                throw new FileNotFoundException(
                    "The approved Generator 109 FBX is missing.",
                    ModelPath
                );
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureModelImporter();
            Dictionary<string, AnimationClip> clips = LoadRequiredClips();
            AnimatorController controller = UpdateAnimatorController(clips);

            CreateEmptyDemoScene();
            CombatFeedbackSetup.SetUpCombatFeedback();
            ValidateCombatFeedbackAssets();

            GameObject variant = CreatePlayerVariant(controller);
            PopulateDemoScene(variant);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateIntegratedAssets();

            Debug.Log(
                "[Powersuit] Generator 109 integration complete. " +
                $"Demo scene: {DemoScenePath}"
            );
        }

        public static void BuildWindowsDevelopment()
        {
            ValidateIntegratedAssets();

            string outputDirectory = Path.GetDirectoryName(BuildOutputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { DemoScenePath },
                locationPathName = BuildOutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Generator 109 development build failed: " +
                    report.summary.result
                );
            }

            Debug.Log(
                "[Powersuit] Generator 109 development build completed: " +
                BuildOutputPath
            );
        }

        private static void ConfigureModelImporter()
        {
            AssetDatabase.ImportAsset(
                ModelPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate
            );

            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("Generator 109 FBX did not produce a ModelImporter.");
            }

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.optimizeGameObjects = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            importer.resampleCurves = true;
            importer.SaveAndReimport();

            ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
            List<ModelImporterClipAnimation> configured = new List<ModelImporterClipAnimation>();

            foreach (string requiredName in RequiredClips)
            {
                ModelImporterClipAnimation clip = defaults.FirstOrDefault(
                    candidate =>
                        candidate.name.IndexOf(requiredName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        candidate.takeName.IndexOf(requiredName, StringComparison.OrdinalIgnoreCase) >= 0
                );

                if (clip == null)
                {
                    string available = string.Join(
                        ", ",
                        defaults.Select(item => $"{item.name} ({item.takeName})")
                    );
                    throw new InvalidOperationException(
                        $"Required clip '{requiredName}' was not found in Generator 109. " +
                        $"Available clips: {available}"
                    );
                }

                clip.name = requiredName;
                clip.loopTime = true;
                clip.loopPose = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                configured.Add(clip);
            }

            importer.clipAnimations = configured.ToArray();
            importer.SaveAndReimport();
        }

        private static Dictionary<string, AnimationClip> LoadRequiredClips()
        {
            AnimationClip[] imported = AssetDatabase
                .LoadAllAssetsAtPath(ModelPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                .ToArray();

            Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
            foreach (string requiredName in RequiredClips)
            {
                AnimationClip clip = imported.SingleOrDefault(
                    candidate => candidate.name == requiredName
                );

                if (clip == null)
                {
                    throw new InvalidOperationException(
                        $"Imported clip '{requiredName}' is missing after FBX configuration."
                    );
                }

                clips.Add(requiredName, clip);
            }

            return clips;
        }

        private static AnimatorController UpdateAnimatorController(
            IReadOnlyDictionary<string, AnimationClip> clips
        )
        {
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            while (controller.parameters.Length > 0)
            {
                controller.RemoveParameter(0);
            }

            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsFlying", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsAiming", AnimatorControllerParameterType.Bool);

            AnimatorControllerLayer[] layers = controller.layers;
            if (layers.Length == 0)
            {
                controller.AddLayer("Base Layer");
                layers = controller.layers;
            }

            AnimatorStateMachine stateMachine = layers[0].stateMachine;
            foreach (ChildAnimatorState child in stateMachine.states.ToArray())
            {
                stateMachine.RemoveState(child.state);
            }

            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines.ToArray())
            {
                stateMachine.RemoveStateMachine(child.stateMachine);
            }

            foreach (AnimatorStateTransition transition in stateMachine.anyStateTransitions.ToArray())
            {
                stateMachine.RemoveAnyStateTransition(transition);
            }

            AnimatorState idle = AddState(stateMachine, "Idle", clips["PS_Idle"], new Vector3(260f, 40f));
            AnimatorState walk = AddState(stateMachine, "Walk", clips["PS_Walk"], new Vector3(520f, 40f));
            AnimatorState hover = AddState(stateMachine, "Hover", clips["PS_Hover"], new Vector3(520f, 160f));
            AnimatorState aim = AddState(stateMachine, "Aim", clips["PS_Aim"], new Vector3(260f, 240f));
            stateMachine.defaultState = idle;

            AddTransition(idle, walk, Condition("IsMoving", true), Condition("IsFlying", false));
            AddTransition(idle, hover, Condition("IsFlying", true));
            AddTransition(walk, idle, Condition("IsMoving", false), Condition("IsFlying", false));
            AddTransition(walk, hover, Condition("IsFlying", true));
            AddTransition(hover, idle, Condition("IsFlying", false), Condition("IsMoving", false));
            AddTransition(hover, walk, Condition("IsFlying", false), Condition("IsMoving", true));

            AnimatorStateTransition aimTransition = stateMachine.AddAnyStateTransition(aim);
            ConfigureTransition(aimTransition);
            aimTransition.canTransitionToSelf = false;
            aimTransition.AddCondition(AnimatorConditionMode.If, 0f, "IsAiming");

            AddTransition(
                aim,
                idle,
                Condition("IsAiming", false),
                Condition("IsFlying", false),
                Condition("IsMoving", false)
            );
            AddTransition(
                aim,
                walk,
                Condition("IsAiming", false),
                Condition("IsFlying", false),
                Condition("IsMoving", true)
            );
            AddTransition(
                aim,
                hover,
                Condition("IsAiming", false),
                Condition("IsFlying", true)
            );

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimatorState AddState(
            AnimatorStateMachine stateMachine,
            string name,
            Motion motion,
            Vector3 position
        )
        {
            AnimatorState state = stateMachine.AddState(name, position);
            state.motion = motion;
            state.writeDefaultValues = true;
            return state;
        }

        private static AnimatorCondition Condition(string parameter, bool value)
        {
            return new AnimatorCondition
            {
                mode = value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                parameter = parameter,
                threshold = 0f
            };
        }

        private static void AddTransition(
            AnimatorState source,
            AnimatorState destination,
            params AnimatorCondition[] conditions
        )
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            ConfigureTransition(transition);
            foreach (AnimatorCondition condition in conditions)
            {
                transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
            }
        }

        private static void ConfigureTransition(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.12f;
            transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
            transition.orderedInterruption = true;
        }

        private static void CreateEmptyDemoScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, DemoScenePath))
            {
                throw new InvalidOperationException("Could not create the Generator 109 demo scene.");
            }
        }

        private static GameObject CreatePlayerVariant(AnimatorController controller)
        {
            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePlayerPrefabPath);
            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (basePrefab == null || modelPrefab == null)
            {
                throw new InvalidOperationException("The base player prefab or Generator 109 model is missing.");
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(basePrefab) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException("Could not instantiate the base player prefab.");
            }

            try
            {
                instance.name = "PlayerPrototype_Generator109";
                RemoveLegacyVisuals(instance.transform);

                GameObject modelInstance =
                    PrefabUtility.InstantiatePrefab(modelPrefab, instance.transform) as GameObject;
                if (modelInstance == null)
                {
                    throw new InvalidOperationException("Could not instantiate the Generator 109 FBX.");
                }

                modelInstance.name = "PowerSuitVisual_Generator109";
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = ModelFacingCorrection;
                modelInstance.transform.localScale = Vector3.one;
                modelInstance.SetActive(true);

                Animator animator = modelInstance.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    animator = modelInstance.AddComponent<Animator>();
                }

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                Transform muzzle = FindChildRecursive(modelInstance.transform, "Rifle_Muzzle");
                if (muzzle == null)
                {
                    throw new InvalidOperationException(
                        "The imported Generator 109 hierarchy does not expose Rifle_Muzzle."
                    );
                }

                PowerSuitController suitController = instance.GetComponent<PowerSuitController>();
                PowerSuitWeapon weapon = instance.GetComponent<PowerSuitWeapon>();
                PowerSuitAnimationDriver animationDriver =
                    instance.GetComponent<PowerSuitAnimationDriver>();

                if (suitController == null || weapon == null || animationDriver == null)
                {
                    throw new InvalidOperationException(
                        "The base player prefab is missing controller, weapon, or animation components."
                    );
                }

                ConfigureAimCamera(suitController);
                weapon.MuzzleTransform = muzzle;

                SerializedObject driverObject = new SerializedObject(animationDriver);
                driverObject.FindProperty("controller").objectReferenceValue = suitController;
                driverObject.FindProperty("animator").objectReferenceValue = animator;
                driverObject.ApplyModifiedPropertiesWithoutUndo();

                if (instance.GetComponent<ReticleHitMarker>() == null)
                {
                    instance.AddComponent<ReticleHitMarker>();
                }

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, PlayerVariantPath);
                if (saved == null)
                {
                    throw new InvalidOperationException("Could not save the Generator 109 player variant.");
                }

                return saved;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void RemoveLegacyVisuals(Transform playerRoot)
        {
            List<GameObject> removals = new List<GameObject>();
            foreach (Transform child in playerRoot)
            {
                bool hasAnimator = child.GetComponentInChildren<Animator>(true) != null;
                bool legacyName =
                    child.name.Equals("PowerSuitVisual", StringComparison.OrdinalIgnoreCase) ||
                    child.name.StartsWith("powersuit_", StringComparison.OrdinalIgnoreCase) ||
                    child.name.Equals("WeaponMuzzle", StringComparison.OrdinalIgnoreCase);

                if (hasAnimator || legacyName)
                {
                    removals.Add(child.gameObject);
                }
            }

            foreach (GameObject removal in removals)
            {
                UnityEngine.Object.DestroyImmediate(removal);
            }
        }

        private static void ConfigureAimCamera(PowerSuitController controller)
        {
            SerializedObject serialized = new SerializedObject(controller);
            SetFloat(serialized, "aimCameraDistance", 2.35f);
            SetFloat(serialized, "aimCameraHeight", 1.55f);
            SetVector(serialized, "aimShoulderOffset", new Vector3(0.75f, 0.12f, 0f));
            SetFloat(serialized, "aimFieldOfView", 44f);
            SetFloat(serialized, "aimTransitionSpeed", 12f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(SerializedObject serialized, string propertyName, float value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Missing serialized property: {propertyName}");
            }
            property.floatValue = value;
        }

        private static void SetVector(
            SerializedObject serialized,
            string propertyName,
            Vector3 value
        )
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Missing serialized property: {propertyName}");
            }
            property.vector3Value = value;
        }

        private static void PopulateDemoScene(GameObject playerVariant)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != DemoScenePath)
            {
                throw new InvalidOperationException("The Generator 109 demo scene is not active.");
            }

            Material groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Game/Content/Materials/Greybox Ground.mat"
            );
            Material structureMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Game/Content/Materials/Greybox Structure.mat"
            );

            GameObject environmentRoot = new GameObject("Demo Environment");
            CreatePrimitive(
                "Range Floor",
                environmentRoot.transform,
                new Vector3(0f, -0.5f, 12f),
                new Vector3(42f, 1f, 48f),
                groundMaterial
            );
            CreatePrimitive(
                "Left Cover",
                environmentRoot.transform,
                new Vector3(-7f, 1.25f, 10f),
                new Vector3(2f, 2.5f, 5f),
                structureMaterial
            );
            CreatePrimitive(
                "Right Cover",
                environmentRoot.transform,
                new Vector3(7f, 1.25f, 14f),
                new Vector3(2f, 2.5f, 5f),
                structureMaterial
            );

            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.05f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.transform.position = new Vector3(0f, 2.4f, -6f);
            cameraObject.transform.rotation = Quaternion.Euler(12f, 0f, 0f);

            GameObject player = PrefabUtility.InstantiatePrefab(playerVariant, scene) as GameObject;
            if (player == null)
            {
                throw new InvalidOperationException("Could not place the Generator 109 player in the demo.");
            }
            player.name = "Generator 109 Player";
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            GameObject enemyRoot = new GameObject("Test Enemies");
            GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            if (enemyPrefab == null)
            {
                throw new InvalidOperationException("EnemyPrototype prefab is missing.");
            }

            Vector3[] enemyPositions =
            {
                new Vector3(-6f, 1f, 18f),
                new Vector3(0f, 1f, 22f),
                new Vector3(6f, 1f, 20f)
            };

            for (int index = 0; index < enemyPositions.Length; index++)
            {
                GameObject enemy = PrefabUtility.InstantiatePrefab(enemyPrefab, scene) as GameObject;
                if (enemy == null)
                {
                    throw new InvalidOperationException("Could not place an enemy in the demo scene.");
                }

                enemy.name = $"Test Enemy {index + 1}";
                enemy.transform.SetParent(enemyRoot.transform, true);
                enemy.transform.position = enemyPositions[index];
                enemy.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            }

            GameObject instructions = new GameObject("Demo Instructions");
            instructions.AddComponent<PoweredSuitDemoInstructions>();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException("Could not save the Generator 109 demo scene.");
            }
        }

        private static void CreatePrimitive(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material
        )
        {
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            primitive.name = name;
            primitive.transform.SetParent(parent, true);
            primitive.transform.position = position;
            primitive.transform.localScale = scale;

            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static void ValidateCombatFeedbackAssets()
        {
            string[] required =
            {
                "Assets/Game/Prefab/Combat/EnemyImpactEffect.prefab",
                "Assets/Game/Prefab/Combat/EnvironmentImpactEffect.prefab",
                "Assets/Game/Prefab/Combat/MuzzleFlashEffect.prefab",
                "Assets/Game/Prefab/Combat/PlayerProjectile.prefab"
            };

            foreach (string path in required)
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                {
                    throw new InvalidOperationException(
                        "Combat feedback setup did not create required asset: " + path
                    );
                }
            }
        }

        private static void ValidateIntegratedAssets()
        {
            Dictionary<string, AnimationClip> clips = LoadRequiredClips();
            if (clips.Count != RequiredClips.Length)
            {
                throw new InvalidOperationException("Generator 109 clip validation failed.");
            }

            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (
                importer == null ||
                importer.animationType != ModelImporterAnimationType.Generic ||
                importer.importCameras ||
                importer.importLights ||
                importer.optimizeGameObjects
            )
            {
                throw new InvalidOperationException("Generator 109 importer settings are invalid.");
            }

            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                throw new InvalidOperationException("PowerSuitAnimator controller is missing.");
            }

            HashSet<string> stateNames = controller.layers[0].stateMachine.states
                .Select(child => child.state.name)
                .ToHashSet();
            if (!new[] { "Idle", "Walk", "Hover", "Aim" }.All(stateNames.Contains))
            {
                throw new InvalidOperationException("PowerSuitAnimator is missing the Aim state.");
            }

            GameObject variant = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerVariantPath);
            if (variant == null || variant.GetComponent<PowerSuitController>() == null)
            {
                throw new InvalidOperationException("Generator 109 player variant is invalid.");
            }

            Animator[] animators = variant.GetComponentsInChildren<Animator>(true);
            if (animators.Length != 1 || animators[0].runtimeAnimatorController != controller)
            {
                throw new InvalidOperationException(
                    "Generator 109 player variant must contain one explicitly configured Animator."
                );
            }

            PowerSuitWeapon weapon = variant.GetComponent<PowerSuitWeapon>();
            if (weapon == null || weapon.MuzzleTransform == null || weapon.MuzzleTransform.name != "Rifle_Muzzle")
            {
                throw new InvalidOperationException("Generator 109 Rifle_Muzzle is not wired to the weapon.");
            }

            Transform visual = variant.transform.Find("PowerSuitVisual_Generator109");
            if (
                visual == null ||
                Quaternion.Angle(visual.localRotation, ModelFacingCorrection) > 0.1f
            )
            {
                throw new InvalidOperationException(
                    "Generator 109 visual must apply the Blender-to-Unity forward correction."
                );
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) == null)
            {
                throw new InvalidOperationException("Generator 109 demo scene is missing.");
            }
        }

        private static Transform FindChildRecursive(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            foreach (Transform child in root)
            {
                Transform found = FindChildRecursive(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
#endif
