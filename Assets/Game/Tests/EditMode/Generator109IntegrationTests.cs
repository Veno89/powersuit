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
        private const string ModelPath =
            "Assets/Game/Models/PoweredSuit/powersuit_animated_with_aim.fbx";

        private const string ControllerPath =
            "Assets/Game/Animation/PowerSuitAnimator.controller";

        private const string PlayerVariantPath =
            "Assets/Game/Prefab/Player/PlayerPrototype_Generator109.prefab";

        private const string DemoScenePath =
            "Assets/Scenes/PoweredSuitAimDemo.unity";

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
            Assert.That(states, Does.Contain("Reload"));
            Assert.That(states, Does.Contain("Bolt Cycle"));
            Assert.That(controller.layers, Has.Length.EqualTo(2));
            Assert.That(controller.layers[1].name, Is.EqualTo("Weapon Actions"));
            Assert.That(controller.layers[1].avatarMask, Is.Not.Null);
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
            Assert.That(player.GetComponent("PowerSuitController"), Is.Not.Null);

            Animator[] animators = player.GetComponentsInChildren<Animator>(true);
            Assert.That(animators, Has.Length.EqualTo(1));
            Assert.That(animators[0].runtimeAnimatorController, Is.EqualTo(controller));

            Transform visual = player.transform.Find("PowerSuitVisual_Generator109");
            Assert.That(visual, Is.Not.Null);
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
        }

        [Test]
        public void Generator109_DemoHasSafeSpawnsAndRequiredObjects()
        {
            Scene scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject player = FindRoot(scene, "Generator 109 Player");
                GameObject enemies = FindRoot(scene, "Test Enemies");

                Assert.That(player, Is.Not.Null);
                Assert.That(enemies, Is.Not.Null);
                Assert.That(enemies.transform.childCount, Is.EqualTo(3));
                Assert.That(FindRoot(scene, "Main Camera")?.GetComponent<Camera>(), Is.Not.Null);

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
                if (scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(root => root.name == name);
        }
    }
}
