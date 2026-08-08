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
                Is.EquivalentTo(new[] { "PS_Aim", "PS_Hover", "PS_Idle", "PS_Walk" })
            );
        }

        [Test]
        public void Generator109_ControllerAndPlayerVariantAreWired()
        {
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null);

            string[] states = controller.layers[0].stateMachine.states
                .Select(child => child.state.name)
                .ToArray();
            Assert.That(states, Does.Contain("Aim"));
            Assert.That(
                controller.parameters.Select(parameter => parameter.name),
                Does.Contain("IsAiming")
            );

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerVariantPath);
            Assert.That(player, Is.Not.Null);
            Assert.That(player.GetComponent("PowerSuitController"), Is.Not.Null);

            Animator[] animators = player.GetComponentsInChildren<Animator>(true);
            Assert.That(animators, Has.Length.EqualTo(1));
            Assert.That(animators[0].runtimeAnimatorController, Is.EqualTo(controller));

            Component weapon = player.GetComponent("PowerSuitWeapon");
            Assert.That(weapon, Is.Not.Null);
            Transform muzzle = weapon.GetType()
                .GetProperty("MuzzleTransform")
                ?.GetValue(weapon) as Transform;
            Assert.That(muzzle, Is.Not.Null);
            Assert.That(muzzle.name, Is.EqualTo("Rifle_Muzzle"));
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
