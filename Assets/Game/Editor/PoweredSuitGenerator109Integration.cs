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
using Powersuit.Combat;

namespace Powersuit.Editor
{
    public static class PoweredSuitGenerator109Integration
    {
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

        private static readonly string[] WeaponActionClips =
        {
            "PS_Weapon_Draw",
            "PS_Weapon_Sheathe",
            "PS_Reload",
            "PS_BoltCycle"
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
            ConfigurePrecisionRifleDefinition();
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

        private static void ConfigurePrecisionRifleDefinition()
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
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
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
                WeaponActionClips.ToDictionary(
                    name => name,
                    name => CreateOrUpdateLayerSafeActionClip(clips[name]),
                    StringComparer.Ordinal
                );

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
            AnimatorStateMachine weaponStateMachine = new AnimatorStateMachine
            {
                name = "Weapon Action State Machine",
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(weaponStateMachine, controller);
            AnimatorControllerLayer weaponLayer = new AnimatorControllerLayer
            {
                name = "Weapon Actions",
                defaultWeight = 1f,
                avatarMask = upperBodyMask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = weaponStateMachine
            };
            controller.AddLayer(weaponLayer);

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
            AnimatorState cycle = AddState(
                weaponStateMachine,
                "Bolt Cycle",
                layerSafeActionClips["PS_BoltCycle"],
                new Vector3(360f, 320f)
            );
            foreach (AnimatorState actionState in new[] { draw, sheathe, reload, cycle })
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
            AddAnyStateTrigger(weaponStateMachine, cycle, "CycleWeapon");
            AddExitTransition(draw, empty);
            AddExitTransition(sheathe, empty);
            AddExitTransition(reload, empty);
            AddExitTransition(cycle, empty);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
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
                if (muzzle == null || rifleRoot == null)
                {
                    throw new InvalidOperationException(
                        "The imported Generator 111 hierarchy does not expose RifleRoot/Rifle_Muzzle."
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

                SerializedObject driverObject = new SerializedObject(animationDriver);
                driverObject.FindProperty("controller").objectReferenceValue = suitController;
                driverObject.FindProperty("animator").objectReferenceValue = animator;
                driverObject.FindProperty("weaponPresentation").objectReferenceValue = presentation;
                driverObject.ApplyModifiedPropertiesWithoutUndo();

                SerializedObject presentationObject = new SerializedObject(presentation);
                presentationObject.FindProperty("controller").objectReferenceValue = suitController;
                presentationObject.FindProperty("animator").objectReferenceValue = animator;
                presentationObject.FindProperty("weapon").objectReferenceValue = weapon;
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

        private static Transform CreateMuzzleAdapter(Transform importedMuzzle)
        {
            GameObject adapter = new GameObject("WeaponMuzzle");
            adapter.transform.SetParent(importedMuzzle, false);
            adapter.transform.localPosition = Vector3.zero;
            adapter.transform.localRotation = MuzzleAdapterRotation;
            adapter.transform.localScale = Vector3.one;
            return adapter.transform;
        }

        private static void ConfigureAimCamera(PowerSuitController controller)
        {
            SerializedObject serialized = new SerializedObject(controller);
            SetFloat(serialized, "walkSpeed", 2.2f);
            SetFloat(serialized, "cameraDistance", 6f);
            SetFloat(serialized, "cameraHeight", 1.55f);
            SetFloat(serialized, "defaultFieldOfView", 65f);
            SetFloat(serialized, "cameraCollisionPadding", 0.05f);
            SetFloat(serialized, "cameraCollisionReleaseSharpness", 14f);
            SetFloat(serialized, "cameraLookSharpness", 28f);
            SetFloat(serialized, "aimCameraDistance", 3.4f);
            SetFloat(serialized, "aimCameraHeight", 1.5f);
            // The shouldered rifle sits on player-local -X. Keeping the camera
            // on +X put the suit between the lens and weapon, so use the firing
            // side and lift slightly to expose the receiver and barrel.
            SetVector(serialized, "aimShoulderOffset", new Vector3(-1.6f, 0.3f, 0f));
            SetFloat(serialized, "aimFieldOfView", 58f);
            SetFloat(serialized, "aimTransitionSpeed", 12f);
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

            if (
                controller.layers.Length != 2 ||
                controller.layers[1].avatarMask == null ||
                controller.layers[1].name != "Weapon Actions"
            )
            {
                throw new InvalidOperationException(
                    "PowerSuitAnimator weapon actions must use the masked upper-body layer."
                );
            }

            string[] requiredLayerSafeStates =
            {
                "Draw Weapon",
                "Sheathe Weapon",
                "Reload",
                "Bolt Cycle"
            };
            foreach (string stateName in requiredLayerSafeStates)
            {
                AnimatorState state = controller.layers[1].stateMachine.states
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

            AvatarMask weaponMask = controller.layers[1].avatarMask;
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
            SerializedProperty aimDistance =
                controllerSettings.FindProperty("aimCameraDistance");
            SerializedProperty aimShoulder =
                controllerSettings.FindProperty("aimShoulderOffset");
            SerializedProperty aimFov =
                controllerSettings.FindProperty("aimFieldOfView");
            if (
                normalDistance == null ||
                normalHeight == null ||
                normalFov == null ||
                aimDistance == null ||
                aimShoulder == null ||
                aimFov == null ||
                normalDistance.floatValue < 5.9f ||
                normalHeight.floatValue < 1.5f ||
                normalFov.floatValue < 64f ||
                aimDistance.floatValue < 3.3f ||
                aimDistance.floatValue >= normalDistance.floatValue ||
                aimShoulder.vector3Value.x > -1.4f ||
                aimShoulder.vector3Value.y < 0.2f ||
                aimFov.floatValue < 55f ||
                aimFov.floatValue >= normalFov.floatValue
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
