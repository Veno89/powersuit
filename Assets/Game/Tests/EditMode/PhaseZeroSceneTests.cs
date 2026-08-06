using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Powersuit.Tests.EditMode
{
    public sealed class PhaseZeroSceneTests
    {
        private const string ScenePath = "Assets/Scenes/FlightPrototype.unity";

        [Test]
        public void FlightPrototype_IsEnabledAsFirstBuildScene()
        {
            Assert.That(EditorBuildSettings.scenes, Is.Not.Empty);
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(ScenePath));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
        }

        [Test]
        public void FlightPrototype_ContainsRequiredGreyboxElements()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool closeWhenFinished = !scene.IsValid() || !scene.isLoaded;
            if (closeWhenFinished)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }

            try
            {
                GameObject greybox = FindRoot(scene, "Phase 0 Greybox");
                Assert.That(greybox, Is.Not.Null);

                string[] requiredGroups =
                {
                    "Ground Areas",
                    "Walls",
                    "Tall Pillars",
                    "Ramps",
                    "Elevated Platforms",
                    "Marked Start Area",
                    "Player Placeholder"
                };

                foreach (string groupName in requiredGroups)
                {
                    Assert.That(greybox.transform.Find(groupName), Is.Not.Null, groupName);
                }

                Transform player = greybox.transform.Find("Player Placeholder");
                Assert.That(player.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(0));
                Assert.That(greybox.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(25));
                Assert.That(FindRoot(scene, "Main Camera")?.GetComponent<Camera>(), Is.Not.Null);
                Assert.That(FindRoot(scene, "Directional Light")?.GetComponent<Light>(), Is.Not.Null);
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
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }

            return null;
        }
    }
}