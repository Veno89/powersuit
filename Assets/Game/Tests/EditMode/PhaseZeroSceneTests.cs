using System;
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
        public void PhaseZeroBuilder_IsExplicitOnly()
        {
            Type builderType = GetEditorType(
                "Powersuit.Editor.PhaseZeroSceneBuilder"
            );

            Assert.That(
                Attribute.IsDefined(
                    builderType,
                    typeof(InitializeOnLoadAttribute)
                ),
                Is.False,
                "Phase 0 setup must not run automatically when editor assemblies load."
            );
        }

        [Test]
        public void PhaseZeroDevelopmentBuildOptions_DoNotMutateBuildProfile()
        {
            string[] before = CaptureBuildProfile();
            Type builderType = GetEditorType(
                "Powersuit.Editor.PhaseZeroSceneBuilder"
            );

            object result = builderType
                .GetMethod("CreateDevelopmentBuildOptions")
                ?.Invoke(null, new object[] { "Build/Windows/Powersuit.exe" });
            Assert.That(result, Is.TypeOf<BuildPlayerOptions>());
            BuildPlayerOptions options = (BuildPlayerOptions)result;

            Assert.That(options.scenes, Is.EqualTo(new[] { ScenePath }));
            Assert.That(options.target, Is.EqualTo(BuildTarget.StandaloneWindows64));
            Assert.That(options.options, Is.EqualTo(BuildOptions.Development));
            Assert.That(CaptureBuildProfile(), Is.EqualTo(before));
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

        private static string[] CaptureBuildProfile()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            string[] snapshot = new string[scenes.Length];
            for (int index = 0; index < scenes.Length; index++)
            {
                snapshot[index] = $"{scenes[index].path}|{scenes[index].enabled}";
            }

            return snapshot;
        }

        private static Type GetEditorType(string fullName)
        {
            Type type = Type.GetType($"{fullName}, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, fullName);
            return type;
        }
    }
}
