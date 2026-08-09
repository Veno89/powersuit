using System.Collections;
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

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

                FieldInfo aimingField = controllerType.GetField(
                    "isAiming",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                MethodInfo setCursorLocked = controllerType.GetMethod(
                    "SetCursorLocked",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.That(aimingField, Is.Not.Null);
                Assert.That(setCursorLocked, Is.Not.Null);

                aimingField.SetValue(controller, true);
                setCursorLocked.Invoke(controller, new object[] { false });
                Assert.That(
                    controllerType.GetProperty("IsAiming")?.GetValue(controller),
                    Is.EqualTo(false),
                    "Releasing the cursor must never preserve the zoomed aim state."
                );
                setCursorLocked.Invoke(controller, new object[] { true });
            }
            finally
            {
                UnityEngine.Object.Destroy(player);
                UnityEngine.Object.Destroy(cameraObject);
            }
        }

        [UnityTest]
        public IEnumerator PoweredSuitAimDemo_RuntimeModelAndMuzzleFaceGameplayForward()
        {
            AsyncOperation loadOperation;
#if UNITY_EDITOR
            loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/PoweredSuitAimDemo.unity",
                new LoadSceneParameters(LoadSceneMode.Single)
            );
#else
            loadOperation = SceneManager.LoadSceneAsync(
                "PoweredSuitAimDemo",
                LoadSceneMode.Single
            );
#endif
            Assert.That(loadOperation, Is.Not.Null);

            while (!loadOperation.isDone)
            {
                yield return null;
            }

            // Allow the imported animation root to evaluate, then force the Aim
            // state so the rifle's bore alignment is meaningful. In Idle the
            // weapon is intentionally not guaranteed to point with the player.
            yield return null;
            yield return null;

            GameObject player = null;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == "Generator 109 Player")
                {
                    player = root;
                    break;
                }
            }

            Assert.That(player, Is.Not.Null);
            Behaviour animationDriver = player.GetComponent("PowerSuitAnimationDriver") as Behaviour;
            if (animationDriver != null)
            {
                animationDriver.enabled = false;
            }

            Animator animator = player.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            animator.Play("Aim Locomotion", 0, 0f);
            animator.Update(0.1f);
            yield return null;

            Component weapon = player.GetComponent("PowerSuitWeapon");
            Assert.That(weapon, Is.Not.Null);
            Transform muzzle = weapon.GetType()
                .GetProperty("MuzzleTransform")
                ?.GetValue(weapon) as Transform;
            Assert.That(muzzle, Is.Not.Null);
            Assert.That(muzzle.name, Is.EqualTo("WeaponMuzzle"));

            Transform importedMuzzle = muzzle.parent;
            Transform rifleRoot = importedMuzzle?.parent;
            Transform stock = rifleRoot?.Find("Rifle_StockContact");
            Assert.That(importedMuzzle, Is.Not.Null);
            Assert.That(rifleRoot, Is.Not.Null);
            Assert.That(stock, Is.Not.Null);

            Vector3 physicalBore =
                (importedMuzzle.position - stock.position).normalized;
            Assert.That(
                Vector3.Dot(physicalBore, player.transform.forward),
                Is.GreaterThan(0.9f),
                "The Aim pose's physical stock-to-muzzle axis must face gameplay forward."
            );
            Assert.That(
                Vector3.Dot(muzzle.forward, player.transform.forward),
                Is.GreaterThan(0.9f),
                "The Animator-evaluated rifle bore must face gameplay forward."
            );
            Assert.That(
                Vector3.Dot(muzzle.forward, physicalBore),
                Is.GreaterThan(0.99f),
                "The muzzle adapter must match the physical rifle bore."
            );

            Transform rightHand = FindChild(player.transform, "Hand.R");
            Transform leftHand = FindChild(player.transform, "Hand.L");
            Transform primaryTarget = FindChild(player.transform, "Rifle_PrimaryGrip");
            Transform supportTarget = FindChild(player.transform, "Rifle_SupportGripTarget");
            Assert.That(rightHand, Is.Not.Null);
            Assert.That(leftHand, Is.Not.Null);
            Assert.That(primaryTarget, Is.Not.Null);
            Assert.That(supportTarget, Is.Not.Null);
            Assert.That(
                Vector3.Distance(rightHand.position, primaryTarget.position),
                Is.LessThan(0.01f),
                "The Aim pose trigger wrist must remain on its imported target."
            );
            Assert.That(
                Vector3.Distance(leftHand.position, supportTarget.position),
                Is.LessThan(0.01f),
                "The Aim pose support wrist must remain on its imported target."
            );

            Transform visor = FindChild(player.transform, "Helmet_Visor");
            Transform helmet = FindChild(player.transform, "Helmet_Core");
            Assert.That(visor, Is.Not.Null);
            Assert.That(helmet, Is.Not.Null);
            Assert.That(
                Vector3.Dot(
                    (visor.position - helmet.position).normalized,
                    player.transform.forward
                ),
                Is.GreaterThan(0.8f),
                "The animated suit's physical front must face gameplay forward."
            );
        }

        [UnityTest]
        public IEnumerator PoweredSuitAimDemo_NormalCameraHasEvaluationRoomAndStablePacing()
        {
            AsyncOperation loadOperation;
#if UNITY_EDITOR
            loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/PoweredSuitAimDemo.unity",
                new LoadSceneParameters(LoadSceneMode.Single)
            );
#else
            loadOperation = SceneManager.LoadSceneAsync(
                "PoweredSuitAimDemo",
                LoadSceneMode.Single
            );
#endif
            Assert.That(loadOperation, Is.Not.Null);

            while (!loadOperation.isDone)
            {
                yield return null;
            }

            yield return null;
            yield return null;

            GameObject player = FindRoot(
                SceneManager.GetActiveScene(),
                "Generator 109 Player"
            );
            Assert.That(player, Is.Not.Null);

            Component controller = player.GetComponent("PowerSuitController");
            Assert.That(controller, Is.Not.Null);
            Assert.That(
                controller.GetType().GetProperty("IsAiming")?.GetValue(controller),
                Is.EqualTo(false)
            );

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Assert.That(camera.fieldOfView, Is.EqualTo(65f).Within(0.1f));

            Vector3 normalPivot = player.transform.position + Vector3.up * 1.55f;
            Assert.That(
                Vector3.Distance(camera.transform.position, normalPivot),
                Is.EqualTo(6f).Within(0.1f)
            );

            Component framePacing = player.GetComponent("PowerSuitFramePacing");
            Assert.That(framePacing, Is.Not.Null);
            Assert.That(Application.runInBackground, Is.True);
            Assert.That(Application.targetFrameRate, Is.EqualTo(60));
            Assert.That(
                framePacing.GetType().GetProperty("SynchronizeToDisplay")
                    ?.GetValue(framePacing),
                Is.EqualTo(true)
            );

            Type pacingPolicyType = Type.GetType(
                "PowerSuitFramePacingPolicy, Assembly-CSharp",
                true
            );
            MethodInfo shouldUseVSync = pacingPolicyType.GetMethod(
                "ShouldUseVSync",
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(shouldUseVSync, Is.Not.Null);
            double refreshRate = Screen.currentResolution.refreshRateRatio.value;
            bool expectedVSync = (bool)shouldUseVSync.Invoke(
                null,
                new object[] { true, refreshRate, 60 }
            );
            Assert.That(
                QualitySettings.vSyncCount,
                Is.EqualTo(expectedVSync ? 1 : 0),
                "Displays at 60 Hz or faster should synchronize to native refresh; " +
                "slower/unknown displays should use the 60 FPS target fallback."
            );
        }

        [UnityTest]
        public IEnumerator PoweredSuitAimDemo_AimReturnsAfterBoltCycleAndActionLayerReleases()
        {
            AsyncOperation loadOperation;
#if UNITY_EDITOR
            loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/PoweredSuitAimDemo.unity",
                new LoadSceneParameters(LoadSceneMode.Single)
            );
#else
            loadOperation = SceneManager.LoadSceneAsync(
                "PoweredSuitAimDemo",
                LoadSceneMode.Single
            );
#endif
            Assert.That(loadOperation, Is.Not.Null);

            while (!loadOperation.isDone)
            {
                yield return null;
            }

            yield return null;
            yield return null;

            GameObject player = FindRoot(
                SceneManager.GetActiveScene(),
                "Generator 109 Player"
            );
            Assert.That(player, Is.Not.Null);

            Behaviour controller = player.GetComponent("PowerSuitController") as Behaviour;
            Behaviour animationDriver =
                player.GetComponent("PowerSuitAnimationDriver") as Behaviour;
            Component weaponAnimationDriver =
                player.GetComponent("PowerSuitWeaponAnimationDriver");
            Animator animator = player.GetComponentInChildren<Animator>(true);
            Assert.That(controller, Is.Not.Null);
            Assert.That(animationDriver, Is.Not.Null);
            Assert.That(weaponAnimationDriver, Is.Not.Null);
            Assert.That(animator, Is.Not.Null);

            // Preserve the real animation adapter while replacing only live
            // mouse input with a deterministic held-aim value.
            controller.enabled = false;
            FieldInfo aimingField = controller.GetType().GetField(
                "isAiming",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(aimingField, Is.Not.Null);
            aimingField.SetValue(controller, true);

            float aimDeadline = Time.realtimeSinceStartup + 1f;
            while (
                Time.realtimeSinceStartup < aimDeadline &&
                (!animator.GetBool("IsAiming") ||
                 !IsInState(animator, 0, "Aim Locomotion"))
            )
            {
                yield return null;
            }

            Assert.That(animator.GetBool("IsAiming"), Is.True);
            Assert.That(
                IsInState(animator, 0, "Aim Locomotion"),
                Is.True,
                "The real controller-to-driver path must enter Aim Locomotion."
            );
            Assert.That(
                animator.GetLayerWeight(1),
                Is.LessThan(0.01f),
                "The idle weapon-action layer must not override Aim Locomotion."
            );

            Component weapon = player.GetComponent("PowerSuitWeapon");
            Assert.That(weapon, Is.Not.Null);
            MethodInfo tryFireWeapon = weapon.GetType().GetMethod(
                "TryFireWeapon",
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(tryFireWeapon, Is.Not.Null);
            object fireResult = tryFireWeapon.Invoke(weapon, null);
            PropertyInfo firedProperty = fireResult?.GetType().GetProperty("Fired");
            Assert.That(firedProperty, Is.Not.Null);
            Assert.That(
                firedProperty.GetValue(fireResult),
                Is.EqualTo(true),
                "The regression must exercise an accepted shot and its real cycle event."
            );

            float cycleDeadline = Time.realtimeSinceStartup + 1f;
            while (
                Time.realtimeSinceStartup < cycleDeadline &&
                !IsInState(animator, 1, "Bolt Cycle")
            )
            {
                yield return null;
            }

            Assert.That(
                IsInState(animator, 1, "Bolt Cycle"),
                Is.True,
                "An accepted shot must enter the bolt-cycle presentation."
            );
            Assert.That(animator.GetLayerWeight(1), Is.GreaterThan(0.99f));

            float releaseDeadline = Time.realtimeSinceStartup + 2f;
            while (
                Time.realtimeSinceStartup < releaseDeadline &&
                (!animator.GetCurrentAnimatorStateInfo(1)
                    .IsName("No Weapon Action") ||
                 animator.IsInTransition(1) ||
                 animator.GetLayerWeight(1) > 0.01f)
            )
            {
                yield return null;
            }

            // Let the Animator evaluate once with the released override weight
            // before reading the resulting physical rifle pose.
            yield return null;

            Assert.That(
                animator.GetCurrentAnimatorStateInfo(1).IsName("No Weapon Action"),
                Is.True
            );
            Assert.That(animator.IsInTransition(1), Is.False);
            Assert.That(
                animator.GetLayerWeight(1),
                Is.LessThan(0.01f),
                "A completed bolt cycle must release the upper-body override."
            );
            Assert.That(
                IsInState(animator, 0, "Aim Locomotion"),
                Is.True,
                "The base aim state must remain active through bolt cycling."
            );

            Transform muzzle = weapon?.GetType()
                .GetProperty("MuzzleTransform")
                ?.GetValue(weapon) as Transform;
            Transform importedMuzzle = muzzle?.parent;
            Transform rifleRoot = importedMuzzle?.parent;
            Transform stock = rifleRoot?.Find("Rifle_StockContact");
            Assert.That(muzzle, Is.Not.Null);
            Assert.That(importedMuzzle, Is.Not.Null);
            Assert.That(stock, Is.Not.Null);

            Vector3 physicalBore =
                (importedMuzzle.position - stock.position).normalized;
            Assert.That(
                Vector3.Dot(physicalBore, player.transform.forward),
                Is.GreaterThan(0.9f),
                "The rifle must return to its forward aim pose after a shot."
            );
        }

        [UnityTest]
        public IEnumerator PoweredSuitAimDemo_WeaponActionsPreserveLocomotionAndFlightCarryStates()
        {
            AsyncOperation loadOperation;
#if UNITY_EDITOR
            loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/PoweredSuitAimDemo.unity",
                new LoadSceneParameters(LoadSceneMode.Single)
            );
#else
            loadOperation = SceneManager.LoadSceneAsync(
                "PoweredSuitAimDemo",
                LoadSceneMode.Single
            );
#endif
            Assert.That(loadOperation, Is.Not.Null);

            while (!loadOperation.isDone)
            {
                yield return null;
            }

            yield return null;
            yield return null;

            GameObject player = FindRoot(
                SceneManager.GetActiveScene(),
                "Generator 109 Player"
            );
            Assert.That(player, Is.Not.Null);

            DisableBehaviour(player, "PowerSuitController");
            DisableBehaviour(player, "PowerSuitAnimationDriver");
            DisableBehaviour(player, "PowerSuitWeaponPresentation");
            DisableBehaviour(player, "PowerSuitWeaponAnimationDriver");
            DisableBehaviour(player, "PowerSuitWeapon");

            Animator animator = player.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.layerCount, Is.EqualTo(2));
            Assert.That(animator.GetLayerName(1), Is.EqualTo("Weapon Actions"));
            animator.SetLayerWeight(1, 1f);
            Assert.That(animator.GetLayerWeight(1), Is.GreaterThan(0.99f));

            animator.Rebind();
            animator.SetBool("IsFlying", false);
            animator.SetBool("IsAiming", false);
            animator.SetBool("WeaponStowed", false);
            animator.SetFloat("MovementY", 1f);
            animator.SetFloat("LocomotionPlaybackSpeed", 2f);
            animator.Play("Ready Locomotion", 0, 0f);
            animator.Play("No Weapon Action", 1, 0f);
            animator.Update(0f);

            Transform animatedModel = FindChild(
                player.transform,
                "PowerSuitModel_Generator111"
            );
            Transform head = FindChild(player.transform, "Head");
            Transform leftFoot = FindChild(player.transform, "Foot.L");
            Transform rightFoot = FindChild(player.transform, "Foot.R");
            Assert.That(animatedModel, Is.Not.Null);
            Assert.That(head, Is.Not.Null);
            Assert.That(leftFoot, Is.Not.Null);
            Assert.That(rightFoot, Is.Not.Null);

            Vector3 baselineModelPosition = animatedModel.localPosition;
            Quaternion baselineModelRotation = animatedModel.localRotation;
            Vector3 baselineModelScale = animatedModel.localScale;
            Assert.That(baselineModelPosition, Is.EqualTo(Vector3.zero));
            Assert.That(
                Quaternion.Angle(baselineModelRotation, Quaternion.identity),
                Is.LessThan(0.1f)
            );
            Assert.That(baselineModelScale, Is.EqualTo(Vector3.one));
            AssertAnimatorRootSafe(
                animatedModel,
                baselineModelPosition,
                baselineModelRotation,
                baselineModelScale,
                head,
                leftFoot,
                rightFoot,
                "initial locomotion"
            );

            AssertWeaponActionRoundTrip(
                animator,
                "DrawWeapon",
                "Draw Weapon",
                1.5f,
                animatedModel,
                baselineModelPosition,
                baselineModelRotation,
                baselineModelScale,
                head,
                leftFoot,
                rightFoot
            );
            AssertWeaponActionRoundTrip(
                animator,
                "SheatheWeapon",
                "Sheathe Weapon",
                1.5f,
                animatedModel,
                baselineModelPosition,
                baselineModelRotation,
                baselineModelScale,
                head,
                leftFoot,
                rightFoot
            );

            Transform lowerLeg = FindChild(player.transform, "LowerLeg.L");
            Assert.That(lowerLeg, Is.Not.Null);

            animator.SetTrigger("ReloadWeapon");
            Assert.That(
                AdvanceUntilState(animator, 1, "Reload", 0.5f),
                Is.True,
                "ReloadWeapon must enter the masked Reload state."
            );
            AssertAnimatorRootSafe(
                animatedModel,
                baselineModelPosition,
                baselineModelRotation,
                baselineModelScale,
                head,
                leftFoot,
                rightFoot,
                "reload entry"
            );
            Assert.That(
                IsInState(animator, 0, "Ready Locomotion"),
                Is.True,
                "The base locomotion state must remain active during reload."
            );
            Assert.That(
                MeasureBoneMotion(animator, lowerLeg, 0.45f),
                Is.GreaterThan(0.1f),
                "The lower leg must keep walking while the upper-body reload plays."
            );
            AssertAnimatorRootSafe(
                animatedModel,
                baselineModelPosition,
                baselineModelRotation,
                baselineModelScale,
                head,
                leftFoot,
                rightFoot,
                "reload playback"
            );
            Assert.That(
                AdvanceUntilState(animator, 1, "No Weapon Action", 3.5f),
                Is.True,
                "Reload must return to No Weapon Action."
            );
            AssertAnimatorRootSafe(
                animatedModel,
                baselineModelPosition,
                baselineModelRotation,
                baselineModelScale,
                head,
                leftFoot,
                rightFoot,
                "reload exit"
            );

            animator.SetTrigger("CycleWeapon");
            Assert.That(
                AdvanceUntilState(animator, 1, "Bolt Cycle", 0.5f),
                Is.True,
                "CycleWeapon must enter the masked Bolt Cycle state."
            );
            AssertAnimatorRootSafe(
                animatedModel,
                baselineModelPosition,
                baselineModelRotation,
                baselineModelScale,
                head,
                leftFoot,
                rightFoot,
                "bolt-cycle entry"
            );
            Assert.That(
                IsInState(animator, 0, "Ready Locomotion"),
                Is.True,
                "The base locomotion state must remain active during bolt cycling."
            );
            Assert.That(
                MeasureBoneMotion(animator, lowerLeg, 0.3f),
                Is.GreaterThan(0.1f),
                "The lower leg must keep walking while the upper-body bolt cycle plays."
            );
            AssertAnimatorRootSafe(
                animatedModel,
                baselineModelPosition,
                baselineModelRotation,
                baselineModelScale,
                head,
                leftFoot,
                rightFoot,
                "bolt-cycle playback"
            );
            Assert.That(
                AdvanceUntilState(animator, 1, "No Weapon Action", 1.2f),
                Is.True,
                "Bolt Cycle must return to No Weapon Action."
            );
            AssertAnimatorRootSafe(
                animatedModel,
                baselineModelPosition,
                baselineModelRotation,
                baselineModelScale,
                head,
                leftFoot,
                rightFoot,
                "bolt-cycle exit"
            );

            animator.SetBool("IsFlying", true);
            animator.SetBool("WeaponStowed", false);
            animator.Play("Hover", 0, 0f);
            animator.Update(0f);

            animator.SetBool("WeaponStowed", true);
            Assert.That(
                AdvanceUntilState(animator, 0, "Stowed Hover", 0.5f),
                Is.True,
                "Stowing while airborne must select Stowed Hover."
            );

            animator.SetBool("WeaponStowed", false);
            Assert.That(
                AdvanceUntilState(animator, 0, "Hover", 0.5f),
                Is.True,
                "Drawing while airborne must return to ready Hover."
            );
        }

        private static void AssertWeaponActionRoundTrip(
            Animator animator,
            string triggerName,
            string actionStateName,
            float returnTimeout,
            Transform animatedModel,
            Vector3 baselineModelPosition,
            Quaternion baselineModelRotation,
            Vector3 baselineModelScale,
            Transform head,
            Transform leftFoot,
            Transform rightFoot
        )
        {
            animator.SetTrigger(triggerName);
            Assert.That(
                AdvanceUntilState(animator, 1, actionStateName, 0.5f),
                Is.True,
                $"{triggerName} must enter {actionStateName}."
            );
            AssertAnimatorRootSafe(
                animatedModel,
                baselineModelPosition,
                baselineModelRotation,
                baselineModelScale,
                head,
                leftFoot,
                rightFoot,
                $"{actionStateName} entry"
            );
            Assert.That(
                AdvanceUntilState(
                    animator,
                    1,
                    "No Weapon Action",
                    returnTimeout
                ),
                Is.True,
                $"{actionStateName} must return to No Weapon Action."
            );
            AssertAnimatorRootSafe(
                animatedModel,
                baselineModelPosition,
                baselineModelRotation,
                baselineModelScale,
                head,
                leftFoot,
                rightFoot,
                $"{actionStateName} exit"
            );
        }

        private static void AssertAnimatorRootSafe(
            Transform animatedModel,
            Vector3 baselinePosition,
            Quaternion baselineRotation,
            Vector3 baselineScale,
            Transform head,
            Transform leftFoot,
            Transform rightFoot,
            string context
        )
        {
            Assert.That(
                Vector3.Distance(animatedModel.localPosition, baselinePosition),
                Is.LessThan(0.001f),
                $"The Animator root moved during {context}."
            );
            Assert.That(
                Quaternion.Angle(animatedModel.localRotation, baselineRotation),
                Is.LessThan(0.1f),
                $"The Animator root rotated during {context}."
            );
            Assert.That(
                Vector3.Distance(animatedModel.localScale, baselineScale),
                Is.LessThan(0.001f),
                $"The Animator root scaled during {context}."
            );

            float highestFoot = Mathf.Max(leftFoot.position.y, rightFoot.position.y);
            Assert.That(
                head.position.y,
                Is.GreaterThan(highestFoot + 1f),
                $"The powered suit stopped being upright during {context}."
            );
        }

        private static bool AdvanceUntilState(
            Animator animator,
            int layerIndex,
            string stateName,
            float timeout
        )
        {
            const float step = 0.02f;
            int iterations = Mathf.CeilToInt(timeout / step);
            for (int index = 0; index <= iterations; index++)
            {
                if (IsInState(animator, layerIndex, stateName))
                {
                    return true;
                }

                animator.Update(step);
            }

            return IsInState(animator, layerIndex, stateName);
        }

        private static bool IsInState(
            Animator animator,
            int layerIndex,
            string stateName
        )
        {
            int expectedHash = Animator.StringToHash(stateName);
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(layerIndex);
            if (current.shortNameHash == expectedHash)
            {
                return true;
            }

            return animator.IsInTransition(layerIndex) &&
                animator.GetNextAnimatorStateInfo(layerIndex).shortNameHash == expectedHash;
        }

        private static float MeasureBoneMotion(
            Animator animator,
            Transform bone,
            float duration
        )
        {
            const float step = 0.05f;
            Quaternion initialRotation = bone.localRotation;
            float maximumAngle = 0f;
            int iterations = Mathf.CeilToInt(duration / step);
            for (int index = 0; index < iterations; index++)
            {
                animator.Update(step);
                maximumAngle = Mathf.Max(
                    maximumAngle,
                    Quaternion.Angle(initialRotation, bone.localRotation)
                );
            }

            return maximumAngle;
        }

        private static void DisableBehaviour(GameObject root, string typeName)
        {
            Behaviour behaviour = root.GetComponent(typeName) as Behaviour;
            if (behaviour != null)
            {
                behaviour.enabled = false;
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

        private static Transform FindChild(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            foreach (Transform child in root)
            {
                Transform found = FindChild(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
