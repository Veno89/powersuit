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
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Powersuit.Abilities.UnityAdapters;
using Powersuit.Combat;
using Powersuit.DeveloperConsole.Integration;
using Powersuit.DeveloperConsole.UnityAdapters;

namespace Powersuit.Editor
{
    public static class PoweredSuitGenerator109Integration
    {
        public enum DemoSceneHandling
        {
            PreserveExisting,
            CreateAndPopulate
        }

        public const string ModelPath =
            "Assets/Game/Models/PoweredSuit/powersuit_animated_with_aim.fbx";

        public const string ControllerPath =
            "Assets/Game/Animation/PowerSuitAnimator.controller";

        public const string UpperBodyMaskPath =
            "Assets/Game/Animation/PowerSuitUpperBody.mask";

        public const string LayerSafeActionClipSuffix = "_UpperBody";

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

        private const string PrecisionRifleDefinitionPath =
            "Assets/Game/Content/Weapons/PrecisionRifle.asset";

        private const string AbilityPrefabFolder =
            "Assets/Game/Prefab/Abilities";
        private const string RocketPrefabPath =
            AbilityPrefabFolder + "/ShoulderRocketProjectile.prefab";
        private const string LightningPrefabPath =
            AbilityPrefabFolder + "/LightningStrikeActor.prefab";
        private const string VoidPrefabPath =
            AbilityPrefabFolder + "/VoidOrbFieldActor.prefab";
        private const string TargetIndicatorPrefabPath =
            AbilityPrefabFolder + "/AbilityTargetIndicator.prefab";

        // Generator 111's carrier-bone FBX imports Blender up as Unity -Z and
        // Blender forward as Unity +Y. Correct both axes on a wrapper above the
        // Animator, because animation evaluation writes the model root itself.
        private static readonly Quaternion ModelFacingCorrection =
            Quaternion.AngleAxis(90f, Vector3.right) *
            Quaternion.Euler(0f, 180f, 0f);

        // The imported rifle preserves its physical bore as local +Y, while
        // gameplay effects expect Transform.forward (+Z).
        private static readonly Quaternion MuzzleAdapterRotation =
            Quaternion.Euler(-90f, 0f, 0f);

        private static readonly string[] RequiredClips =
        {
            "PS_Idle",
            "PS_Walk",
            "PS_Hover",
            "PS_Aim",
            "PS_WeaponReady_Idle",
            "PS_WeaponStowed_Idle",
            "PS_Weapon_Draw",
            "PS_Weapon_Sheathe",
            "PS_Walk_Forward",
            "PS_Walk_Backward",
            "PS_Aim_Walk_Forward",
            "PS_Aim_Walk_Backward",
            "PS_WeaponStowed_Walk_Forward",
            "PS_WeaponStowed_Walk_Backward",
            "PS_WeaponStowed_Hover",
            "PS_Reload",
            "PS_BoltCycle"
        };

        private static readonly HashSet<string> LoopingClips =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "PS_Idle",
                "PS_Walk",
                "PS_Hover",
                "PS_Aim",
                "PS_WeaponReady_Idle",
                "PS_WeaponStowed_Idle",
                "PS_Walk_Forward",
                "PS_Walk_Backward",
                "PS_Aim_Walk_Forward",
                "PS_Aim_Walk_Backward",
                "PS_WeaponStowed_Walk_Forward",
                "PS_WeaponStowed_Walk_Backward",
                "PS_WeaponStowed_Hover"
            };

        private static readonly string[] OverrideWeaponActionClips =
        {
            "PS_Weapon_Draw",
            "PS_Weapon_Sheathe",
            "PS_Reload"
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
            SceneAsset importedDemoScene =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath);
            bool demoSceneExistsOnDisk = File.Exists(
                Path.Combine(
                    Directory.GetParent(Application.dataPath)?.FullName ?? ".",
                    DemoScenePath
                )
            );
            if (demoSceneExistsOnDisk && importedDemoScene == null)
            {
                throw new InvalidOperationException(
                    "PoweredSuitAimDemo exists on disk but Unity cannot import it. " +
                    "Integration stopped rather than overwriting that scene."
                );
            }

            bool demoSceneExists = importedDemoScene != null;
            DemoSceneHandling sceneHandling = ResolveDemoSceneHandling(
                demoSceneExists
            );

            ConfigurePrecisionRifleDefinition(overwriteExisting: false);
            ConfigureModelImporter();
            Dictionary<string, AnimationClip> clips = LoadRequiredClips();
            AnimatorController controller = UpdateAnimatorController(clips);

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene generatedDemoScene = default;
            bool integrationSucceeded = false;
            try
            {
                // Integration validates its dependencies but never invokes the
                // broad legacy setup command, which also rewrites loaded scenes
                // and unrelated prefabs. Missing feedback assets must be created
                // explicitly before integration.
                ValidateCombatFeedbackAssets();

                AbilityPrefabSet abilityPrefabs =
                    CreateOrUpdateAbilityPrefabs();
                PowerSuitDemoEnemyContentGenerator.GeneratedContent
                    enemyContent =
                        PowerSuitDemoEnemyContentGenerator.Generate();
                GameObject variant = CreatePlayerVariant(
                    controller,
                    abilityPrefabs,
                    enemyContent.CombatSandboxPrefab
                );
                if (sceneHandling == DemoSceneHandling.CreateAndPopulate)
                {
                    // Defer creation until every required generated asset and
                    // prefab has succeeded. A failed run therefore cannot leave
                    // an empty scene that later runs mistake for user content.
                    generatedDemoScene = CreateEmptyDemoScene();
                    PopulateDemoScene(variant, generatedDemoScene);
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ValidateIntegratedAssets();
                integrationSucceeded = true;
            }
            finally
            {
                if (
                    previousActiveScene.IsValid() &&
                    previousActiveScene.isLoaded
                )
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }

                if (
                    generatedDemoScene.IsValid() &&
                    generatedDemoScene.isLoaded
                )
                {
                    EditorSceneManager.CloseScene(generatedDemoScene, true);
                }

                if (
                    sceneHandling == DemoSceneHandling.CreateAndPopulate &&
                    !integrationSucceeded &&
                    (
                        AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) !=
                        null ||
                        File.Exists(
                            Path.Combine(
                                Directory.GetParent(Application.dataPath)?.FullName ?? ".",
                                DemoScenePath
                            )
                        )
                    )
                )
                {
                    AssetDatabase.DeleteAsset(DemoScenePath);
                }
            }

            Debug.Log(
                "[Powersuit] Generator 109 integration complete. " +
                $"Demo scene: {DemoScenePath} ({sceneHandling})."
            );
        }

        public static DemoSceneHandling ResolveDemoSceneHandling(
            bool demoSceneExists
        )
        {
            return demoSceneExists
                ? DemoSceneHandling.PreserveExisting
                : DemoSceneHandling.CreateAndPopulate;
        }

        [MenuItem("Tools/Powered Suit/Apply Recommended Camera And Rifle Tuning")]
        public static void ApplyRecommendedTuning()
        {
            ConfigurePrecisionRifleDefinition(overwriteExisting: true);
            ConfigureBasePlayerCamera();
            Debug.Log(
                "[Powersuit] Applied the recommended camera and Precision Rifle tuning."
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
                        !configured.Contains(candidate) &&
                        (
                            MatchesImportedClipName(candidate.name, requiredName) ||
                            MatchesImportedClipName(candidate.takeName, requiredName)
                        )
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
                bool shouldLoop = LoopingClips.Contains(requiredName);
                clip.loopTime = shouldLoop;
                clip.loopPose = shouldLoop;
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

        private static void ConfigurePrecisionRifleDefinition(
            bool overwriteExisting
        )
        {
            WeaponDefinition definition =
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(
                    PrecisionRifleDefinitionPath
                );
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<WeaponDefinition>();
                AssetDatabase.CreateAsset(definition, PrecisionRifleDefinitionPath);
            }
            else if (!overwriteExisting)
            {
                return;
            }

            SerializedObject serialized = new SerializedObject(definition);
            SetString(serialized, "weaponId", "precision-rifle");
            SetString(serialized, "displayName", "Precision Rifle");
            SetEnum(serialized, "weaponClass", 1); // PrecisionRifle
            SetEnum(serialized, "triggerMode", 0); // SemiAutomatic
            SetFloat(serialized, "baseDamage", 60f);
            SetFloat(serialized, "roundsPerMinute", 45f);
            SetFloat(serialized, "criticalChance", 0.1f);
            SetFloat(serialized, "criticalDamageMultiplier", 2f);
            SetInt(serialized, "magazineCapacity", 5);
            SetInt(serialized, "startingReserveAmmo", 25);
            SetInt(serialized, "maximumReserveAmmo", 50);
            SetFloat(serialized, "reloadDurationSeconds", 2.8f);
            SetFloat(serialized, "reloadCommitNormalizedTime", 0.89f);
            SetBool(serialized, "requiresManualCycle", true);
            SetFloat(serialized, "manualCycleDurationSeconds", 0.67f);
            SetFloat(serialized, "projectileSpeed", 100f);
            SetFloat(serialized, "projectileLifetimeSeconds", 4f);
            SetFloat(serialized, "projectileRadius", 0.15f);
            SetFloat(serialized, "aimSpreadDegrees", 0.15f);
            SetFloat(serialized, "hipSpreadDegrees", 1.25f);
            SetFloat(serialized, "aimRecoilPitch", 0.9f);
            SetFloat(serialized, "aimRecoilYaw", 0.25f);
            SetFloat(serialized, "hipRecoilPitch", 1.6f);
            SetFloat(serialized, "hipRecoilYaw", 0.5f);
            SetBool(serialized, "supportsScope", true);
            SetFloat(serialized, "shoulderFieldOfViewDegrees", 62f);
            SetFloat(serialized, "scopedFieldOfViewDegrees", 28f);
            SetFloat(
                serialized,
                "shoulderLookSensitivityMultiplier",
                0.9f
            );
            SetFloat(
                serialized,
                "scopedLookSensitivityMultiplier",
                0.45f
            );
            SetFloat(serialized, "aimTransitionSharpness", 22f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssetIfDirty(definition);
        }

        private static bool MatchesImportedClipName(
            string importedName,
            string requiredName
        )
        {
            if (string.Equals(
                importedName,
                requiredName,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                return true;
            }

            return importedName.EndsWith(
                    "|" + requiredName,
                    StringComparison.OrdinalIgnoreCase
                ) ||
                importedName.EndsWith(
                    ":" + requiredName,
                    StringComparison.OrdinalIgnoreCase
                );
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
            Dictionary<string, AnimationClip> layerSafeActionClips =
                OverrideWeaponActionClips.ToDictionary(
                    name => name,
                    name => CreateOrUpdateLayerSafeActionClip(clips[name]),
                    StringComparer.Ordinal
                );
            AnimationClip layerSafeForwardPoseClip =
                CreateOrUpdateLayerSafeActionClip(clips["PS_Aim"]);
            AnimationClip layerSafeAdditiveBoltClip =
                CreateOrUpdateLayerSafeAdditiveClip(clips["PS_BoltCycle"]);

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
            controller.AddParameter("MovementX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MovementY", AnimatorControllerParameterType.Float);
            controller.AddParameter("MovementSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter(
                new AnimatorControllerParameter
                {
                    name = "LocomotionPlaybackSpeed",
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 1f
                }
            );
            controller.AddParameter("IsBackpedaling", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsAimWalking", AnimatorControllerParameterType.Bool);
            controller.AddParameter("WeaponStowed", AnimatorControllerParameterType.Bool);
            controller.AddParameter("DrawWeapon", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("SheatheWeapon", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("ReloadWeapon", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("CycleWeapon", AnimatorControllerParameterType.Trigger);

            AnimatorControllerLayer[] layers = controller.layers;
            if (layers.Length == 0)
            {
                controller.AddLayer("Base Layer");
                layers = controller.layers;
            }

            for (int index = controller.layers.Length - 1; index >= 1; index--)
            {
                controller.RemoveLayer(index);
            }
            layers = controller.layers;

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

            foreach (
                BlendTree staleTree in AssetDatabase
                    .LoadAllAssetsAtPath(ControllerPath)
                    .OfType<BlendTree>()
                    .ToArray()
            )
            {
                UnityEngine.Object.DestroyImmediate(staleTree, true);
            }

            foreach (
                AnimatorStateMachine staleMachine in AssetDatabase
                    .LoadAllAssetsAtPath(ControllerPath)
                    .OfType<AnimatorStateMachine>()
                    .Where(machine => machine != stateMachine)
                    .ToArray()
            )
            {
                UnityEngine.Object.DestroyImmediate(staleMachine, true);
            }

            BlendTree readyLocomotion = CreateDirectionalBlendTree(
                controller,
                "Ready Locomotion Blend",
                clips["PS_Walk_Backward"],
                clips["PS_WeaponReady_Idle"],
                clips["PS_Walk_Forward"]
            );
            BlendTree stowedLocomotion = CreateDirectionalBlendTree(
                controller,
                "Stowed Locomotion Blend",
                clips["PS_WeaponStowed_Walk_Backward"],
                clips["PS_WeaponStowed_Idle"],
                clips["PS_WeaponStowed_Walk_Forward"]
            );
            BlendTree aimLocomotion = CreateDirectionalBlendTree(
                controller,
                "Aim Locomotion Blend",
                clips["PS_Aim_Walk_Backward"],
                clips["PS_Aim"],
                clips["PS_Aim_Walk_Forward"]
            );

            AnimatorState ready = AddState(
                stateMachine,
                "Ready Locomotion",
                readyLocomotion,
                new Vector3(220f, 40f)
            );
            AnimatorState stowed = AddState(
                stateMachine,
                "Stowed Locomotion",
                stowedLocomotion,
                new Vector3(220f, 320f)
            );
            AnimatorState aim = AddState(
                stateMachine,
                "Aim Locomotion",
                aimLocomotion,
                new Vector3(500f, 40f)
            );
            AnimatorState hover = AddState(
                stateMachine,
                "Hover",
                clips["PS_Hover"],
                new Vector3(500f, 320f)
            );
            AnimatorState stowedHover = AddState(
                stateMachine,
                "Stowed Hover",
                clips["PS_WeaponStowed_Hover"],
                new Vector3(500f, 460f)
            );
            foreach (AnimatorState locomotionState in new[] { ready, stowed, aim })
            {
                locomotionState.speedParameterActive = true;
                locomotionState.speedParameter = "LocomotionPlaybackSpeed";
            }
            stateMachine.defaultState = ready;

            AddTransition(
                ready,
                aim,
                Condition("IsAiming", true),
                Condition("IsFlying", false)
            );
            AddTransition(
                aim,
                ready,
                Condition("IsAiming", false),
                Condition("IsFlying", false)
            );
            AddTransition(
                ready,
                stowed,
                Condition("WeaponStowed", true),
                Condition("IsFlying", false)
            );
            AddTransition(
                stowed,
                ready,
                Condition("WeaponStowed", false),
                Condition("IsFlying", false)
            );

            AddTransition(ready, hover, Condition("IsFlying", true));
            AddTransition(aim, hover, Condition("IsFlying", true));
            AddTransition(stowed, stowedHover, Condition("IsFlying", true));
            AddTransition(
                hover,
                stowedHover,
                Condition("IsFlying", true),
                Condition("WeaponStowed", true)
            );
            AddTransition(
                stowedHover,
                hover,
                Condition("IsFlying", true),
                Condition("WeaponStowed", false)
            );
            AddTransition(
                hover,
                stowed,
                Condition("IsFlying", false),
                Condition("WeaponStowed", true)
            );
            AddTransition(
                hover,
                aim,
                Condition("IsFlying", false),
                Condition("WeaponStowed", false),
                Condition("IsAiming", true)
            );
            AddTransition(
                hover,
                ready,
                Condition("IsFlying", false),
                Condition("WeaponStowed", false),
                Condition("IsAiming", false)
            );
            AddTransition(
                stowedHover,
                stowed,
                Condition("IsFlying", false)
            );

            AvatarMask upperBodyMask = CreateOrUpdateUpperBodyMask();
            AnimatorStateMachine forwardPoseStateMachine = new AnimatorStateMachine
            {
                name = "Forward Weapon Pose State Machine",
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(forwardPoseStateMachine, controller);
            AnimatorControllerLayer forwardPoseLayer = new AnimatorControllerLayer
            {
                name = PowerSuitAnimationDriver.ForwardWeaponPoseLayerName,
                defaultWeight = 0f,
                avatarMask = upperBodyMask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = forwardPoseStateMachine
            };
            controller.AddLayer(forwardPoseLayer);

            AnimatorState forwardPose = AddState(
                forwardPoseStateMachine,
                "Forward Weapon Pose",
                layerSafeForwardPoseClip,
                new Vector3(180f, 100f)
            );
            forwardPose.writeDefaultValues = false;
            forwardPoseStateMachine.defaultState = forwardPose;

            AnimatorStateMachine weaponStateMachine = new AnimatorStateMachine
            {
                name = "Weapon Action State Machine",
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(weaponStateMachine, controller);
            AnimatorControllerLayer weaponLayer = new AnimatorControllerLayer
            {
                name = "Weapon Actions",
                // The runtime adapter raises this layer only for an active
                // weapon action. Keeping the serialized default neutral also
                // prevents retained upper-body poses before its first Update.
                defaultWeight = 0f,
                avatarMask = upperBodyMask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = weaponStateMachine
            };
            AnimatorState empty = AddState(
                weaponStateMachine,
                "No Weapon Action",
                null,
                new Vector3(100f, 100f)
            );
            empty.writeDefaultValues = false;
            AnimatorState draw = AddState(
                weaponStateMachine,
                "Draw Weapon",
                layerSafeActionClips["PS_Weapon_Draw"],
                new Vector3(360f, 20f)
            );
            AnimatorState sheathe = AddState(
                weaponStateMachine,
                "Sheathe Weapon",
                layerSafeActionClips["PS_Weapon_Sheathe"],
                new Vector3(360f, 120f)
            );
            AnimatorState reload = AddState(
                weaponStateMachine,
                "Reload",
                layerSafeActionClips["PS_Reload"],
                new Vector3(360f, 220f)
            );
            foreach (AnimatorState actionState in new[] { draw, sheathe, reload })
            {
                // The imported actions include axis/root/lower-body curves that
                // do not belong on this override layer. Layer-safe copies remove
                // them, Write Defaults stays off, and the model root lock handles
                // Unity Generic's synthesized transition pose.
                actionState.writeDefaultValues = false;
            }
            weaponStateMachine.defaultState = empty;
            AddAnyStateTrigger(weaponStateMachine, draw, "DrawWeapon");
            AddAnyStateTrigger(weaponStateMachine, sheathe, "SheatheWeapon");
            AddAnyStateTrigger(weaponStateMachine, reload, "ReloadWeapon");
            AddExitTransition(draw, empty);
            AddExitTransition(sheathe, empty);
            AddExitTransition(reload, empty);

            AnimatorStateMachine boltCycleStateMachine = new AnimatorStateMachine
            {
                name = "Bolt Cycle Action State Machine",
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(boltCycleStateMachine, controller);
            AnimatorControllerLayer boltCycleLayer = new AnimatorControllerLayer
            {
                name = PowerSuitWeaponAnimationDriver.BoltCycleLayerName,
                defaultWeight = 0f,
                avatarMask = upperBodyMask,
                blendingMode = AnimatorLayerBlendingMode.Additive,
                stateMachine = boltCycleStateMachine
            };
            controller.AddLayer(boltCycleLayer);
            controller.AddLayer(weaponLayer);

            AnimatorState noBoltCycle = AddState(
                boltCycleStateMachine,
                PowerSuitWeaponAnimationDriver.NoBoltCycleStateName,
                null,
                new Vector3(100f, 100f)
            );
            noBoltCycle.writeDefaultValues = false;
            AnimatorState cycle = AddState(
                boltCycleStateMachine,
                "Bolt Cycle",
                layerSafeAdditiveBoltClip,
                new Vector3(360f, 100f)
            );
            cycle.writeDefaultValues = false;
            boltCycleStateMachine.defaultState = noBoltCycle;
            AddAnyStateTrigger(boltCycleStateMachine, cycle, "CycleWeapon");
            AddExitTransition(cycle, noBoltCycle);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            return controller;
        }

        private static AnimationClip CreateOrUpdateLayerSafeActionClip(
            AnimationClip source
        )
        {
            string assetPath =
                $"Assets/Game/Animation/{source.name}{LayerSafeActionClipSuffix}.anim";
            AnimationClip target = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (target == null)
            {
                target = new AnimationClip();
                AssetDatabase.CreateAsset(target, assetPath);
            }

            // Rebuild the clip curve-by-curve instead of cloning the imported
            // FBX's serialized payload. Imported Generic clips can retain baked
            // root data even after their visible empty-path curve is removed.
            // A fresh whitelist also keeps Root, Hips, and legs entirely out of
            // the override layer rather than trusting an AvatarMask alone.
            target.ClearCurves();
            target.name = source.name + LayerSafeActionClipSuffix;
            target.frameRate = source.frameRate;
            target.wrapMode = source.wrapMode;
            target.legacy = source.legacy;
            AnimationUtility.SetAnimationClipSettings(
                target,
                AnimationUtility.GetAnimationClipSettings(source)
            );

            foreach (
                EditorCurveBinding binding in AnimationUtility
                    .GetCurveBindings(source)
                    .Where(binding => IsLayerSafeActionBindingPath(binding.path))
            )
            {
                AnimationUtility.SetEditorCurve(
                    target,
                    binding,
                    AnimationUtility.GetEditorCurve(source, binding)
                );
            }

            foreach (
                EditorCurveBinding binding in AnimationUtility
                    .GetObjectReferenceCurveBindings(source)
                    .Where(binding => IsLayerSafeActionBindingPath(binding.path))
            )
            {
                AnimationUtility.SetObjectReferenceCurve(
                    target,
                    binding,
                    AnimationUtility.GetObjectReferenceCurve(source, binding)
                );
            }
            AnimationUtility.SetAnimationEvents(
                target,
                AnimationUtility.GetAnimationEvents(source)
            );

            if (
                AnimationUtility.GetCurveBindings(target)
                    .Any(binding => !IsLayerSafeActionBindingPath(binding.path)) ||
                AnimationUtility.GetObjectReferenceCurveBindings(target)
                    .Any(binding => !IsLayerSafeActionBindingPath(binding.path))
            )
            {
                throw new InvalidOperationException(
                    $"Layer-safe action '{target.name}' contains a root or lower-body binding."
                );
            }

            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
            return target;
        }

        private static AnimationClip CreateOrUpdateLayerSafeAdditiveClip(
            AnimationClip source
        )
        {
            AnimationClip target = CreateOrUpdateLayerSafeActionClip(source);
            AnimationClipSettings settings =
                AnimationUtility.GetAnimationClipSettings(target);
            settings.loopTime = false;
            settings.hasAdditiveReferencePose = true;
            settings.additiveReferencePoseClip = source;
            settings.additiveReferencePoseTime = 0f;
            AnimationUtility.SetAnimationClipSettings(target, settings);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
            return target;
        }

        private static bool IsLayerSafeActionBindingPath(string path)
        {
            return path == "Root/Hips/Spine" ||
                path.StartsWith("Root/Hips/Spine/", StringComparison.Ordinal) ||
                path == "WeaponRoot" ||
                path == "WeaponRoot/WeaponMagazine" ||
                path.StartsWith(
                    "WeaponRoot/WeaponMagazine/",
                    StringComparison.Ordinal
                ) ||
                path == "WeaponRoot/WeaponBolt" ||
                path.StartsWith(
                    "WeaponRoot/WeaponBolt/",
                    StringComparison.Ordinal
                );
        }

        private static BlendTree CreateDirectionalBlendTree(
            AnimatorController controller,
            string name,
            Motion backward,
            Motion idle,
            Motion forward
        )
        {
            BlendTree tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = "MovementY",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            tree.AddChild(backward, -1f);
            tree.AddChild(idle, 0f);
            tree.AddChild(forward, 1f);
            AssetDatabase.AddObjectToAsset(tree, controller);
            return tree;
        }

        private static AvatarMask CreateOrUpdateUpperBodyMask()
        {
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(
                UpperBodyMaskPath
            );
            if (mask == null)
            {
                mask = new AvatarMask
                {
                    name = "PowerSuitUpperBody"
                };
                AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
            }

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                throw new InvalidOperationException(
                    "Cannot build the upper-body mask without the powered-suit model."
                );
            }

            mask.transformCount = 0;
            mask.AddTransformPath(model.transform, true);
            for (int index = 0; index < mask.transformCount; index++)
            {
                mask.SetTransformActive(
                    index,
                    IsUpperBodyAnimationPath(mask.GetTransformPath(index))
                );
            }

            EditorUtility.SetDirty(mask);
            AssetDatabase.SaveAssetIfDirty(mask);
            return mask;
        }

        private static bool IsUpperBodyAnimationPath(string path)
        {
            string leaf = path;
            int separator = leaf.LastIndexOf('/');
            if (separator >= 0)
            {
                leaf = leaf.Substring(separator + 1);
            }

            return leaf == "Spine" ||
                leaf == "Chest" ||
                leaf == "Neck" ||
                leaf == "Head" ||
                leaf.StartsWith("Shoulder.", StringComparison.Ordinal) ||
                leaf.StartsWith("UpperArm.", StringComparison.Ordinal) ||
                leaf.StartsWith("LowerArm.", StringComparison.Ordinal) ||
                leaf.StartsWith("Hand.", StringComparison.Ordinal) ||
                leaf == "WeaponRoot" ||
                leaf == "WeaponMagazine" ||
                leaf == "WeaponBolt";
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

        private static void AddAnyStateTrigger(
            AnimatorStateMachine stateMachine,
            AnimatorState destination,
            string triggerName
        )
        {
            AnimatorStateTransition transition =
                stateMachine.AddAnyStateTransition(destination);
            ConfigureTransition(transition);
            transition.canTransitionToSelf = false;
            transition.AddCondition(
                AnimatorConditionMode.If,
                0f,
                triggerName
            );
        }

        private static void AddExitTransition(
            AnimatorState source,
            AnimatorState destination,
            params AnimatorCondition[] conditions
        )
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.hasFixedDuration = true;
            transition.duration = 0.02f;
            transition.interruptionSource = TransitionInterruptionSource.None;
            foreach (AnimatorCondition condition in conditions)
            {
                transition.AddCondition(
                    condition.mode,
                    condition.threshold,
                    condition.parameter
                );
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

        private static Scene CreateEmptyDemoScene()
        {
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive
            );
            if (!EditorSceneManager.SaveScene(scene, DemoScenePath))
            {
                EditorSceneManager.CloseScene(scene, true);
                throw new InvalidOperationException("Could not create the Generator 109 demo scene.");
            }

            return scene;
        }

        private static AbilityPrefabSet CreateOrUpdateAbilityPrefabs()
        {
            EnsureAssetFolder(AbilityPrefabFolder);
            Scene previewScene = EditorSceneManager.NewPreviewScene();

            try
            {
                GameObject rocketObject = GameObject.CreatePrimitive(
                    PrimitiveType.Capsule
                );
                SceneManager.MoveGameObjectToScene(
                    rocketObject,
                    previewScene
                );
                rocketObject.name = "ShoulderRocketProjectile";
                rocketObject.transform.localScale =
                    new Vector3(0.14f, 0.28f, 0.14f);
                UnityEngine.Object.DestroyImmediate(
                    rocketObject.GetComponent<Collider>()
                );
                rocketObject.AddComponent<AbilityAreaEffectPresentation>();
                ShoulderRocketProjectile rocket =
                    rocketObject.AddComponent<ShoulderRocketProjectile>();
                TrailRenderer rocketTrail =
                    rocketObject.AddComponent<TrailRenderer>();
                rocketTrail.time = 0.28f;
                rocketTrail.startWidth = 0.12f;
                rocketTrail.endWidth = 0.01f;
                GameObject rocketPrefab = PrefabUtility.SaveAsPrefabAsset(
                    rocketObject,
                    RocketPrefabPath
                );
                UnityEngine.Object.DestroyImmediate(rocketObject);

                GameObject lightningObject = new GameObject(
                    "LightningStrikeActor"
                );
                SceneManager.MoveGameObjectToScene(
                    lightningObject,
                    previewScene
                );
                GameObject lightningVisual = GameObject.CreatePrimitive(
                    PrimitiveType.Cylinder
                );
                SceneManager.MoveGameObjectToScene(
                    lightningVisual,
                    previewScene
                );
                lightningVisual.name = "LightningAreaVisual";
                lightningVisual.transform.SetParent(
                    lightningObject.transform,
                    false
                );
                lightningVisual.transform.localScale =
                    new Vector3(1f, 0.02f, 1f);
                UnityEngine.Object.DestroyImmediate(
                    lightningVisual.GetComponent<Collider>()
                );
                lightningObject.AddComponent<AbilityAreaEffectPresentation>();
                LightningStrikeActor lightning =
                    lightningObject.AddComponent<LightningStrikeActor>();
                SetObjectReference(
                    new SerializedObject(lightning),
                    "visualRoot",
                    lightningVisual.transform
                );
                GameObject lightningPrefab = PrefabUtility.SaveAsPrefabAsset(
                    lightningObject,
                    LightningPrefabPath
                );
                UnityEngine.Object.DestroyImmediate(lightningObject);

                GameObject voidObject = new GameObject("VoidOrbFieldActor");
                SceneManager.MoveGameObjectToScene(voidObject, previewScene);
                GameObject voidVisual = GameObject.CreatePrimitive(
                    PrimitiveType.Sphere
                );
                SceneManager.MoveGameObjectToScene(voidVisual, previewScene);
                voidVisual.name = "VoidOrbVisual";
                voidVisual.transform.SetParent(voidObject.transform, false);
                UnityEngine.Object.DestroyImmediate(
                    voidVisual.GetComponent<Collider>()
                );
                VoidOrbFieldActor voidActor =
                    voidObject.AddComponent<VoidOrbFieldActor>();
                SetObjectReference(
                    new SerializedObject(voidActor),
                    "visualRoot",
                    voidVisual.transform
                );
                GameObject voidPrefab = PrefabUtility.SaveAsPrefabAsset(
                    voidObject,
                    VoidPrefabPath
                );
                UnityEngine.Object.DestroyImmediate(voidObject);

                GameObject indicatorObject = GameObject.CreatePrimitive(
                    PrimitiveType.Cylinder
                );
                SceneManager.MoveGameObjectToScene(
                    indicatorObject,
                    previewScene
                );
                indicatorObject.name = "AbilityTargetIndicator";
                indicatorObject.transform.localScale =
                    new Vector3(1f, 0.02f, 1f);
                UnityEngine.Object.DestroyImmediate(
                    indicatorObject.GetComponent<Collider>()
                );
                indicatorObject.AddComponent<AbilityAreaEffectPresentation>();
                AbilityTargetIndicator indicator =
                    indicatorObject.AddComponent<AbilityTargetIndicator>();
                GameObject indicatorPrefab = PrefabUtility.SaveAsPrefabAsset(
                    indicatorObject,
                    TargetIndicatorPrefabPath
                );
                UnityEngine.Object.DestroyImmediate(indicatorObject);

                if (
                    rocketPrefab == null ||
                    lightningPrefab == null ||
                    voidPrefab == null ||
                    indicatorPrefab == null
                )
                {
                    throw new InvalidOperationException(
                        "Could not create one or more ability prefabs."
                    );
                }

                return new AbilityPrefabSet(
                    rocketPrefab.GetComponent<ShoulderRocketProjectile>(),
                    lightningPrefab.GetComponent<LightningStrikeActor>(),
                    voidPrefab.GetComponent<VoidOrbFieldActor>(),
                    indicatorPrefab.GetComponent<AbilityTargetIndicator>()
                );
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
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

        private static GameObject CreatePlayerVariant(
            AnimatorController controller,
            AbilityPrefabSet abilityPrefabs,
            GameObject demoWorldPrefab
        )
        {
            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePlayerPrefabPath);
            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (basePrefab == null || modelPrefab == null)
            {
                throw new InvalidOperationException("The base player prefab or Generator 109 model is missing.");
            }

            Scene previewScene = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;

            try
            {
                instance = PrefabUtility.InstantiatePrefab(
                    basePrefab,
                    previewScene
                ) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate the base player prefab."
                    );
                }

                instance.name = "PlayerPrototype_Generator109";
                RemoveLegacyVisuals(instance.transform);

                GameObject visualWrapper = new GameObject("PowerSuitVisual_Generator109");
                visualWrapper.transform.SetParent(instance.transform, false);
                visualWrapper.transform.localPosition = Vector3.zero;
                visualWrapper.transform.localRotation = ModelFacingCorrection;
                visualWrapper.transform.localScale = Vector3.one;

                GameObject modelInstance =
                    PrefabUtility.InstantiatePrefab(modelPrefab, visualWrapper.transform) as GameObject;
                if (modelInstance == null)
                {
                    throw new InvalidOperationException("Could not instantiate the Generator 109 FBX.");
                }

                modelInstance.name = "PowerSuitModel_Generator111";
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
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

                PowerSuitAnimatorRootLock rootLock =
                    modelInstance.GetComponent<PowerSuitAnimatorRootLock>();
                if (rootLock == null)
                {
                    rootLock = modelInstance.AddComponent<PowerSuitAnimatorRootLock>();
                }
                rootLock.CaptureCurrentLocalTransform();

                Transform muzzle = FindChildRecursive(modelInstance.transform, "Rifle_Muzzle");
                Transform rifleRoot = FindChildRecursive(modelInstance.transform, "RifleRoot");
                Transform sightOcular = FindChildRecursive(
                    modelInstance.transform,
                    "Rifle_SightOcular"
                );
                Transform shoulder = FindChildRecursive(
                    modelInstance.transform,
                    "Shoulder.R"
                );
                if (
                    muzzle == null ||
                    rifleRoot == null ||
                    sightOcular == null ||
                    shoulder == null
                )
                {
                    throw new InvalidOperationException(
                        "The imported Generator 111 hierarchy does not expose " +
                        "RifleRoot/Rifle_Muzzle/Rifle_SightOcular/Shoulder.R."
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

                ConfigureAimCamera(
                    suitController,
                    walkSpeed: 6.5f,
                    applyResponsiveFeel: true
                );
                ConfigureResponsiveAnimation(animationDriver);
                suitController.ScopePoint = CreateScopeAdapter(sightOcular);
                PowerSuitInputRouter inputRouter =
                    instance.GetComponent<PowerSuitInputRouter>();
                if (inputRouter == null)
                {
                    inputRouter = instance.AddComponent<PowerSuitInputRouter>();
                }
                PowerSuitFramePacing framePacing =
                    instance.GetComponent<PowerSuitFramePacing>();
                if (framePacing == null)
                {
                    framePacing = instance.AddComponent<PowerSuitFramePacing>();
                }
                ConfigureFramePacing(framePacing);
                weapon.MuzzleTransform = CreateMuzzleAdapter(muzzle);
                WeaponDefinition weaponDefinition =
                    AssetDatabase.LoadAssetAtPath<WeaponDefinition>(
                        PrecisionRifleDefinitionPath
                    );
                if (weaponDefinition == null)
                {
                    throw new InvalidOperationException(
                        "The Precision Rifle WeaponDefinition is missing."
                    );
                }
                weapon.Definition = weaponDefinition;
                weapon.ShowLegacyAmmoHud = false;

                PlayerHealth playerHealth = instance.GetComponent<PlayerHealth>();
                if (playerHealth == null)
                {
                    throw new InvalidOperationException(
                        "The player variant is missing PlayerHealth."
                    );
                }
                playerHealth.ShowLegacyHealthHud = false;

                PowerSuitWeaponPresentation presentation =
                    instance.GetComponent<PowerSuitWeaponPresentation>();
                if (presentation == null)
                {
                    presentation = instance.AddComponent<PowerSuitWeaponPresentation>();
                }

                PowerSuitWeaponAnimationDriver weaponAnimationDriver =
                    instance.GetComponent<PowerSuitWeaponAnimationDriver>();
                if (weaponAnimationDriver == null)
                {
                    weaponAnimationDriver =
                        instance.AddComponent<PowerSuitWeaponAnimationDriver>();
                }

                PowerSuitVisualFlightResponse visualFlightResponse =
                    instance.GetComponent<PowerSuitVisualFlightResponse>();
                if (visualFlightResponse == null)
                {
                    visualFlightResponse =
                        instance.AddComponent<PowerSuitVisualFlightResponse>();
                }
                visualFlightResponse.VisualRoot = visualWrapper.transform;

                ShoulderRocketAbility shoulderRocket =
                    instance.GetComponent<ShoulderRocketAbility>();
                if (shoulderRocket == null)
                {
                    shoulderRocket =
                        instance.AddComponent<ShoulderRocketAbility>();
                }
                LightningStrikeAbility lightningStrike =
                    instance.GetComponent<LightningStrikeAbility>();
                if (lightningStrike == null)
                {
                    lightningStrike =
                        instance.AddComponent<LightningStrikeAbility>();
                }
                VoidUltimateAbility voidUltimate =
                    instance.GetComponent<VoidUltimateAbility>();
                if (voidUltimate == null)
                {
                    voidUltimate = instance.AddComponent<VoidUltimateAbility>();
                }

                Transform shoulderMuzzle = CreateShoulderMuzzle(shoulder);
                GameObject indicatorObject = PrefabUtility.InstantiatePrefab(
                    abilityPrefabs.TargetIndicator.gameObject,
                    instance.transform
                ) as GameObject;
                AbilityTargetIndicator targetIndicator =
                    indicatorObject != null
                        ? indicatorObject.GetComponent<AbilityTargetIndicator>()
                        : null;
                if (targetIndicator == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate the ability target indicator."
                    );
                }
                indicatorObject.name = "AbilityTargetIndicator";

                PowerSuitAbilityController abilityController =
                    instance.GetComponent<PowerSuitAbilityController>();
                if (abilityController == null)
                {
                    abilityController =
                        instance.AddComponent<PowerSuitAbilityController>();
                }
                abilityController.Configure(
                    suitController,
                    inputRouter,
                    instance.GetComponent<PlayerHealth>(),
                    weapon,
                    shoulderRocket,
                    lightningStrike,
                    voidUltimate,
                    shoulderMuzzle,
                    targetIndicator,
                    abilityPrefabs.Rocket,
                    abilityPrefabs.Lightning,
                    abilityPrefabs.VoidField
                );

                PowerSuitHudPresenter hudPresenter = CreatePlayerHud(
                    instance,
                    playerHealth,
                    weapon,
                    shoulderRocket,
                    lightningStrike,
                    voidUltimate
                );
                if (demoWorldPrefab == null)
                {
                    throw new InvalidOperationException(
                        "The generated PowerSuit combat sandbox prefab is missing."
                    );
                }

                PowerSuitDemoBootstrap demoBootstrap =
                    instance.GetComponent<PowerSuitDemoBootstrap>();
                if (demoBootstrap == null)
                {
                    demoBootstrap = instance.AddComponent<
                        PowerSuitDemoBootstrap
                    >();
                }
                demoBootstrap.ConfigureForPlayerPrefab(
                    demoWorldPrefab,
                    instance.transform,
                    hudPresenter,
                    shouldInitializeOnStart: true
                );
                DeveloperConsoleOverlay consoleOverlay =
                    ConfigureDeveloperConsole(
                    instance,
                    inputRouter,
                    suitController,
                    weapon,
                    presentation,
                    abilityController
                );
                DeveloperConsoleGameplayCommandPack consoleCommands =
                    instance.GetComponent<
                        DeveloperConsoleGameplayCommandPack
                    >();
                if (consoleCommands == null)
                {
                    consoleCommands = instance.AddComponent<
                        DeveloperConsoleGameplayCommandPack
                    >();
                }
                consoleCommands.Configure(
                    consoleOverlay,
                    instance.GetComponent<PlayerHealth>(),
                    suitController,
                    weapon,
                    abilityController,
                    shoulderRocket,
                    lightningStrike,
                    voidUltimate
                );

                SerializedObject driverObject = new SerializedObject(animationDriver);
                driverObject.FindProperty("controller").objectReferenceValue = suitController;
                driverObject.FindProperty("animator").objectReferenceValue = animator;
                driverObject.FindProperty("weaponPresentation").objectReferenceValue = presentation;
                driverObject.ApplyModifiedPropertiesWithoutUndo();

                SerializedObject presentationObject = new SerializedObject(presentation);
                presentationObject.FindProperty("controller").objectReferenceValue = suitController;
                presentationObject.FindProperty("animator").objectReferenceValue = animator;
                presentationObject.FindProperty("weapon").objectReferenceValue = weapon;
                presentationObject.FindProperty("weaponAnimationDriver").objectReferenceValue =
                    weaponAnimationDriver;
                presentationObject.FindProperty("startsStowed").boolValue = false;
                presentationObject.FindProperty("drawDuration").floatValue = 1f;
                presentationObject.FindProperty("sheatheDuration").floatValue = 1f;
                presentationObject.ApplyModifiedPropertiesWithoutUndo();

                SerializedObject weaponAnimationObject =
                    new SerializedObject(weaponAnimationDriver);
                weaponAnimationObject.FindProperty("weapon").objectReferenceValue = weapon;
                weaponAnimationObject.FindProperty("animator").objectReferenceValue = animator;
                weaponAnimationObject.ApplyModifiedPropertiesWithoutUndo();

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
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }

                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static void ConfigureBasePlayerCamera()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BasePlayerPrefabPath);
            try
            {
                PowerSuitController controller =
                    root.GetComponent<PowerSuitController>();
                if (controller == null)
                {
                    throw new InvalidOperationException(
                        "The base player prefab is missing PowerSuitController."
                    );
                }

                // The legacy FlightPrototype uses this base prefab at its
                // original movement tune. Persist the shared camera profiles
                // without silently applying the focused demo's slower walk.
                ConfigureAimCamera(
                    controller,
                    walkSpeed: 5f,
                    applyResponsiveFeel: false
                );
                PrefabUtility.SaveAsPrefabAsset(root, BasePlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
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

        private static Transform CreateMuzzleAdapter(Transform importedMuzzle)
        {
            GameObject adapter = new GameObject("WeaponMuzzle");
            adapter.transform.SetParent(importedMuzzle, false);
            adapter.transform.localPosition = Vector3.zero;
            adapter.transform.localRotation = MuzzleAdapterRotation;
            adapter.transform.localScale = Vector3.one;
            return adapter.transform;
        }

        private static Transform CreateScopeAdapter(Transform importedOcular)
        {
            GameObject adapter = new GameObject("WeaponScopePoint");
            adapter.transform.SetParent(importedOcular, false);
            adapter.transform.localPosition = Vector3.zero;
            adapter.transform.localRotation = MuzzleAdapterRotation;
            adapter.transform.localScale = Vector3.one;
            return adapter.transform;
        }

        private static Transform CreateShoulderMuzzle(Transform shoulder)
        {
            GameObject muzzle = new GameObject("ShoulderMuzzle");
            muzzle.transform.SetParent(shoulder, false);
            muzzle.transform.localPosition = Vector3.zero;
            muzzle.transform.localRotation = Quaternion.identity;
            muzzle.transform.localScale = Vector3.one;
            return muzzle.transform;
        }

        private static void ConfigureAimCamera(
            PowerSuitController controller,
            float walkSpeed,
            bool applyResponsiveFeel
        )
        {
            SerializedObject serialized = new SerializedObject(controller);
            SetFloat(serialized, "walkSpeed", walkSpeed);
            if (applyResponsiveFeel)
            {
                SetFloat(serialized, "groundAcceleration", 55f);
                SetFloat(serialized, "flightSpeed", 14f);
                SetFloat(serialized, "boostSpeed", 28f);
                SetFloat(serialized, "flightAcceleration", 38f);
                SetFloat(serialized, "turningSpeed", 20f);
                SetFloat(serialized, "combatTurningSpeed", 32f);
                SetFloat(serialized, "mouseSensitivity", 0.18f);
                SetFloat(serialized, "controllerLookSpeed", 180f);
                SetFloat(serialized, "movementSettings.groundDeceleration", 65f);
                SetFloat(
                    serialized,
                    "movementSettings.groundBrakingAcceleration",
                    105f
                );
                SetFloat(serialized, "movementSettings.airAcceleration", 16f);
                SetFloat(serialized, "movementSettings.airDeceleration", 4f);
                SetFloat(
                    serialized,
                    "movementSettings.airBrakingAcceleration",
                    22f
                );
                SetFloat(serialized, "movementSettings.flightDeceleration", 30f);
                SetFloat(
                    serialized,
                    "movementSettings.flightBrakingAcceleration",
                    55f
                );
                SetFloat(
                    serialized,
                    "movementSettings.flightVerticalSpeed",
                    11f
                );
                SetFloat(
                    serialized,
                    "movementSettings.boostVerticalSpeed",
                    18f
                );
                SetFloat(
                    serialized,
                    "movementSettings.flightVerticalAcceleration",
                    36f
                );
                SetFloat(
                    serialized,
                    "movementSettings.flightVerticalDeceleration",
                    30f
                );
                SetFloat(
                    serialized,
                    "movementSettings.flightVerticalBrakingAcceleration",
                    55f
                );
                SetFloat(
                    serialized,
                    "movementSettings.boostAccelerationMultiplier",
                    1.7f
                );
            }
            SetFloat(serialized, "cameraDistance", 9.5f);
            SetFloat(serialized, "cameraHeight", 1.5f);
            SetFloat(serialized, "defaultFieldOfView", 72f);
            SetFloat(serialized, "flightCameraDistance", 11f);
            SetFloat(serialized, "flightCameraHeight", 1.75f);
            SetFloat(serialized, "flightFieldOfView", 74f);
            SetFloat(serialized, "boostCameraDistance", 12f);
            SetFloat(serialized, "boostCameraHeight", 1.8f);
            SetFloat(serialized, "boostFieldOfView", 82f);
            SetFloat(serialized, "cameraCollisionPadding", 0.05f);
            SetFloat(serialized, "cameraCollisionReleaseSharpness", 14f);
            SetFloat(
                serialized,
                "cameraLookSharpness",
                applyResponsiveFeel ? 45f : 28f
            );
            SetFloat(serialized, "aimCameraDistance", 4.3f);
            SetFloat(serialized, "aimCameraHeight", 1.45f);
            // The shouldered rifle sits on player-local -X. Keeping the camera
            // on +X put the suit between the lens and weapon, so use the firing
            // side and lift slightly to expose the receiver and barrel.
            SetVector(serialized, "aimShoulderOffset", new Vector3(-1.2f, 0.05f, 0f));
            SetFloat(serialized, "aimFieldOfView", 62f);
            SetFloat(
                serialized,
                "aimTransitionSpeed",
                applyResponsiveFeel ? 22f : 12f
            );
            SetFloat(serialized, "scopeEyeRelief", 0.045f);
            SetFloat(serialized, "scopedNearClipPlane", 0.02f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureFramePacing(PowerSuitFramePacing framePacing)
        {
            SerializedObject serialized = new SerializedObject(framePacing);
            SetBool(serialized, "runInBackground", true);
            SetBool(serialized, "synchronizeToDisplay", true);
            SetInt(serialized, "fallbackTargetFrameRate", 60);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureResponsiveAnimation(
            PowerSuitAnimationDriver animationDriver
        )
        {
            SerializedObject serialized = new SerializedObject(animationDriver);
            SetFloat(serialized, "movementDamping", 0.06f);
            SetFloat(serialized, "fullSpeedLocomotionPlayback", 4.5f);
            SetFloat(serialized, "forwardPoseBlendSharpness", 22f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static PowerSuitHudPresenter CreatePlayerHud(
            GameObject player,
            PlayerHealth health,
            PowerSuitWeapon weapon,
            ShoulderRocketAbility rocket,
            LightningStrikeAbility lightning,
            VoidUltimateAbility ultimate
        )
        {
            Transform existing = player.transform.Find("PowerSuitHUD");
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            GameObject hudObject = new GameObject(
                "PowerSuitHUD",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(PowerSuitHudPresenter)
            );
            hudObject.transform.SetParent(player.transform, false);
            Canvas canvas = hudObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            CanvasScaler scaler = hudObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject safeAreaObject = new GameObject(
                "SafeArea",
                typeof(RectTransform),
                typeof(PowerSuitHudSafeArea)
            );
            RectTransform safeArea = safeAreaObject.GetComponent<RectTransform>();
            safeArea.SetParent(hudObject.transform, false);
            safeArea.anchorMin = Vector2.zero;
            safeArea.anchorMax = Vector2.one;
            safeArea.offsetMin = Vector2.zero;
            safeArea.offsetMax = Vector2.zero;

            HudWidget healthWidget = CreateHudWidget(
                safeArea,
                "Health",
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(30f, 30f),
                new Vector2(280f, 48f),
                TextAnchor.MiddleLeft
            );
            HudWidget ammoWidget = CreateHudWidget(
                safeArea,
                "Ammunition",
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-30f, 30f),
                new Vector2(260f, 52f),
                TextAnchor.MiddleCenter
            );
            HudWidget reloadWidget = CreateHudWidget(
                safeArea,
                "Reload",
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-30f, 90f),
                new Vector2(260f, 38f),
                TextAnchor.MiddleCenter
            );
            HudWidget rocketWidget = CreateHudWidget(
                safeArea,
                "Rocket",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(-240f, 30f),
                new Vector2(210f, 44f),
                TextAnchor.MiddleCenter
            );
            HudWidget lightningWidget = CreateHudWidget(
                safeArea,
                "Lightning",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 30f),
                new Vector2(210f, 44f),
                TextAnchor.MiddleCenter
            );
            HudWidget ultimateWidget = CreateHudWidget(
                safeArea,
                "Ultimate",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(240f, 30f),
                new Vector2(210f, 44f),
                TextAnchor.MiddleCenter
            );

            PowerSuitHudPresenter presenter =
                hudObject.GetComponent<PowerSuitHudPresenter>();
            SerializedObject serialized = new SerializedObject(presenter);
            SetObjectReference(serialized, "healthSource", health);
            SetObjectReference(serialized, "weaponSource", weapon);
            SetObjectReference(serialized, "shoulderRocketSource", rocket);
            SetObjectReference(serialized, "lightningSource", lightning);
            SetObjectReference(serialized, "ultimateSource", ultimate);
            ConfigureHudWidget(
                serialized,
                "health",
                healthWidget
            );
            ConfigureHudWidget(
                serialized,
                "ammunition",
                ammoWidget,
                includeFill: false
            );
            ConfigureHudWidget(
                serialized,
                "reload",
                reloadWidget
            );
            ConfigureHudWidget(
                serialized,
                "shoulderRocket",
                rocketWidget
            );
            ConfigureHudWidget(
                serialized,
                "lightning",
                lightningWidget
            );
            ConfigureHudWidget(
                serialized,
                "ultimate",
                ultimateWidget
            );
            return presenter;
        }

        private static HudWidget CreateHudWidget(
            Transform parent,
            string name,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size,
            TextAnchor alignment
        )
        {
            GameObject root = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image)
            );
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(parent, false);
            rootRect.anchorMin = anchor;
            rootRect.anchorMax = anchor;
            rootRect.pivot = pivot;
            rootRect.anchoredPosition = anchoredPosition;
            rootRect.sizeDelta = size;
            Image background = root.GetComponent<Image>();
            background.color = new Color(0.015f, 0.035f, 0.06f, 0.78f);

            GameObject fillObject = new GameObject(
                "Fill",
                typeof(RectTransform),
                typeof(Image)
            );
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.SetParent(rootRect, false);
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 0f);
            fillRect.pivot = new Vector2(0f, 0f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(0f, 6f);
            Image fill = fillObject.GetComponent<Image>();
            fill.color = new Color(0.15f, 0.82f, 1f, 0.95f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 1f;

            GameObject labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(Text)
            );
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(rootRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 6f);
            labelRect.offsetMax = new Vector2(-10f, 0f);
            Text label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf"
            );
            label.fontSize = 17;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;

            return new HudWidget(root, fill, label);
        }

        private static void ConfigureHudWidget(
            SerializedObject serialized,
            string prefix,
            HudWidget widget,
            bool includeFill = true
        )
        {
            SetObjectReference(serialized, prefix + "Root", widget.Root);
            if (includeFill)
            {
                SetObjectReference(serialized, prefix + "Fill", widget.Fill);
            }
            SetObjectReference(serialized, prefix + "Label", widget.Label);
        }

        private static DeveloperConsoleOverlay ConfigureDeveloperConsole(
            GameObject player,
            PowerSuitInputRouter inputRouter,
            PowerSuitController controller,
            PowerSuitWeapon weapon,
            PowerSuitWeaponPresentation presentation,
            PowerSuitAbilityController abilityController
        )
        {
            DeveloperConsoleOverlay overlay =
                player.GetComponent<DeveloperConsoleOverlay>();
            if (overlay == null)
            {
                overlay = player.AddComponent<DeveloperConsoleOverlay>();
            }

            SerializedObject serialized = new SerializedObject(overlay);
            SerializedProperty behaviours = serialized.FindProperty(
                "gameplayInputBehaviours"
            );
            behaviours.arraySize = 5;
            behaviours.GetArrayElementAtIndex(0).objectReferenceValue =
                inputRouter;
            behaviours.GetArrayElementAtIndex(1).objectReferenceValue =
                controller;
            behaviours.GetArrayElementAtIndex(2).objectReferenceValue = weapon;
            behaviours.GetArrayElementAtIndex(3).objectReferenceValue =
                presentation;
            behaviours.GetArrayElementAtIndex(4).objectReferenceValue =
                abilityController;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return overlay;
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

        private static void SetInt(
            SerializedObject serialized,
            string propertyName,
            int value
        )
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Missing serialized property: {propertyName}");
            }
            property.intValue = value;
        }

        private static void SetEnum(
            SerializedObject serialized,
            string propertyName,
            int value
        )
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Missing serialized property: {propertyName}");
            }
            property.enumValueIndex = value;
        }

        private static void SetBool(
            SerializedObject serialized,
            string propertyName,
            bool value
        )
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Missing serialized property: {propertyName}");
            }
            property.boolValue = value;
        }

        private static void SetString(
            SerializedObject serialized,
            string propertyName,
            string value
        )
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Missing serialized property: {propertyName}");
            }
            property.stringValue = value;
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

        private static void SetObjectReference(
            SerializedObject serialized,
            string propertyName,
            UnityEngine.Object value
        )
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"Missing serialized property: {propertyName}"
                );
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void PopulateDemoScene(
            GameObject playerVariant,
            Scene scene
        )
        {
            if (!scene.IsValid() || scene.path != DemoScenePath)
            {
                throw new InvalidOperationException(
                    "The Generator 109 demo scene is unavailable."
                );
            }

            if (!SceneManager.SetActiveScene(scene))
            {
                throw new InvalidOperationException(
                    "Could not activate the isolated Generator 109 demo scene."
                );
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

            HashSet<string> stateNames = controller.layers
                .SelectMany(layer => layer.stateMachine.states)
                .Select(child => child.state.name)
                .ToHashSet();
            string[] requiredStates =
            {
                "Ready Locomotion",
                "Stowed Locomotion",
                "Aim Locomotion",
                "Hover",
                "Stowed Hover",
                "Forward Weapon Pose",
                "Draw Weapon",
                "Sheathe Weapon",
                "Reload",
                "Bolt Cycle"
            };
            if (!requiredStates.All(stateNames.Contains))
            {
                throw new InvalidOperationException(
                    "PowerSuitAnimator is missing one or more combat-animation states."
                );
            }

            AnimatorControllerLayer forwardPoseLayer = controller.layers
                .SingleOrDefault(
                    layer => layer.name == PowerSuitAnimationDriver.ForwardWeaponPoseLayerName
                );
            AnimatorControllerLayer weaponLayer = controller.layers
                .SingleOrDefault(
                    layer => layer.name == PowerSuitWeaponAnimationDriver.WeaponActionLayerName
                );
            AnimatorControllerLayer boltCycleLayer = controller.layers
                .SingleOrDefault(
                    layer => layer.name == PowerSuitWeaponAnimationDriver.BoltCycleLayerName
                );
            if (
                controller.layers.Length != 4 ||
                !controller.layers.Select(layer => layer.name).SequenceEqual(
                    new[]
                    {
                        "Base Layer",
                        PowerSuitAnimationDriver.ForwardWeaponPoseLayerName,
                        PowerSuitWeaponAnimationDriver.BoltCycleLayerName,
                        PowerSuitWeaponAnimationDriver.WeaponActionLayerName
                    }
                ) ||
                forwardPoseLayer == null ||
                forwardPoseLayer.avatarMask == null ||
                forwardPoseLayer.defaultWeight != 0f ||
                forwardPoseLayer.blendingMode != AnimatorLayerBlendingMode.Override ||
                boltCycleLayer == null ||
                boltCycleLayer.avatarMask == null ||
                boltCycleLayer.defaultWeight != 0f ||
                boltCycleLayer.blendingMode != AnimatorLayerBlendingMode.Additive ||
                weaponLayer == null ||
                weaponLayer.avatarMask == null ||
                weaponLayer.defaultWeight != 0f ||
                weaponLayer.blendingMode != AnimatorLayerBlendingMode.Override ||
                forwardPoseLayer.avatarMask != boltCycleLayer.avatarMask ||
                forwardPoseLayer.avatarMask != weaponLayer.avatarMask
            )
            {
                throw new InvalidOperationException(
                    "PowerSuitAnimator must layer Base, Forward Weapon Pose, " +
                    "additive Bolt Cycle Action, then override Weapon Actions."
                );
            }

            AnimatorState forwardPoseState = forwardPoseLayer.stateMachine.states
                .Select(child => child.state)
                .SingleOrDefault(state => state.name == "Forward Weapon Pose");
            AnimationClip forwardPoseClip = forwardPoseState?.motion as AnimationClip;
            if (
                forwardPoseState == null ||
                forwardPoseClip == null ||
                forwardPoseState.writeDefaultValues ||
                AnimationUtility.GetCurveBindings(forwardPoseClip)
                    .Any(binding => !IsLayerSafeActionBindingPath(binding.path)) ||
                AnimationUtility.GetObjectReferenceCurveBindings(forwardPoseClip)
                    .Any(binding => !IsLayerSafeActionBindingPath(binding.path))
            )
            {
                throw new InvalidOperationException(
                    "Forward weapon pose must use a layer-safe upper-body clip " +
                    "without Animator-root or lower-body bindings."
                );
            }

            string[] requiredLayerSafeStates =
            {
                "Draw Weapon",
                "Sheathe Weapon",
                "Reload"
            };
            foreach (string stateName in requiredLayerSafeStates)
            {
                AnimatorState state = weaponLayer.stateMachine.states
                    .Select(child => child.state)
                    .SingleOrDefault(candidate => candidate.name == stateName);
                AnimationClip actionClip = state?.motion as AnimationClip;
                if (
                    state == null ||
                    actionClip == null ||
                    state.writeDefaultValues ||
                    AnimationUtility.GetCurveBindings(actionClip)
                        .Any(
                            binding => !IsLayerSafeActionBindingPath(binding.path)
                        ) ||
                    AnimationUtility.GetObjectReferenceCurveBindings(actionClip)
                        .Any(
                            binding => !IsLayerSafeActionBindingPath(binding.path)
                        )
                )
                {
                    throw new InvalidOperationException(
                        $"Weapon action '{stateName}' must use a layer-safe clip " +
                        "without Animator-root bindings or Write Defaults."
                    );
                }
            }

            if (
                weaponLayer.stateMachine.states.Any(
                    child => child.state.name == "Bolt Cycle"
                ) ||
                weaponLayer.stateMachine.anyStateTransitions.Any(
                    transition => transition.conditions.Any(
                        condition => condition.parameter == "CycleWeapon"
                    )
                )
            )
            {
                throw new InvalidOperationException(
                    "CycleWeapon must not enter the diagonal override action layer."
                );
            }

            AnimatorState boltCycleState = boltCycleLayer.stateMachine.states
                .Select(child => child.state)
                .SingleOrDefault(state => state.name == "Bolt Cycle");
            AnimationClip boltCycleClip = boltCycleState?.motion as AnimationClip;
            AnimationClipSettings boltClipSettings = boltCycleClip != null
                ? AnimationUtility.GetAnimationClipSettings(boltCycleClip)
                : null;
            if (
                boltCycleState == null ||
                boltCycleClip == null ||
                boltCycleState.writeDefaultValues ||
                boltClipSettings == null ||
                !boltClipSettings.hasAdditiveReferencePose ||
                boltClipSettings.additiveReferencePoseClip == null ||
                boltClipSettings.additiveReferencePoseClip.name != "PS_BoltCycle" ||
                Mathf.Abs(boltClipSettings.additiveReferencePoseTime) > 0.0001f ||
                AnimationUtility.GetCurveBindings(boltCycleClip)
                    .Any(binding => !IsLayerSafeActionBindingPath(binding.path))
            )
            {
                throw new InvalidOperationException(
                    "Bolt Cycle must be a layer-safe additive action referenced " +
                    "against its authored frame-zero pose."
                );
            }

            AvatarMask weaponMask = weaponLayer.avatarMask;
            string[] requiredActiveMaskLeaves =
            {
                "UpperArm.L",
                "LowerArm.L",
                "Hand.L",
                "UpperArm.R",
                "LowerArm.R",
                "Hand.R",
                "WeaponRoot",
                "WeaponMagazine",
                "WeaponBolt"
            };
            string[] requiredInactiveMaskLeaves =
            {
                "Hips",
                "Pelvis",
                "UpperLeg.L",
                "UpperLeg.R"
            };
            if (
                requiredActiveMaskLeaves.Any(
                    leaf => !MaskContainsLeaf(weaponMask, leaf, true)
                ) ||
                requiredInactiveMaskLeaves.Any(
                    leaf => !MaskContainsLeaf(weaponMask, leaf, false)
                )
            )
            {
                throw new InvalidOperationException(
                    "PowerSuit upper-body mask does not preserve the required " +
                    "weapon/arm controls and lower-body locomotion split."
                );
            }

            HashSet<string> parameterNames = controller.parameters
                .Select(parameter => parameter.name)
                .ToHashSet();
            string[] requiredParameters =
            {
                "MovementX",
                "MovementY",
                "MovementSpeed",
                "LocomotionPlaybackSpeed",
                "IsBackpedaling",
                "IsAimWalking",
                "WeaponStowed",
                "DrawWeapon",
                "SheatheWeapon",
                "ReloadWeapon",
                "CycleWeapon"
            };
            if (!requiredParameters.All(parameterNames.Contains))
            {
                throw new InvalidOperationException(
                    "PowerSuitAnimator is missing combat-animation parameters."
                );
            }

            GameObject variant = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerVariantPath);
            PowerSuitController suitController =
                variant != null ? variant.GetComponent<PowerSuitController>() : null;
            if (variant == null || suitController == null)
            {
                throw new InvalidOperationException("Generator 109 player variant is invalid.");
            }

            SerializedObject controllerSettings = new SerializedObject(suitController);
            SerializedProperty normalDistance =
                controllerSettings.FindProperty("cameraDistance");
            SerializedProperty normalHeight =
                controllerSettings.FindProperty("cameraHeight");
            SerializedProperty normalFov =
                controllerSettings.FindProperty("defaultFieldOfView");
            SerializedProperty flightDistance =
                controllerSettings.FindProperty("flightCameraDistance");
            SerializedProperty flightHeight =
                controllerSettings.FindProperty("flightCameraHeight");
            SerializedProperty flightFov =
                controllerSettings.FindProperty("flightFieldOfView");
            SerializedProperty boostDistance =
                controllerSettings.FindProperty("boostCameraDistance");
            SerializedProperty boostHeight =
                controllerSettings.FindProperty("boostCameraHeight");
            SerializedProperty boostFov =
                controllerSettings.FindProperty("boostFieldOfView");
            SerializedProperty aimDistance =
                controllerSettings.FindProperty("aimCameraDistance");
            SerializedProperty aimShoulder =
                controllerSettings.FindProperty("aimShoulderOffset");
            SerializedProperty aimFov =
                controllerSettings.FindProperty("aimFieldOfView");
            SerializedProperty scopeEyeRelief =
                controllerSettings.FindProperty("scopeEyeRelief");
            SerializedProperty scopedNearClip =
                controllerSettings.FindProperty("scopedNearClipPlane");
            if (
                normalDistance == null ||
                normalHeight == null ||
                normalFov == null ||
                flightDistance == null ||
                flightHeight == null ||
                flightFov == null ||
                boostDistance == null ||
                boostHeight == null ||
                boostFov == null ||
                aimDistance == null ||
                aimShoulder == null ||
                aimFov == null ||
                scopeEyeRelief == null ||
                scopedNearClip == null ||
                normalDistance.floatValue < 9.4f ||
                normalHeight.floatValue < 1.45f ||
                normalFov.floatValue < 71f ||
                flightDistance.floatValue < 10.9f ||
                flightDistance.floatValue <= normalDistance.floatValue ||
                flightHeight.floatValue < normalHeight.floatValue ||
                flightFov.floatValue < 73f ||
                flightFov.floatValue < normalFov.floatValue ||
                boostDistance.floatValue < flightDistance.floatValue ||
                boostHeight.floatValue < flightHeight.floatValue ||
                boostFov.floatValue <= flightFov.floatValue ||
                aimDistance.floatValue < 4.2f ||
                aimDistance.floatValue >= normalDistance.floatValue ||
                aimShoulder.vector3Value.x > -1.1f ||
                Mathf.Abs(aimShoulder.vector3Value.y) > 0.1f ||
                aimFov.floatValue < 61f ||
                aimFov.floatValue >= normalFov.floatValue ||
                scopeEyeRelief.floatValue < 0.02f ||
                scopeEyeRelief.floatValue > 0.1f ||
                scopedNearClip.floatValue > 0.05f
            )
            {
                throw new InvalidOperationException(
                    "Normal camera must retain evaluation room, while aim stays " +
                    "on the rifle's local -X shoulder with weapon-readable framing."
                );
            }

            PowerSuitFramePacing framePacing =
                variant.GetComponent<PowerSuitFramePacing>();
            if (
                framePacing == null ||
                !framePacing.RunInBackground ||
                !framePacing.SynchronizeToDisplay ||
                framePacing.FallbackTargetFrameRate != 60
            )
            {
                throw new InvalidOperationException(
                    "Generator 109 demo player is missing the 60 FPS/display-sync policy."
                );
            }

            Animator[] animators = variant.GetComponentsInChildren<Animator>(true);
            if (animators.Length != 1 || animators[0].runtimeAnimatorController != controller)
            {
                throw new InvalidOperationException(
                    "Generator 109 player variant must contain one explicitly configured Animator."
                );
            }

            PowerSuitWeapon weapon = variant.GetComponent<PowerSuitWeapon>();
            WeaponDefinition precisionRifle =
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(
                    PrecisionRifleDefinitionPath
                );
            if (
                weapon == null ||
                precisionRifle == null ||
                weapon.Definition != precisionRifle ||
                weapon.MuzzleTransform == null ||
                weapon.MuzzleTransform.name != "WeaponMuzzle" ||
                weapon.MuzzleTransform.parent == null ||
                weapon.MuzzleTransform.parent.name != "Rifle_Muzzle" ||
                weapon.MuzzleTransform.localPosition.sqrMagnitude > 0.000001f ||
                Quaternion.Angle(
                    weapon.MuzzleTransform.localRotation,
                    MuzzleAdapterRotation
                ) > 0.1f
            )
            {
                throw new InvalidOperationException(
                    "Generator 109 WeaponMuzzle adapter is not wired to Rifle_Muzzle."
                );
            }

            Transform scopePoint = suitController.ScopePoint;
            if (
                scopePoint == null ||
                scopePoint.name != "WeaponScopePoint" ||
                scopePoint.parent == null ||
                scopePoint.parent.name != "Rifle_SightOcular" ||
                scopePoint.localPosition.sqrMagnitude > 0.000001f ||
                Quaternion.Angle(
                    scopePoint.localRotation,
                    MuzzleAdapterRotation
                ) > 0.1f ||
                variant.GetComponent<PowerSuitInputRouter>() == null ||
                !precisionRifle.SupportsScope ||
                precisionRifle.ScopedFieldOfViewDegrees >=
                    precisionRifle.ShoulderFieldOfViewDegrees ||
                Mathf.Abs(
                    precisionRifle.ShoulderLookSensitivityMultiplier - 0.9f
                ) > 0.001f ||
                Mathf.Abs(
                    precisionRifle.ScopedLookSensitivityMultiplier - 0.45f
                ) > 0.001f ||
                Mathf.Abs(precisionRifle.AimTransitionSharpness - 22f) > 0.001f
            )
            {
                throw new InvalidOperationException(
                    "Generator 109 precision-scope anchor, input router, or " +
                    "weapon aim profile is invalid."
                );
            }

            PowerSuitAbilityController abilityController =
                variant.GetComponent<PowerSuitAbilityController>();
            ShoulderRocketAbility rocketAbility =
                variant.GetComponent<ShoulderRocketAbility>();
            LightningStrikeAbility lightningAbility =
                variant.GetComponent<LightningStrikeAbility>();
            VoidUltimateAbility ultimateAbility =
                variant.GetComponent<VoidUltimateAbility>();
            if (
                abilityController == null ||
                rocketAbility == null ||
                lightningAbility == null ||
                ultimateAbility == null ||
                abilityController.ShoulderMuzzle == null ||
                abilityController.ShoulderMuzzle.name != "ShoulderMuzzle" ||
                abilityController.TargetIndicator == null ||
                abilityController.RocketProjectilePrefab == null ||
                abilityController.LightningActorPrefab == null ||
                abilityController.VoidFieldPrefab == null ||
                abilityController.TargetIndicator.GetComponent<
                    AbilityAreaEffectPresentation
                >() == null ||
                abilityController.RocketProjectilePrefab.GetComponent<
                    AbilityAreaEffectPresentation
                >() == null ||
                abilityController.LightningActorPrefab.GetComponent<
                    AbilityAreaEffectPresentation
                >() == null ||
                rocketAbility.LaunchPoint != abilityController.ShoulderMuzzle
            )
            {
                throw new InvalidOperationException(
                    "Generator 109 player is missing its configured rocket, " +
                    "lightning, void, or ability-presentation references."
                );
            }

            PowerSuitHudPresenter hud =
                variant.GetComponentInChildren<PowerSuitHudPresenter>(true);
            PowerSuitHudSafeArea hudSafeArea =
                variant.GetComponentInChildren<PowerSuitHudSafeArea>(true);
            PlayerHealth playerHealth = variant.GetComponent<PlayerHealth>();
            PowerSuitDemoBootstrap demoBootstrap =
                variant.GetComponent<PowerSuitDemoBootstrap>();
            GameObject expectedDemoWorld =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PowerSuitDemoEnemyContentGenerator
                        .CombatSandboxPrefabPath
                );
            DeveloperConsoleOverlay console =
                variant.GetComponent<DeveloperConsoleOverlay>();
            DeveloperConsoleGameplayCommandPack commandPack =
                variant.GetComponent<DeveloperConsoleGameplayCommandPack>();
            if (
                hud == null ||
                hudSafeArea == null ||
                hudSafeArea.transform.parent != hud.transform ||
                playerHealth == null ||
                hud.HealthSource != playerHealth ||
                hud.WeaponSource != weapon ||
                hud.ShoulderRocketSource != rocketAbility ||
                hud.LightningSource != lightningAbility ||
                hud.UltimateSource != ultimateAbility ||
                weapon.ShowLegacyAmmoHud ||
                playerHealth.ShowLegacyHealthHud ||
                demoBootstrap == null ||
                expectedDemoWorld == null ||
                demoBootstrap.DemoWorldPrefab != expectedDemoWorld ||
                demoBootstrap.OwningPlayer != variant.transform ||
                demoBootstrap.HudPresenter != hud ||
                console == null ||
                commandPack == null ||
                commandPack.ConsoleOverlay != console ||
                commandPack.AbilityController != abilityController
            )
            {
                throw new InvalidOperationException(
                    "Generator 109 HUD or developer-console command pack is " +
                    "missing required gameplay bindings."
                );
            }

            if (
                variant.GetComponent<PowerSuitWeaponPresentation>() == null ||
                variant.GetComponent<PowerSuitWeaponAnimationDriver>() == null
            )
            {
                throw new InvalidOperationException(
                    "Generator 109 player variant is missing weapon presentation drivers."
                );
            }

            Transform visual = variant.transform.Find("PowerSuitVisual_Generator109");
            if (
                visual == null ||
                Quaternion.Angle(visual.localRotation, ModelFacingCorrection) > 0.1f
            )
            {
                throw new InvalidOperationException(
                    "Generator 109 visual wrapper must preserve its runtime facing correction."
                );
            }

            PowerSuitVisualFlightResponse visualFlightResponse =
                variant.GetComponent<PowerSuitVisualFlightResponse>();
            if (
                visualFlightResponse == null ||
                visualFlightResponse.VisualRoot != visual
            )
            {
                throw new InvalidOperationException(
                    "Generator 109 player must apply flight attitude only to " +
                    "its dedicated visual wrapper."
                );
            }

            Transform animatedModel = visual.Find("PowerSuitModel_Generator111");
            PowerSuitAnimatorRootLock rootLock =
                animatedModel != null
                    ? animatedModel.GetComponent<PowerSuitAnimatorRootLock>()
                    : null;
            if (
                animatedModel == null ||
                Quaternion.Angle(animatedModel.localRotation, Quaternion.identity) > 0.1f ||
                rootLock == null ||
                !rootLock.HasLock ||
                rootLock.LockedLocalPosition.sqrMagnitude > 0.000001f ||
                Quaternion.Angle(
                    rootLock.LockedLocalRotation,
                    Quaternion.identity
                ) > 0.1f ||
                Vector3.Distance(rootLock.LockedLocalScale, Vector3.one) > 0.0001f
            )
            {
                throw new InvalidOperationException(
                    "Generator 111 animated model must retain its locked identity " +
                    "transform beneath the facing wrapper."
                );
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) == null)
            {
                throw new InvalidOperationException("Generator 109 demo scene is missing.");
            }

            ValidateDemoSceneContents();
        }

        private static void ValidateDemoSceneContents()
        {
            Scene scene = SceneManager.GetSceneByPath(DemoScenePath);
            bool closeWhenFinished = !scene.IsValid() || !scene.isLoaded;
            if (closeWhenFinished)
            {
                scene = EditorSceneManager.OpenScene(
                    DemoScenePath,
                    OpenSceneMode.Additive
                );
            }

            try
            {
                GameObject player = scene.GetRootGameObjects()
                    .FirstOrDefault(root => root.name == "Generator 109 Player");
                Camera mainCamera = scene.GetRootGameObjects()
                    .Select(root => root.GetComponent<Camera>())
                    .FirstOrDefault(camera => camera != null);
                string playerSourcePath = player != null
                    ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(player)
                    : string.Empty;

                if (
                    player == null ||
                    mainCamera == null ||
                    playerSourcePath != PlayerVariantPath
                )
                {
                    throw new InvalidOperationException(
                        "Generator 109 demo scene must retain its main camera " +
                        "and generated player-variant instance."
                    );
                }
            }
            finally
            {
                if (closeWhenFinished && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private readonly struct AbilityPrefabSet
        {
            public AbilityPrefabSet(
                ShoulderRocketProjectile rocket,
                LightningStrikeActor lightning,
                VoidOrbFieldActor voidField,
                AbilityTargetIndicator targetIndicator
            )
            {
                Rocket = rocket;
                Lightning = lightning;
                VoidField = voidField;
                TargetIndicator = targetIndicator;
            }

            public ShoulderRocketProjectile Rocket { get; }
            public LightningStrikeActor Lightning { get; }
            public VoidOrbFieldActor VoidField { get; }
            public AbilityTargetIndicator TargetIndicator { get; }
        }

        private readonly struct HudWidget
        {
            public HudWidget(GameObject root, Image fill, Text label)
            {
                Root = root;
                Fill = fill;
                Label = label;
            }

            public GameObject Root { get; }
            public Image Fill { get; }
            public Text Label { get; }
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

        private static bool MaskContainsLeaf(
            AvatarMask mask,
            string expectedLeaf,
            bool expectedActive
        )
        {
            for (int index = 0; index < mask.transformCount; index++)
            {
                string path = mask.GetTransformPath(index);
                int separator = path.LastIndexOf('/');
                string leaf = separator >= 0
                    ? path.Substring(separator + 1)
                    : path;
                if (
                    leaf == expectedLeaf &&
                    mask.GetTransformActive(index) == expectedActive
                )
                {
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
