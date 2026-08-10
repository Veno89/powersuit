using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Powersuit.Tests.EditMode
{
    public sealed class Generator109IntegrationTests
    {
        private const string BaseLayerName = "Base Layer";
        private const string ForwardWeaponPoseLayerName = "Forward Weapon Pose";
        private const string BoltCycleActionLayerName = "Bolt Cycle Action";
        private const string WeaponActionsLayerName = "Weapon Actions";

        private const string ModelPath =
            "Assets/Game/Models/PoweredSuit/powersuit_animated_with_aim.fbx";

        private const string ControllerPath =
            "Assets/Game/Animation/PowerSuitAnimator.controller";

        private const string PlayerVariantPath =
            "Assets/Game/Prefab/Player/PlayerPrototype_Generator109.prefab";

        private const string BasePlayerPrefabPath =
            "Assets/Game/Prefab/Player/PlayerPrototype.prefab";

        private const string DemoScenePath =
            "Assets/Scenes/PoweredSuitAimDemo.unity";

        [Test]
        public void Generator109_DemoSceneOwnershipPolicyIsNonDestructive()
        {
            Type integrationType = Type.GetType(
                "Powersuit.Editor.PoweredSuitGenerator109Integration, " +
                "Assembly-CSharp-Editor"
            );
            Assert.That(integrationType, Is.Not.Null);

            System.Reflection.MethodInfo resolveMethod =
                integrationType.GetMethod("ResolveDemoSceneHandling");
            Assert.That(resolveMethod, Is.Not.Null);

            object preserveExisting = resolveMethod.Invoke(
                null,
                new object[] { true }
            );
            object createAndPopulate = resolveMethod.Invoke(
                null,
                new object[] { false }
            );

            Assert.That(
                preserveExisting?.ToString(),
                Is.EqualTo("PreserveExisting")
            );
            Assert.That(
                createAndPopulate?.ToString(),
                Is.EqualTo("CreateAndPopulate")
            );
        }

        [Test]
        public void Generator109_ImporterContainsOnlyRequiredGameplayContent()
        {
            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.optimizeGameObjects, Is.False);

            string[] clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__"))
                .Select(clip => clip.name)
                .OrderBy(name => name)
                .ToArray();

            Assert.That(
                clips,
                Is.EquivalentTo(
                    new[]
                    {
                        "PS_Aim",
                        "PS_Aim_Walk_Backward",
                        "PS_Aim_Walk_Forward",
                        "PS_BoltCycle",
                        "PS_Hover",
                        "PS_Idle",
                        "PS_Reload",
                        "PS_Walk",
                        "PS_Walk_Backward",
                        "PS_Walk_Forward",
                        "PS_WeaponStowed_Idle",
                        "PS_WeaponStowed_Hover",
                        "PS_WeaponStowed_Walk_Backward",
                        "PS_WeaponStowed_Walk_Forward",
                        "PS_WeaponReady_Idle",
                        "PS_Weapon_Draw",
                        "PS_Weapon_Sheathe"
                    }
                )
            );
        }

        [Test]
        public void Generator109_ControllerAndPlayerVariantAreWired()
        {
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null);

            string[] states = controller.layers
                .SelectMany(layer => layer.stateMachine.states)
                .Select(child => child.state.name)
                .ToArray();
            Assert.That(states, Does.Contain("Ready Locomotion"));
            Assert.That(states, Does.Contain("Stowed Locomotion"));
            Assert.That(states, Does.Contain("Aim Locomotion"));
            Assert.That(states, Does.Contain("Stowed Hover"));
            Assert.That(states, Does.Contain("Forward Weapon Pose"));
            Assert.That(states, Does.Contain("Reload"));
            Assert.That(states, Does.Contain("Bolt Cycle"));

            Assert.That(controller.layers, Has.Length.EqualTo(4));
            Assert.That(
                controller.layers.Select(layer => layer.name),
                Is.EqualTo(
                    new[]
                    {
                        BaseLayerName,
                        ForwardWeaponPoseLayerName,
                        BoltCycleActionLayerName,
                        WeaponActionsLayerName
                    }
                )
            );

            AnimatorControllerLayer baseLayer = FindLayer(controller, BaseLayerName);
            AnimatorControllerLayer forwardWeaponPoseLayer = FindLayer(
                controller,
                ForwardWeaponPoseLayerName
            );
            AnimatorControllerLayer boltCycleActionLayer = FindLayer(
                controller,
                BoltCycleActionLayerName
            );
            AnimatorControllerLayer weaponActionsLayer = FindLayer(
                controller,
                WeaponActionsLayerName
            );
            Assert.That(baseLayer, Is.Not.Null);
            Assert.That(forwardWeaponPoseLayer, Is.Not.Null);
            Assert.That(boltCycleActionLayer, Is.Not.Null);
            Assert.That(weaponActionsLayer, Is.Not.Null);

            Assert.That(forwardWeaponPoseLayer.defaultWeight, Is.Zero);
            Assert.That(
                forwardWeaponPoseLayer.blendingMode,
                Is.EqualTo(AnimatorLayerBlendingMode.Override)
            );
            Assert.That(forwardWeaponPoseLayer.avatarMask, Is.Not.Null);
            Assert.That(boltCycleActionLayer.defaultWeight, Is.Zero);
            Assert.That(
                boltCycleActionLayer.blendingMode,
                Is.EqualTo(AnimatorLayerBlendingMode.Additive)
            );
            Assert.That(boltCycleActionLayer.avatarMask, Is.Not.Null);
            Assert.That(weaponActionsLayer.defaultWeight, Is.Zero);
            Assert.That(weaponActionsLayer.avatarMask, Is.Not.Null);
            Assert.That(
                forwardWeaponPoseLayer.avatarMask,
                Is.SameAs(weaponActionsLayer.avatarMask),
                "Forward pose and weapon actions must share the validated upper-body mask."
            );
            Assert.That(
                boltCycleActionLayer.avatarMask,
                Is.SameAs(weaponActionsLayer.avatarMask),
                "Additive bolt cycle must share the validated upper-body mask."
            );

            AnimatorState forwardWeaponPoseState = forwardWeaponPoseLayer.stateMachine.states
                .Select(child => child.state)
                .SingleOrDefault(state => state.name == "Forward Weapon Pose");
            Assert.That(forwardWeaponPoseState, Is.Not.Null);
            Assert.That(forwardWeaponPoseState.writeDefaultValues, Is.False);
            AnimationClip forwardWeaponPoseClip =
                forwardWeaponPoseState.motion as AnimationClip;
            Assert.That(forwardWeaponPoseClip, Is.Not.Null);
            AssertLayerSafeClip(forwardWeaponPoseClip, "Forward Weapon Pose");
            AssertUpperBodyMask(
                forwardWeaponPoseLayer.avatarMask,
                "Forward Weapon Pose"
            );

            foreach (
                string actionStateName in new[]
                {
                    "Draw Weapon",
                    "Sheathe Weapon",
                    "Reload"
                }
            )
            {
                AnimatorState actionState = weaponActionsLayer.stateMachine.states
                    .Select(child => child.state)
                    .SingleOrDefault(state => state.name == actionStateName);
                Assert.That(actionState, Is.Not.Null, actionStateName);
                Assert.That(
                    actionState.writeDefaultValues,
                    Is.False,
                    $"{actionStateName} must not write unrelated transform defaults."
                );

                AnimationClip actionClip = actionState.motion as AnimationClip;
                Assert.That(actionClip, Is.Not.Null, actionStateName);
                AssertLayerSafeClip(actionClip, actionStateName);
            }
            AssertUpperBodyMask(weaponActionsLayer.avatarMask, "Weapon Actions");

            Assert.That(
                weaponActionsLayer.stateMachine.states
                    .Select(child => child.state.name),
                Does.Not.Contain("Bolt Cycle"),
                "The diagonal bolt clip must not remain on the override action layer."
            );
            Assert.That(
                weaponActionsLayer.stateMachine.anyStateTransitions
                    .SelectMany(transition => transition.conditions)
                    .Select(condition => condition.parameter),
                Does.Not.Contain("CycleWeapon")
            );

            AnimatorState boltCycleState = boltCycleActionLayer.stateMachine.states
                .Select(child => child.state)
                .SingleOrDefault(state => state.name == "Bolt Cycle");
            Assert.That(boltCycleState, Is.Not.Null);
            Assert.That(boltCycleState.writeDefaultValues, Is.False);
            AnimationClip boltCycleClip = boltCycleState.motion as AnimationClip;
            Assert.That(boltCycleClip, Is.Not.Null);
            AssertLayerSafeClip(boltCycleClip, "Bolt Cycle");
            AnimationClipSettings boltSettings =
                AnimationUtility.GetAnimationClipSettings(boltCycleClip);
            Assert.That(boltSettings.hasAdditiveReferencePose, Is.True);
            Assert.That(boltSettings.additiveReferencePoseClip, Is.Not.Null);
            Assert.That(
                boltSettings.additiveReferencePoseClip.name,
                Is.EqualTo("PS_BoltCycle")
            );
            Assert.That(
                boltSettings.additiveReferencePoseTime,
                Is.EqualTo(0f).Within(0.0001f)
            );
            AssertUpperBodyMask(
                boltCycleActionLayer.avatarMask,
                "Bolt Cycle Action"
            );
            Assert.That(
                controller.parameters.Select(parameter => parameter.name),
                Is.SupersetOf(
                    new[]
                    {
                        "IsAiming",
                        "MovementY",
                        "LocomotionPlaybackSpeed",
                        "WeaponStowed",
                        "DrawWeapon",
                        "SheatheWeapon",
                        "ReloadWeapon",
                        "CycleWeapon"
                    }
                )
            );

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerVariantPath);
            Assert.That(player, Is.Not.Null);
            Component suitController = player.GetComponent("PowerSuitController");
            Assert.That(suitController, Is.Not.Null);

            SerializedObject controllerSettings = new SerializedObject(suitController);
            Assert.That(
                controllerSettings.FindProperty("walkSpeed").floatValue,
                Is.EqualTo(6.5f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("groundAcceleration").floatValue,
                Is.EqualTo(55f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("flightSpeed").floatValue,
                Is.EqualTo(14f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("boostSpeed").floatValue,
                Is.EqualTo(28f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("flightAcceleration").floatValue,
                Is.EqualTo(38f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("turningSpeed").floatValue,
                Is.EqualTo(20f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("combatTurningSpeed").floatValue,
                Is.EqualTo(32f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("mouseSensitivity").floatValue,
                Is.EqualTo(0.18f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("controllerLookSpeed").floatValue,
                Is.EqualTo(180f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("cameraDistance").floatValue,
                Is.EqualTo(9.5f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("cameraHeight").floatValue,
                Is.EqualTo(1.5f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("defaultFieldOfView").floatValue,
                Is.EqualTo(72f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("flightCameraDistance").floatValue,
                Is.EqualTo(11f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("flightCameraHeight").floatValue,
                Is.EqualTo(1.75f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("flightFieldOfView").floatValue,
                Is.EqualTo(74f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("boostCameraDistance").floatValue,
                Is.EqualTo(12f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("boostCameraHeight").floatValue,
                Is.EqualTo(1.8f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("boostFieldOfView").floatValue,
                Is.EqualTo(82f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("cameraCollisionPadding").floatValue,
                Is.EqualTo(0.05f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("cameraCollisionReleaseSharpness").floatValue,
                Is.EqualTo(14f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("cameraLookSharpness").floatValue,
                Is.EqualTo(45f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("aimCameraDistance").floatValue,
                Is.EqualTo(4.3f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("aimCameraHeight").floatValue,
                Is.EqualTo(1.45f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("aimShoulderOffset").vector3Value,
                Is.EqualTo(new Vector3(-1.2f, 0.05f, 0f))
            );
            Assert.That(
                controllerSettings.FindProperty("aimFieldOfView").floatValue,
                Is.EqualTo(62f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("aimTransitionSpeed").floatValue,
                Is.EqualTo(22f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty(
                    "movementSettings.groundDeceleration"
                ).floatValue,
                Is.EqualTo(65f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty(
                    "movementSettings.groundBrakingAcceleration"
                ).floatValue,
                Is.EqualTo(105f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty(
                    "movementSettings.flightDeceleration"
                ).floatValue,
                Is.EqualTo(30f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty(
                    "movementSettings.flightBrakingAcceleration"
                ).floatValue,
                Is.EqualTo(55f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("scopeEyeRelief").floatValue,
                Is.EqualTo(0.045f).Within(0.001f)
            );
            Assert.That(
                controllerSettings.FindProperty("scopedNearClipPlane").floatValue,
                Is.EqualTo(0.02f).Within(0.001f)
            );
            Assert.That(player.GetComponent("PowerSuitInputRouter"), Is.Not.Null);

            Component responsiveAnimation = player.GetComponent(
                "PowerSuitAnimationDriver"
            );
            Assert.That(responsiveAnimation, Is.Not.Null);
            SerializedObject responsiveAnimationSettings =
                new SerializedObject(responsiveAnimation);
            Assert.That(
                responsiveAnimationSettings.FindProperty(
                    "movementDamping"
                ).floatValue,
                Is.EqualTo(0.06f).Within(0.001f)
            );
            Assert.That(
                responsiveAnimationSettings.FindProperty(
                    "fullSpeedLocomotionPlayback"
                ).floatValue,
                Is.EqualTo(4.5f).Within(0.001f)
            );
            Assert.That(
                responsiveAnimationSettings.FindProperty(
                    "forwardPoseBlendSharpness"
                ).floatValue,
                Is.EqualTo(22f).Within(0.001f)
            );

            GameObject basePlayer =
                AssetDatabase.LoadAssetAtPath<GameObject>(BasePlayerPrefabPath);
            Assert.That(basePlayer, Is.Not.Null);
            Component baseSuitController = basePlayer.GetComponent("PowerSuitController");
            Assert.That(baseSuitController, Is.Not.Null);
            SerializedObject baseControllerSettings =
                new SerializedObject(baseSuitController);
            Assert.That(
                baseControllerSettings.FindProperty("walkSpeed").floatValue,
                Is.EqualTo(5f).Within(0.001f),
                "Camera integration must preserve the legacy base prefab movement tune."
            );

            Component framePacing = player.GetComponent("PowerSuitFramePacing");
            Assert.That(framePacing, Is.Not.Null);
            Assert.That(
                framePacing.GetType().GetProperty("RunInBackground")?.GetValue(framePacing),
                Is.EqualTo(true)
            );
            Assert.That(
                framePacing.GetType().GetProperty("SynchronizeToDisplay")?.GetValue(framePacing),
                Is.EqualTo(true)
            );
            Assert.That(
                framePacing.GetType().GetProperty("FallbackTargetFrameRate")?.GetValue(framePacing),
                Is.EqualTo(60)
            );

            Animator[] animators = player.GetComponentsInChildren<Animator>(true);
            Assert.That(animators, Has.Length.EqualTo(1));
            Assert.That(animators[0].runtimeAnimatorController, Is.EqualTo(controller));

            Transform visual = player.transform.Find("PowerSuitVisual_Generator109");
            Assert.That(visual, Is.Not.Null);
            Component visualResponse =
                player.GetComponent("PowerSuitVisualFlightResponse");
            Assert.That(visualResponse, Is.Not.Null);
            Assert.That(
                visualResponse.GetType().GetProperty("VisualRoot")
                    ?.GetValue(visualResponse),
                Is.EqualTo(visual),
                "Flight attitude must affect only the dedicated visual wrapper."
            );
            Assert.That(
                Quaternion.Angle(
                    visual.localRotation,
                    Quaternion.AngleAxis(90f, Vector3.right) *
                        Quaternion.Euler(0f, 180f, 0f)
                ),
                Is.LessThan(0.1f),
                "The non-animated wrapper must correct the FBX up/front axes."
            );

            Transform animatedModel = visual.Find("PowerSuitModel_Generator111");
            Assert.That(animatedModel, Is.Not.Null);
            Assert.That(
                Quaternion.Angle(animatedModel.localRotation, Quaternion.identity),
                Is.LessThan(0.1f),
                "The Animator root must remain unrotated beneath the facing wrapper."
            );
            Component rootLock = animatedModel.GetComponent("PowerSuitAnimatorRootLock");
            Assert.That(rootLock, Is.Not.Null);
            Assert.That(
                rootLock.GetType().GetProperty("HasLock")?.GetValue(rootLock),
                Is.EqualTo(true)
            );
            Assert.That(
                rootLock.GetType().GetProperty("LockedLocalPosition")
                    ?.GetValue(rootLock),
                Is.EqualTo(Vector3.zero)
            );
            Quaternion lockedRotation = (Quaternion)rootLock.GetType()
                .GetProperty("LockedLocalRotation")
                .GetValue(rootLock);
            Assert.That(
                Quaternion.Angle(lockedRotation, Quaternion.identity),
                Is.LessThan(0.1f)
            );
            Assert.That(
                rootLock.GetType().GetProperty("LockedLocalScale")
                    ?.GetValue(rootLock),
                Is.EqualTo(Vector3.one)
            );

            Component weapon = player.GetComponent("PowerSuitWeapon");
            Assert.That(weapon, Is.Not.Null);
            object definition = weapon.GetType()
                .GetProperty("Definition")
                ?.GetValue(weapon);
            Assert.That(definition, Is.Not.Null);
            Assert.That(
                definition.GetType().GetProperty("WeaponClass")?.GetValue(definition)?.ToString(),
                Is.EqualTo("PrecisionRifle")
            );
            Assert.That(
                definition.GetType().GetProperty("RequiresManualCycle")?.GetValue(definition),
                Is.EqualTo(true)
            );
            Assert.That(
                definition.GetType().GetProperty("MagazineCapacity")?.GetValue(definition),
                Is.EqualTo(5)
            );
            Assert.That(
                definition.GetType().GetProperty("SupportsScope")?.GetValue(definition),
                Is.EqualTo(true)
            );
            Assert.That(
                definition.GetType().GetProperty("ScopedFieldOfViewDegrees")
                    ?.GetValue(definition),
                Is.EqualTo(28f).Within(0.001f)
            );
            Assert.That(player.GetComponent("PowerSuitWeaponPresentation"), Is.Not.Null);
            Assert.That(player.GetComponent("PowerSuitWeaponAnimationDriver"), Is.Not.Null);
            Transform muzzle = weapon.GetType()
                .GetProperty("MuzzleTransform")
                ?.GetValue(weapon) as Transform;
            Assert.That(muzzle, Is.Not.Null);
            Assert.That(muzzle.name, Is.EqualTo("WeaponMuzzle"));
            Assert.That(muzzle.parent, Is.Not.Null);
            Assert.That(muzzle.parent.name, Is.EqualTo("Rifle_Muzzle"));
            Assert.That(muzzle.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(
                Quaternion.Angle(muzzle.localRotation, Quaternion.Euler(-90f, 0f, 0f)),
                Is.LessThan(0.1f),
                "The muzzle adapter must map the imported +Y bore to Unity forward."
            );

            Transform scopePoint = suitController.GetType()
                .GetProperty("ScopePoint")
                ?.GetValue(suitController) as Transform;
            Assert.That(scopePoint, Is.Not.Null);
            Assert.That(scopePoint.name, Is.EqualTo("WeaponScopePoint"));
            Assert.That(scopePoint.parent, Is.Not.Null);
            Assert.That(scopePoint.parent.name, Is.EqualTo("Rifle_SightOcular"));
            Assert.That(scopePoint.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(
                Quaternion.Angle(
                    scopePoint.localRotation,
                    Quaternion.Euler(-90f, 0f, 0f)
                ),
                Is.LessThan(0.1f),
                "The scope adapter must map the imported +Y optic axis to Unity forward."
            );
        }

        [Test]
        public void Generator109_DemoRetainsCanonicalPlayerVariantAndSafeSpawns()
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
                GameObject player = FindRoot(scene, "Generator 109 Player");
                GameObject enemies = FindRoot(scene, "Test Enemies");

                Assert.That(player, Is.Not.Null);
                Assert.That(FindRoot(scene, "Main Camera")?.GetComponent<Camera>(), Is.Not.Null);

                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(player),
                    Is.EqualTo(PlayerVariantPath)
                );

                if (enemies == null)
                {
                    return;
                }

                foreach (Transform enemy in enemies.transform)
                {
                    Assert.That(
                        Vector3.Distance(player.transform.position, enemy.position),
                        Is.GreaterThan(10f),
                        enemy.name
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

        private static GameObject FindRoot(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(root => root.name == name);
        }

        private static AnimatorControllerLayer FindLayer(
            AnimatorController controller,
            string layerName
        )
        {
            return controller.layers.SingleOrDefault(layer => layer.name == layerName);
        }

        private static void AssertLayerSafeClip(AnimationClip clip, string context)
        {
            Assert.That(
                AnimationUtility.GetCurveBindings(clip)
                    .Any(binding => !IsLayerSafeWeaponActionPath(binding.path)),
                Is.False,
                $"{context} must contain only upper-body/weapon bindings."
            );
            Assert.That(
                AnimationUtility.GetObjectReferenceCurveBindings(clip)
                    .Any(binding => !IsLayerSafeWeaponActionPath(binding.path)),
                Is.False,
                $"{context} must contain only upper-body/weapon bindings."
            );
        }

        private static void AssertUpperBodyMask(AvatarMask mask, string context)
        {
            foreach (
                string requiredActiveLeaf in new[]
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
                }
            )
            {
                Assert.That(
                    MaskContainsLeaf(mask, requiredActiveLeaf, true),
                    Is.True,
                    $"{context} mask must include {requiredActiveLeaf}."
                );
            }

            foreach (
                string requiredInactiveLeaf in new[]
                {
                    "Hips",
                    "Pelvis",
                    "UpperLeg.L",
                    "UpperLeg.R"
                }
            )
            {
                Assert.That(
                    MaskContainsLeaf(mask, requiredInactiveLeaf, false),
                    Is.True,
                    $"{context} mask must exclude {requiredInactiveLeaf}."
                );
            }
        }

        private static bool MaskContainsLeaf(
            AvatarMask mask,
            string leafName,
            bool expectedActive
        )
        {
            for (int index = 0; index < mask.transformCount; index++)
            {
                string path = mask.GetTransformPath(index);
                string leaf = path.Substring(path.LastIndexOf('/') + 1);
                if (
                    leaf == leafName &&
                    mask.GetTransformActive(index) == expectedActive
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLayerSafeWeaponActionPath(string path)
        {
            return path == "Root/Hips/Spine" ||
                path.StartsWith("Root/Hips/Spine/") ||
                path == "WeaponRoot" ||
                path == "WeaponRoot/WeaponMagazine" ||
                path.StartsWith("WeaponRoot/WeaponMagazine/") ||
                path == "WeaponRoot/WeaponBolt" ||
                path.StartsWith("WeaponRoot/WeaponBolt/");
        }
    }
}
