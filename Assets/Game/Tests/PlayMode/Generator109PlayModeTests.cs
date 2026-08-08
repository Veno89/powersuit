using System.Collections;
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Powersuit.Tests.PlayMode
{
    public sealed class Generator109PlayModeTests
    {
        [UnityTest]
        public IEnumerator Generator109RuntimeComponents_InitializeWithMuzzleContract()
        {
            GameObject cameraObject = new GameObject("Generator 109 Test Camera");
            GameObject player = new GameObject("Generator 109 Runtime Player");

            try
            {
                cameraObject.tag = "MainCamera";
                cameraObject.AddComponent<Camera>();

                player.AddComponent<CharacterController>();

                Type controllerType = Type.GetType("PowerSuitController, Assembly-CSharp", true);
                Type weaponType = Type.GetType("PowerSuitWeapon, Assembly-CSharp", true);
                Behaviour controller = player.AddComponent(controllerType) as Behaviour;
                Component weapon = player.AddComponent(weaponType);

                Transform muzzle = new GameObject("Rifle_Muzzle").transform;
                muzzle.SetParent(player.transform, false);
                weaponType.GetProperty("MuzzleTransform")?.SetValue(weapon, muzzle);

                yield return null;

                Assert.That(controller, Is.Not.Null);
                Assert.That(controller.enabled, Is.True);
                Assert.That(Camera.main, Is.Not.Null);
                Assert.That(
                    weaponType.GetProperty("MuzzleTransform")?.GetValue(weapon),
                    Is.EqualTo(muzzle)
                );
            }
            finally
            {
                UnityEngine.Object.Destroy(player);
                UnityEngine.Object.Destroy(cameraObject);
            }
        }
    }
}
