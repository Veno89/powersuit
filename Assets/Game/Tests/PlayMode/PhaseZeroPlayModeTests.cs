using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Powersuit.Tests.PlayMode
{
    public sealed class PhaseZeroPlayModeTests
    {
        [UnityTest]
        public IEnumerator FlightPrototype_LoadsWithVisiblePlaceholder()
        {
            AsyncOperation loadOperation = SceneManager.LoadSceneAsync("FlightPrototype", LoadSceneMode.Single);
            Assert.That(loadOperation, Is.Not.Null);

            while (!loadOperation.isDone)
            {
                yield return null;
            }

            Scene scene = SceneManager.GetActiveScene();
            GameObject greybox = FindRoot(scene, "Phase 0 Greybox");
            Assert.That(greybox, Is.Not.Null);

            Transform player = greybox.transform.Find("Player Placeholder");
            Assert.That(player, Is.Not.Null);
            Assert.That(player.gameObject.activeInHierarchy, Is.True);
            Assert.That(player.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(0));
            Assert.That(Camera.main, Is.Not.Null);
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