using System.Collections;
using System;
using System.Reflection;
using System.Linq;
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
        private const string BaseLayerName = "Base Layer";
        private const string ForwardWeaponPoseLayerName = "Forward Weapon Pose";
        private const string BoltCycleActionLayerName = "Bolt Cycle Action";
        private const string WeaponActionsLayerName = "Weapon Actions";

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
                Assert.That(
                    controllerType.GetProperty("IsPrimaryFireSuppressed")
                        ?.GetValue(controller),
                    Is.EqualTo(true),
                    "An unlocked cursor must suppress gameplay fire."
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

            int baseLayerIndex = RequireLayerIndex(animator, BaseLayerName);
            animator.Play("Aim Locomotion", baseLayerIndex, 0f);
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
            Assert.That(camera.fieldOfView, Is.EqualTo(72f).Within(0.1f));

            Vector3 normalPivot = player.transform.position + Vector3.up * 1.5f;
            Assert.That(
                Vector3.Distance(camera.transform.position, normalPivot),
                Is.EqualTo(9.5f).Within(0.1f)
            );
            float originalAspect = camera.aspect;
            foreach (float testAspect in new[] { 16f / 9f, 4f / 3f })
            {
                camera.aspect = testAspect;
                AssertRendererBoundsInsideViewport(
                    player,
                    camera,
                    0.05f,
                    0.95f,
                    0.1f,
                    0.9f,
                    0.12f,
                    0.25f,
                    $"normal full-body framing at aspect {testAspect:F3}"
                );
            }
            camera.aspect = originalAspect;

            FieldInfo flyingField = controller.GetType().GetField(
                "isFlying",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(flyingField, Is.Not.Null);
            flyingField.SetValue(controller, true);

            float flightCameraDeadline = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < flightCameraDeadline)
            {
                Vector3 flightPivot =
                    player.transform.position + Vector3.up * 1.75f;
                bool reachedFlightProfile =
                    Mathf.Abs(camera.fieldOfView - 74f) <= 0.1f &&
                    Mathf.Abs(
                        Vector3.Distance(camera.transform.position, flightPivot) - 11f
                    ) <= 0.1f;
                if (reachedFlightProfile)
                {
                    break;
                }

                yield return null;
            }

            Vector3 finalFlightPivot =
                player.transform.position + Vector3.up * 1.75f;
            Assert.That(camera.fieldOfView, Is.EqualTo(74f).Within(0.1f));
            Assert.That(
                Vector3.Distance(camera.transform.position, finalFlightPivot),
                Is.EqualTo(11f).Within(0.1f),
                "Unobstructed flight must use the wider flight exploration profile."
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

            Type pacingPolicyType = FindType("PowerSuitFramePacingPolicy");
            Assert.That(pacingPolicyType, Is.Not.Null);
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
        public IEnumerator PoweredSuitAimDemo_AimCameraKeepsSuitInFrameAtCommonAspects()
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
            Camera camera = Camera.main;
            Assert.That(controller, Is.Not.Null);
            Assert.That(camera, Is.Not.Null);

            MethodInfo updateCamera = controller.GetType().GetMethod(
                "UpdateCamera",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(updateCamera, Is.Not.Null);

            controller.enabled = false;
            SetAimRequested(controller, true);
            float settleDeadline = Time.realtimeSinceStartup + 1.5f;
            while (
                Time.realtimeSinceStartup < settleDeadline &&
                Mathf.Abs(camera.fieldOfView - 62f) > 0.1f
            )
            {
                updateCamera.Invoke(controller, null);
                yield return null;
            }
            updateCamera.Invoke(controller, null);
            Assert.That(camera.fieldOfView, Is.EqualTo(62f).Within(0.1f));

            float originalAspect = camera.aspect;
            foreach (float testAspect in new[] { 16f / 9f, 4f / 3f })
            {
                camera.aspect = testAspect;
                AssertRendererBoundsInsideViewport(
                    player,
                    camera,
                    0.05f,
                    0.85f,
                    0.15f,
                    0.9f,
                    0.3f,
                    0.45f,
                    $"aim full-body framing at aspect {testAspect:F3}"
                );
            }
            camera.aspect = originalAspect;
            controller.enabled = true;
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
            int baseLayerIndex = RequireLayerIndex(animator, BaseLayerName);
            int forwardWeaponPoseLayerIndex = RequireLayerIndex(
                animator,
                ForwardWeaponPoseLayerName
            );
            int weaponActionsLayerIndex = RequireLayerIndex(
                animator,
                WeaponActionsLayerName
            );
            int boltCycleActionLayerIndex = RequireLayerIndex(
                animator,
                BoltCycleActionLayerName
            );

            // Preserve the real animation adapter while replacing only live
            // mouse input with a deterministic held-aim value. Request the
            // shot before an Animator evaluation so this also guards the
            // first-frame aim-transition staging contract.
            controller.enabled = false;
            SetAimRequested(controller, true);

            Component weapon = player.GetComponent("PowerSuitWeapon");
            Assert.That(weapon, Is.Not.Null);
            int magazineBeforeShot = GetIntProperty(
                weapon,
                "CurrentMagazineAmmo"
            );
            MethodInfo requestFire = weapon.GetType().GetMethod(
                "RequestFire",
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(requestFire, Is.Not.Null);
            Assert.That(
                requestFire.Invoke(weapon, null),
                Is.EqualTo(true),
                "The public request path must accept and stage the shot."
            );
            Assert.That(
                GetIntProperty(weapon, "CurrentMagazineAmmo"),
                Is.EqualTo(magazineBeforeShot),
                "A first-frame aim shot must wait for one Animator evaluation before committing."
            );
            Assert.That(
                GetBoolProperty(weapon, "IsCycling"),
                Is.False,
                "The gameplay transaction must not begin before the staged pose is evaluated."
            );
            MethodInfo weaponUpdate = weapon.GetType().GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(weaponUpdate, Is.Not.Null);
            weaponUpdate.Invoke(weapon, null);
            Assert.That(
                GetIntProperty(weapon, "CurrentMagazineAmmo"),
                Is.EqualTo(magazineBeforeShot),
                "An early-order caller must not let the weapon consume a staged shot again in the same frame."
            );
            Assert.That(GetBoolProperty(weapon, "IsCycling"), Is.False);
            Assert.That(
                animator.GetLayerWeight(forwardWeaponPoseLayerIndex),
                Is.GreaterThan(0.99f),
                "Staging must raise the forward-pose layer immediately."
            );
            Assert.That(
                animator.GetLayerWeight(weaponActionsLayerIndex),
                Is.LessThan(0.01f),
                "The idle weapon-action layer must not override the staged aim pose."
            );

            float cycleDeadline = Time.realtimeSinceStartup + 1f;
            while (
                Time.realtimeSinceStartup < cycleDeadline &&
                (
                    GetIntProperty(weapon, "CurrentMagazineAmmo") ==
                        magazineBeforeShot ||
                    !animator.GetBool("IsAiming") ||
                    !IsInState(animator, baseLayerIndex, "Aim Locomotion") ||
                    !IsStableState(
                        animator,
                        boltCycleActionLayerIndex,
                        "Bolt Cycle"
                    )
                )
            )
            {
                yield return null;
            }

            Assert.That(
                GetIntProperty(weapon, "CurrentMagazineAmmo"),
                Is.EqualTo(magazineBeforeShot - 1),
                "The staged request must commit exactly one accepted shot."
            );
            Assert.That(animator.GetBool("IsAiming"), Is.True);
            Assert.That(
                IsInState(animator, baseLayerIndex, "Aim Locomotion"),
                Is.True,
                "The real controller-to-driver path must enter Aim Locomotion."
            );
            Assert.That(
                IsStableState(
                    animator,
                    boltCycleActionLayerIndex,
                    "Bolt Cycle"
                ),
                Is.True,
                "An accepted shot must enter the additive bolt-cycle presentation."
            );
            Assert.That(
                animator.GetLayerWeight(boltCycleActionLayerIndex),
                Is.GreaterThan(0.99f)
            );
            Assert.That(
                animator.GetLayerWeight(forwardWeaponPoseLayerIndex),
                Is.GreaterThan(0.99f),
                "Accepted fire must hold the weapon's forward pose through cycling."
            );
            Assert.That(
                animator.GetLayerWeight(weaponActionsLayerIndex),
                Is.LessThan(0.01f),
                "Bolt cycling must not enter the diagonal override-action layer."
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
            Vector3 cycleBore =
                (importedMuzzle.position - stock.position).normalized;
            Assert.That(
                Vector3.Dot(cycleBore, player.transform.forward),
                Is.GreaterThan(0.9f),
                "The rifle must remain forward while the bolt action is playing."
            );

            float releaseDeadline = Time.realtimeSinceStartup + 2f;
            while (
                Time.realtimeSinceStartup < releaseDeadline &&
                (!animator.GetCurrentAnimatorStateInfo(boltCycleActionLayerIndex)
                    .IsName("No Bolt Cycle") ||
                 animator.IsInTransition(boltCycleActionLayerIndex) ||
                 animator.GetLayerWeight(boltCycleActionLayerIndex) > 0.01f ||
                 animator.GetLayerWeight(forwardWeaponPoseLayerIndex) > 0.01f)
            )
            {
                yield return null;
            }

            // Let the Animator evaluate once with the released override weight
            // before reading the resulting physical rifle pose.
            yield return null;

            Assert.That(
                animator.GetCurrentAnimatorStateInfo(boltCycleActionLayerIndex)
                    .IsName("No Bolt Cycle"),
                Is.True
            );
            Assert.That(
                animator.IsInTransition(boltCycleActionLayerIndex),
                Is.False
            );
            Assert.That(
                animator.GetLayerWeight(boltCycleActionLayerIndex),
                Is.LessThan(0.01f),
                "A completed bolt cycle must release the additive action layer."
            );
            Assert.That(
                IsInState(animator, baseLayerIndex, "Aim Locomotion"),
                Is.True,
                "The base aim state must remain active through bolt cycling."
            );

            Vector3 physicalBore =
                (importedMuzzle.position - stock.position).normalized;
            Assert.That(
                Vector3.Dot(physicalBore, player.transform.forward),
                Is.GreaterThan(0.9f),
                "The rifle must return to its forward aim pose after a shot."
            );

            float fireReadyDeadline = Time.realtimeSinceStartup + 2f;
            while (
                Time.realtimeSinceStartup < fireReadyDeadline &&
                !GetBoolProperty(weapon, "CanFire")
            )
            {
                yield return null;
            }
            Assert.That(GetBoolProperty(weapon, "CanFire"), Is.True);
            int stableAimMagazine = GetIntProperty(
                weapon,
                "CurrentMagazineAmmo"
            );
            Assert.That(requestFire.Invoke(weapon, null), Is.EqualTo(true));
            Assert.That(
                GetIntProperty(weapon, "CurrentMagazineAmmo"),
                Is.EqualTo(stableAimMagazine - 1),
                "A fully evaluated forward aim pose must fire immediately without adding input latency."
            );
        }

        [UnityTest]
        public IEnumerator PoweredSuitAimDemo_AirborneAimAndReloadPreserveHover()
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

            // This is an animation/carry-state regression, not an encounter
            // endurance test. Remove the runtime-owned sandbox before waiting
            // through a real bolt cycle and reload so enemy damage cannot kill
            // and respawn the player, which intentionally clears AimRequested.
            Component demoBootstrap = player.GetComponent(
                "PowerSuitDemoBootstrap"
            );
            Assert.That(demoBootstrap, Is.Not.Null);
            MethodInfo cleanupOwnedWorld = demoBootstrap.GetType().GetMethod(
                "CleanupOwnedWorld",
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(cleanupOwnedWorld, Is.Not.Null);
            cleanupOwnedWorld.Invoke(demoBootstrap, null);

            GameObject enemies = FindRoot(
                SceneManager.GetActiveScene(),
                "Test Enemies"
            );
            if (enemies != null)
            {
                enemies.SetActive(false);
            }

            Behaviour controller =
                player.GetComponent("PowerSuitController") as Behaviour;
            Behaviour animationDriver =
                player.GetComponent("PowerSuitAnimationDriver") as Behaviour;
            Component weapon = player.GetComponent("PowerSuitWeapon");
            Component weaponPresentation = player.GetComponent(
                "PowerSuitWeaponPresentation"
            );
            Animator animator = player.GetComponentInChildren<Animator>(true);
            Assert.That(controller, Is.Not.Null);
            Assert.That(animationDriver, Is.Not.Null);
            Assert.That(animationDriver.enabled, Is.True);
            Assert.That(weapon, Is.Not.Null);
            Assert.That(weaponPresentation, Is.Not.Null);
            Assert.That(animator, Is.Not.Null);

            int baseLayerIndex = RequireLayerIndex(animator, BaseLayerName);
            int forwardWeaponPoseLayerIndex = RequireLayerIndex(
                animator,
                ForwardWeaponPoseLayerName
            );
            int weaponActionsLayerIndex = RequireLayerIndex(
                animator,
                WeaponActionsLayerName
            );
            int boltCycleActionLayerIndex = RequireLayerIndex(
                animator,
                BoltCycleActionLayerName
            );

            // Keep the real animation, carry, weapon-runtime, and action-layer
            // adapters active. Only replace live keyboard/mouse sampling with
            // deterministic held flight + aim state.
            controller.enabled = false;
            FieldInfo flyingField = controller.GetType().GetField(
                "isFlying",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(flyingField, Is.Not.Null);
            flyingField.SetValue(controller, true);
            SetAimRequested(controller, true);

            float airborneAimDeadline = Time.realtimeSinceStartup + 1.5f;
            while (
                Time.realtimeSinceStartup < airborneAimDeadline &&
                (!animator.GetBool("IsFlying") ||
                 !animator.GetBool("IsAiming") ||
                 !IsInState(animator, baseLayerIndex, "Hover") ||
                 animator.GetLayerWeight(forwardWeaponPoseLayerIndex) < 0.99f)
            )
            {
                yield return null;
            }

            Assert.That(animator.GetBool("IsFlying"), Is.True);
            Assert.That(animator.GetBool("IsAiming"), Is.True);
            Assert.That(
                IsInState(animator, baseLayerIndex, "Hover"),
                Is.True,
                "Airborne aim must retain Hover on the locomotion base layer."
            );
            Assert.That(
                animator.GetLayerWeight(forwardWeaponPoseLayerIndex),
                Is.GreaterThan(0.99f),
                "Held airborne aim must raise the masked forward-weapon layer."
            );
            Assert.That(
                animator.GetLayerWeight(weaponActionsLayerIndex),
                Is.LessThan(0.01f),
                "Airborne aim alone must not raise the action override."
            );
            Assert.That(
                GetBoolProperty(weapon, "PresentationAllowsReload"),
                Is.True,
                "Stable Ready carry must allow reload independently of flight."
            );
            AssertWeaponBoreFacesForward(player, weapon, "airborne aim");

            int initialMagazine = GetIntProperty(weapon, "CurrentMagazineAmmo");
            int initialReserve = GetIntProperty(weapon, "ReserveAmmo");
            MethodInfo tryFireWeapon = weapon.GetType().GetMethod(
                "TryFireWeapon",
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(tryFireWeapon, Is.Not.Null);
            object fireResult = tryFireWeapon.Invoke(weapon, null);
            Assert.That(
                fireResult?.GetType().GetProperty("Fired")?.GetValue(fireResult),
                Is.EqualTo(true),
                "The airborne reload regression needs one real spent round."
            );
            Assert.That(
                GetIntProperty(weapon, "CurrentMagazineAmmo"),
                Is.EqualTo(initialMagazine - 1)
            );
            AssertWeaponBoreFacesForward(player, weapon, "airborne bolt cycle");

            float cycleReleaseDeadline = Time.realtimeSinceStartup + 3.5f;
            while (
                Time.realtimeSinceStartup < cycleReleaseDeadline &&
                (GetBoolProperty(weapon, "IsCycling") ||
                 !IsStableState(
                     animator,
                     boltCycleActionLayerIndex,
                     "No Bolt Cycle"
                 ) ||
                 animator.GetLayerWeight(boltCycleActionLayerIndex) > 0.01f)
            )
            {
                yield return null;
            }

            Assert.That(GetBoolProperty(weapon, "IsCycling"), Is.False);
            Assert.That(
                IsStableState(
                    animator,
                    boltCycleActionLayerIndex,
                    "No Bolt Cycle"
                ),
                Is.True,
                "The preparatory bolt cycle must release before reload begins."
            );

            MethodInfo tryStartReload = weapon.GetType().GetMethod(
                "TryStartReload",
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(tryStartReload, Is.Not.Null);
            object reloadStartResult = tryStartReload.Invoke(weapon, null);
            Assert.That(
                reloadStartResult?.ToString(),
                Is.EqualTo("Started"),
                "A ready weapon must accept reload while flying."
            );

            float reloadEntryDeadline = Time.realtimeSinceStartup + 1f;
            while (
                Time.realtimeSinceStartup < reloadEntryDeadline &&
                (!IsInState(animator, weaponActionsLayerIndex, "Reload") ||
                 animator.GetLayerWeight(weaponActionsLayerIndex) < 0.99f)
            )
            {
                yield return null;
            }

            Assert.That(GetBoolProperty(weapon, "IsReloading"), Is.True);
            Assert.That(
                IsInState(animator, weaponActionsLayerIndex, "Reload"),
                Is.True,
                "Accepted airborne reload must enter the Reload action state."
            );
            Assert.That(
                animator.GetLayerWeight(weaponActionsLayerIndex),
                Is.GreaterThan(0.99f)
            );
            Assert.That(
                IsInState(animator, baseLayerIndex, "Hover"),
                Is.True,
                "Reload must not replace airborne Hover locomotion."
            );
            Assert.That(
                animator.GetLayerWeight(forwardWeaponPoseLayerIndex),
                Is.GreaterThan(0.99f),
                "Held aim must remain selected beneath the reload action."
            );

            // Unity may deliberately throttle frames during editor focus or
            // domain transitions. Allow the real 2.8-second gameplay timer to
            // accumulate without turning an editor stall into a false failure.
            float reloadCompletionDeadline = Time.realtimeSinceStartup + 8f;
            while (
                Time.realtimeSinceStartup < reloadCompletionDeadline &&
                (GetBoolProperty(weapon, "IsReloading") ||
                 !IsStableState(
                     animator,
                     weaponActionsLayerIndex,
                     "No Weapon Action"
                 ) ||
                 animator.GetLayerWeight(weaponActionsLayerIndex) > 0.01f)
            )
            {
                yield return null;
            }

            yield return null;

            Assert.That(GetBoolProperty(weapon, "IsReloading"), Is.False);
            Assert.That(
                IsStableState(
                    animator,
                    weaponActionsLayerIndex,
                    "No Weapon Action"
                ),
                Is.True
            );
            Assert.That(
                animator.GetLayerWeight(weaponActionsLayerIndex),
                Is.LessThan(0.01f),
                "Completed airborne reload must release the action layer."
            );
            Assert.That(
                GetIntProperty(weapon, "CurrentMagazineAmmo"),
                Is.EqualTo(initialMagazine),
                "Reload must replace the single round spent by the regression."
            );
            Assert.That(
                GetIntProperty(weapon, "ReserveAmmo"),
                Is.EqualTo(initialReserve - 1)
            );
            Assert.That(
                IsInState(animator, baseLayerIndex, "Hover"),
                Is.True
            );
            Assert.That(
                animator.GetLayerWeight(forwardWeaponPoseLayerIndex),
                Is.GreaterThan(0.99f),
                "Held aim must recover immediately after airborne reload. " +
                $"AimRequested={GetBoolProperty(controller, "AimRequested")}, " +
                $"IsAiming={GetBoolProperty(controller, "IsAiming")}, " +
                $"PresentationState={weaponPresentation.GetType().GetProperty("State")?.GetValue(weaponPresentation)}, " +
                $"CanUseWeapon={GetBoolProperty(weaponPresentation, "CanUseWeapon")}"
            );
            AssertWeaponBoreFacesForward(
                player,
                weapon,
                "airborne reload recovery"
            );
        }

        [UnityTest]
        public IEnumerator PoweredSuitAimDemo_HipFireUsesForwardPoseWithoutAimZoom()
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
            GameObject enemies = FindRoot(
                SceneManager.GetActiveScene(),
                "Test Enemies"
            );
            if (enemies != null)
            {
                enemies.SetActive(false);
            }

            Behaviour controller = player.GetComponent("PowerSuitController") as Behaviour;
            Component weapon = player.GetComponent("PowerSuitWeapon");
            Animator animator = player.GetComponentInChildren<Animator>(true);
            Assert.That(controller, Is.Not.Null);
            Assert.That(weapon, Is.Not.Null);
            Assert.That(animator, Is.Not.Null);

            MethodInfo setCursorLocked = controller.GetType().GetMethod(
                "SetCursorLocked",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(setCursorLocked, Is.Not.Null);
            setCursorLocked.Invoke(controller, new object[] { true });

            int baseLayerIndex = RequireLayerIndex(animator, BaseLayerName);
            int forwardPoseLayerIndex = RequireLayerIndex(
                animator,
                ForwardWeaponPoseLayerName
            );
            int boltCycleLayerIndex = RequireLayerIndex(
                animator,
                BoltCycleActionLayerName
            );
            int weaponActionsLayerIndex = RequireLayerIndex(
                animator,
                WeaponActionsLayerName
            );

            FieldInfo aimingField = controller.GetType().GetField(
                "isAiming",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(aimingField, Is.Not.Null);
            aimingField.SetValue(controller, false);
            animator.SetBool("IsAiming", false);

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            float explorationFov = camera.fieldOfView;
            float explorationDistance = Vector3.Distance(
                camera.transform.position,
                player.transform.position + Vector3.up * 1.5f
            );
            Vector3 cameraHeading = Vector3.ProjectOnPlane(
                camera.transform.forward,
                Vector3.up
            ).normalized;
            Transform animatedBolt = player.GetComponentsInChildren<Transform>(true)
                .SingleOrDefault(candidate => candidate.name == "WeaponBolt");
            Assert.That(animatedBolt, Is.Not.Null);
            Vector3 boltReferencePosition = animatedBolt.localPosition;
            player.transform.rotation = Quaternion.LookRotation(
                Quaternion.Euler(0f, 55f, 0f) * cameraHeading,
                Vector3.up
            );

            int initialMagazine = GetIntProperty(weapon, "CurrentMagazineAmmo");
            MethodInfo requestFire = weapon.GetType().GetMethod(
                "RequestFire",
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(requestFire, Is.Not.Null);
            object requestAccepted = requestFire.Invoke(weapon, null);
            Assert.That(
                requestAccepted,
                Is.EqualTo(true)
            );
            Assert.That(
                GetIntProperty(weapon, "CurrentMagazineAmmo"),
                Is.EqualTo(initialMagazine),
                "Hip fire must wait one Animator evaluation before sampling the muzzle."
            );

            float poseDeadline = Time.realtimeSinceStartup + 1f;
            while (
                Time.realtimeSinceStartup < poseDeadline &&
                (
                    GetIntProperty(weapon, "CurrentMagazineAmmo") == initialMagazine ||
                    animator.GetLayerWeight(forwardPoseLayerIndex) < 0.99f ||
                    !IsInState(animator, boltCycleLayerIndex, "Bolt Cycle")
                )
            )
            {
                yield return null;
            }
            Assert.That(
                GetIntProperty(weapon, "CurrentMagazineAmmo"),
                Is.EqualTo(initialMagazine - 1),
                "The staged request must become one accepted shot after pose evaluation."
            );

            Assert.That(
                controller.GetType().GetProperty("IsAiming")?.GetValue(controller),
                Is.EqualTo(false),
                "Hip fire must not enable gameplay aim."
            );
            Assert.That(camera.fieldOfView, Is.EqualTo(explorationFov).Within(0.1f));
            Assert.That(
                Vector3.Distance(
                    camera.transform.position,
                    player.transform.position + Vector3.up * 1.5f
                ),
                Is.EqualTo(explorationDistance).Within(0.15f),
                "Hip fire must retain the exploration-camera distance."
            );
            Assert.That(
                Vector3.Dot(player.transform.forward, cameraHeading),
                Is.GreaterThan(0.999f),
                "An accepted hip shot must face the suit toward the camera combat ray."
            );
            Assert.That(
                IsInState(animator, baseLayerIndex, "Ready Locomotion"),
                Is.True
            );
            Assert.That(
                animator.GetLayerWeight(forwardPoseLayerIndex),
                Is.GreaterThan(0.99f)
            );
            Assert.That(
                IsInState(animator, boltCycleLayerIndex, "Bolt Cycle"),
                Is.True
            );
            Assert.That(
                animator.GetLayerWeight(weaponActionsLayerIndex),
                Is.LessThan(0.01f),
                "Hip fire must never invoke the diagonal override-action pose."
            );
            AssertWeaponBoreFacesForward(player, weapon, "grounded hip fire");

            player.transform.rotation = Quaternion.LookRotation(
                Quaternion.Euler(0f, -55f, 0f) * cameraHeading,
                Vector3.up
            );
            setCursorLocked.Invoke(controller, new object[] { true });

            float midpointDeadline = Time.realtimeSinceStartup + 0.75f;
            while (Time.realtimeSinceStartup < midpointDeadline)
            {
                AnimatorStateInfo cycleState =
                    animator.GetCurrentAnimatorStateInfo(boltCycleLayerIndex);
                if (
                    cycleState.IsName("Bolt Cycle") &&
                    cycleState.normalizedTime >= 0.35f
                )
                {
                    break;
                }

                yield return null;
            }

            AnimatorStateInfo midpointState =
                animator.GetCurrentAnimatorStateInfo(boltCycleLayerIndex);
            Assert.That(midpointState.IsName("Bolt Cycle"), Is.True);
            Assert.That(midpointState.normalizedTime, Is.GreaterThanOrEqualTo(0.35f));
            Assert.That(
                Vector3.Dot(player.transform.forward, cameraHeading),
                Is.GreaterThan(0.9f),
                "Live ground movement logic must retain combat-facing throughout the cycle."
            );
            Assert.That(
                Vector3.Distance(animatedBolt.localPosition, boltReferencePosition),
                Is.GreaterThan(0.02f),
                "The additive cycle must move the physical bolt at mid-action."
            );
            AssertWeaponBoreFacesForward(
                player,
                weapon,
                "grounded hip fire at bolt midpoint"
            );

            float releaseDeadline = Time.realtimeSinceStartup + 3f;
            while (
                Time.realtimeSinceStartup < releaseDeadline &&
                (
                    GetBoolProperty(weapon, "IsCycling") ||
                    !IsStableState(animator, boltCycleLayerIndex, "No Bolt Cycle") ||
                    animator.GetLayerWeight(boltCycleLayerIndex) > 0.01f ||
                    animator.GetLayerWeight(forwardPoseLayerIndex) > 0.01f
                )
            )
            {
                yield return null;
            }

            Assert.That(GetBoolProperty(weapon, "IsCycling"), Is.False);
            Assert.That(
                animator.GetLayerWeight(boltCycleLayerIndex),
                Is.LessThan(0.01f)
            );
            Assert.That(
                animator.GetLayerWeight(forwardPoseLayerIndex),
                Is.LessThan(0.01f),
                "The temporary hip-fire pose must release after cycle recovery."
            );
        }

        [UnityTest]
        public IEnumerator PoweredSuitAimDemo_RespawnCancelsTransientPlayerState()
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
            GameObject enemies = FindRoot(
                SceneManager.GetActiveScene(),
                "Test Enemies"
            );
            if (enemies != null)
            {
                enemies.SetActive(false);
            }

            Component controller = player.GetComponent("PowerSuitController");
            Component weapon = player.GetComponent("PowerSuitWeapon");
            Component presentation = player.GetComponent(
                "PowerSuitWeaponPresentation"
            );
            Component health = player.GetComponent("PlayerHealth");
            Animator animator = player.GetComponentInChildren<Animator>(true);
            Assert.That(controller, Is.Not.Null);
            Assert.That(weapon, Is.Not.Null);
            Assert.That(presentation, Is.Not.Null);
            Assert.That(health, Is.Not.Null);
            Assert.That(animator, Is.Not.Null);
            int forwardPoseLayerIndex = RequireLayerIndex(
                animator,
                ForwardWeaponPoseLayerName
            );
            int boltCycleLayerIndex = RequireLayerIndex(
                animator,
                BoltCycleActionLayerName
            );
            int weaponActionsLayerIndex = RequireLayerIndex(
                animator,
                WeaponActionsLayerName
            );

            SetPrivateField(health, "respawnDelay", 0.05f);
            controller.GetType().GetMethod("SetFlightEnabled")?.Invoke(
                controller,
                new object[] { true }
            );
            SetAimRequested(controller, true);

            object fireResult = weapon.GetType().GetMethod("TryFireWeapon")
                ?.Invoke(weapon, null);
            Assert.That(
                fireResult?.GetType().GetProperty("Fired")?.GetValue(fireResult),
                Is.EqualTo(true)
            );
            Assert.That(GetBoolProperty(weapon, "IsCycling"), Is.True);

            float cycleEntryDeadline = Time.realtimeSinceStartup + 0.75f;
            while (
                Time.realtimeSinceStartup < cycleEntryDeadline &&
                !IsStableState(animator, boltCycleLayerIndex, "Bolt Cycle")
            )
            {
                yield return null;
            }
            Assert.That(
                IsStableState(animator, boltCycleLayerIndex, "Bolt Cycle"),
                Is.True,
                "The player must be visibly mid-cycle before defeat resets presentation."
            );
            Assert.That(
                animator.GetLayerWeight(boltCycleLayerIndex),
                Is.GreaterThan(0.99f)
            );
            Assert.That(
                animator.GetLayerWeight(forwardPoseLayerIndex),
                Is.GreaterThan(0.99f)
            );

            int restoredCount = 0;
            int respawnedCount = 0;
            bool restoredWasDamageable = false;
            Action<float, float> onRestored = (_, __) =>
            {
                restoredCount++;
                restoredWasDamageable = GetBoolProperty(
                    health,
                    "CanReceiveDamage"
                );
            };
            Action onRespawned = () => respawnedCount++;
            health.GetType().GetEvent("OnHealthRestored")?.AddEventHandler(
                health,
                onRestored
            );
            health.GetType().GetEvent("OnRespawned")?.AddEventHandler(
                health,
                onRespawned
            );

            health.GetType().GetMethod("TakeDamage")?.Invoke(
                health,
                new object[] { 1000f }
            );

            float deadline = Time.realtimeSinceStartup + 2f;
            while (
                Time.realtimeSinceStartup < deadline &&
                respawnedCount == 0
            )
            {
                yield return null;
            }

            Assert.That(respawnedCount, Is.EqualTo(1));
            Assert.That(restoredCount, Is.EqualTo(1));
            Assert.That(restoredWasDamageable, Is.True);
            Assert.That(GetBoolProperty(controller, "IsFlying"), Is.False);
            Assert.That(GetBoolProperty(controller, "IsAiming"), Is.False);
            Assert.That(GetBoolProperty(weapon, "IsCycling"), Is.False);
            Assert.That(GetBoolProperty(weapon, "IsReloading"), Is.False);
            Assert.That(((Behaviour)controller).enabled, Is.True);
            Assert.That(((Behaviour)weapon).enabled, Is.True);
            Assert.That(((Behaviour)presentation).enabled, Is.True);
            Assert.That(
                presentation.GetType().GetProperty("State")
                    ?.GetValue(presentation)?.ToString(),
                Is.EqualTo("Ready")
            );
            Assert.That(
                animator.GetLayerWeight(forwardPoseLayerIndex),
                Is.LessThan(0.01f),
                "Respawn must release the transient forward-pose layer."
            );
            Assert.That(
                animator.GetLayerWeight(boltCycleLayerIndex),
                Is.LessThan(0.01f),
                "Respawn must release the bolt-cycle layer."
            );
            Assert.That(
                animator.GetLayerWeight(weaponActionsLayerIndex),
                Is.LessThan(0.01f),
                "Respawn must release the general weapon-action layer."
            );
            Assert.That(
                IsStableState(animator, boltCycleLayerIndex, "No Bolt Cycle"),
                Is.True,
                "Respawn must restore the bolt layer's neutral state."
            );
            Assert.That(
                IsStableState(
                    animator,
                    weaponActionsLayerIndex,
                    "No Weapon Action"
                ),
                Is.True,
                "Respawn must restore the action layer's neutral state."
            );
            Assert.That(
                (float)health.GetType().GetProperty("CurrentHealth")
                    ?.GetValue(health),
                Is.EqualTo(
                    (float)health.GetType().GetProperty("MaximumHealth")
                        ?.GetValue(health)
                )
            );
        }

        [UnityTest]
        public IEnumerator PoweredSuitAimDemo_WeaponLoadoutSwitchesAndPreservesAmmo()
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
            GameObject enemies = FindRoot(
                SceneManager.GetActiveScene(),
                "Test Enemies"
            );
            if (enemies != null)
            {
                enemies.SetActive(false);
            }
            Type encounterType = FindType("PowerSuitEncounterDirector");
            Type spawnDirectorType = FindType(
                "Powersuit.Enemies.UnityAdapters.EnemySpawnDirector"
            );
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (encounterType != null)
                {
                    foreach (
                        Component encounter in root.GetComponentsInChildren(
                            encounterType,
                            true
                        )
                    )
                    {
                        if (encounter is Behaviour behaviour)
                        {
                            behaviour.enabled = false;
                        }
                    }
                }
                if (spawnDirectorType != null)
                {
                    foreach (
                        Component director in root.GetComponentsInChildren(
                            spawnDirectorType,
                            true
                        )
                    )
                    {
                        director.GetType().GetMethod("SetDirectorEnabled")
                            ?.Invoke(director, new object[] { false });
                        director.GetType().GetMethod("ClearActiveEnemies")
                            ?.Invoke(director, null);
                    }
                }
            }

            Component weapon = player.GetComponent("PowerSuitWeapon");
            Component loadout = player.GetComponent("PowerSuitWeaponLoadout");
            Component controller = player.GetComponent("PowerSuitController");
            Component scopeSight = player.GetComponent("PowerSuitScopeSight");
            Component presentation = player.GetComponent(
                "PowerSuitWeaponPresentation"
            );
            Component weaponVisuals = player.GetComponent(
                "PowerSuitWeaponVisualController"
            );
            Assert.That(weapon, Is.Not.Null);
            Assert.That(loadout, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(scopeSight, Is.Not.Null);
            Assert.That(presentation, Is.Not.Null);
            Assert.That(weaponVisuals, Is.Not.Null);
            Assert.That(GetIntProperty(loadout, "SlotCount"), Is.EqualTo(3));
            Assert.That(GetIntProperty(loadout, "EquippedIndex"), Is.EqualTo(0));
            Assert.That(GetWeaponDisplayName(weapon), Is.EqualTo("Precision Rifle"));

            int precisionMagazine = GetIntProperty(
                weapon,
                "CurrentMagazineAmmo"
            );
            Assert.That(precisionMagazine, Is.EqualTo(5));

            object switchResult = loadout.GetType().GetMethod("RequestSlot")
                ?.Invoke(loadout, new object[] { 1 });
            Assert.That(switchResult?.ToString(), Is.EqualTo("Queued"));
            Assert.That(
                loadout.GetType().GetProperty("IsSwitching")?.GetValue(loadout),
                Is.EqualTo(true)
            );

            bool observedSheathing = false;
            bool observedDrawing = false;
            float switchDeadline = Time.realtimeSinceStartup + 4f;
            while (
                GetIntProperty(loadout, "EquippedIndex") != 1 ||
                (bool)loadout.GetType().GetProperty("IsSwitching")
                    .GetValue(loadout)
            )
            {
                string carryState = presentation.GetType()
                    .GetProperty("State")?.GetValue(presentation)?.ToString();
                observedSheathing |= carryState == "Sheathing";
                observedDrawing |= carryState == "Drawing";
                Assert.That(
                    Time.realtimeSinceStartup,
                    Is.LessThan(switchDeadline),
                    "The visible sheathe/swap/draw sequence did not finish."
                );
                yield return null;
            }

            Assert.That(GetIntProperty(loadout, "EquippedIndex"), Is.EqualTo(1));
            Assert.That(observedSheathing, Is.True);
            Assert.That(observedDrawing, Is.True);
            Assert.That(GetWeaponDisplayName(weapon), Is.EqualTo("Assault Rifle"));
            Assert.That(
                scopeSight.GetType().GetProperty("IsScopeEligible")
                    ?.GetValue(scopeSight),
                Is.EqualTo(false),
                "Only the Precision Rifle may enter magnified scope mode."
            );
            Assert.That(GetBoolProperty(controller, "IsScoped"), Is.False);

            Renderer[] scopeRenderers = player
                .GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.name.StartsWith("Rifle_Scope"))
                .ToArray();
            Assert.That(scopeRenderers.Length, Is.GreaterThan(0));
            Assert.That(
                scopeRenderers.All(renderer => !renderer.enabled),
                Is.True,
                "The shared prototype receiver must hide its precision optic when the Assault Rifle is equipped."
            );
            Assert.That(
                weaponVisuals.GetType().GetProperty("IsAssaultVisualActive")
                    ?.GetValue(weaponVisuals),
                Is.EqualTo(true)
            );
            Transform assaultFeedbackRoot = weaponVisuals.GetType()
                .GetProperty("AssaultFeedbackRoot")
                ?.GetValue(weaponVisuals) as Transform;
            Assert.That(assaultFeedbackRoot, Is.Not.Null);
            Renderer[] assaultRenderers = assaultFeedbackRoot.parent
                .GetComponentsInChildren<Renderer>(true);
            Assert.That(assaultRenderers.Length, Is.GreaterThanOrEqualTo(12));
            Assert.That(
                assaultRenderers.All(renderer => renderer.enabled),
                Is.True
            );

            object fireResult = weapon.GetType().GetMethod("TryFireWeapon")
                ?.Invoke(weapon, null);
            Assert.That(
                fireResult?.GetType().GetProperty("Fired")?.GetValue(fireResult),
                Is.EqualTo(true)
            );
            Assert.That(
                GetIntProperty(weapon, "CurrentMagazineAmmo"),
                Is.EqualTo(29)
            );
            Assert.That(
                weapon.GetType().GetProperty("CurrentReticleStyle")
                    ?.GetValue(weapon)?.ToString(),
                Is.EqualTo("AssaultDynamic")
            );
            Assert.That(
                (float)weaponVisuals.GetType().GetProperty("RecoilAmount")
                    .GetValue(weaponVisuals),
                Is.GreaterThan(0f),
                "An accepted automatic-rifle shot must kick its visible receiver."
            );

            loadout.GetType().GetMethod("RequestSlot")
                ?.Invoke(loadout, new object[] { 2 });
            switchDeadline = Time.realtimeSinceStartup + 4f;
            while (
                GetIntProperty(loadout, "EquippedIndex") != 2 ||
                (bool)loadout.GetType().GetProperty("IsSwitching")
                    .GetValue(loadout)
            )
            {
                Assert.That(
                    Time.realtimeSinceStartup,
                    Is.LessThan(switchDeadline),
                    "The Assault-to-Heavy visible switch did not finish."
                );
                yield return null;
            }
            Assert.That(
                GetWeaponDisplayName(weapon),
                Is.EqualTo("Heavy Plasma Cannon")
            );
            Assert.That(
                weapon.GetType().GetProperty("CurrentReticleStyle")
                    ?.GetValue(weapon)?.ToString(),
                Is.EqualTo("HeavyCharge")
            );
            Assert.That(
                weapon.GetType().GetProperty("IsCharging")?.GetValue(weapon),
                Is.EqualTo(false)
            );
            Assert.That(
                weapon.GetType().GetMethod("RequestFire")?.Invoke(weapon, null),
                Is.EqualTo(false),
                "The Heavy Plasma Cannon must not bypass its charge-release gate."
            );
            Assert.That(
                weaponVisuals.GetType().GetProperty("IsHeavyVisualActive")
                    ?.GetValue(weaponVisuals),
                Is.EqualTo(true)
            );
            Transform heavyFeedbackRoot = weaponVisuals.GetType()
                .GetProperty("HeavyFeedbackRoot")
                ?.GetValue(weaponVisuals) as Transform;
            Assert.That(heavyFeedbackRoot, Is.Not.Null);
            Assert.That(
                heavyFeedbackRoot.parent.GetComponentsInChildren<Renderer>(true)
                    .All(renderer => renderer.enabled),
                Is.True
            );

            loadout.GetType().GetMethod("RequestSlot")
                ?.Invoke(loadout, new object[] { 0 });
            switchDeadline = Time.realtimeSinceStartup + 4f;
            while (
                GetIntProperty(loadout, "EquippedIndex") != 0 ||
                (bool)loadout.GetType().GetProperty("IsSwitching")
                    .GetValue(loadout)
            )
            {
                Assert.That(
                    Time.realtimeSinceStartup,
                    Is.LessThan(switchDeadline)
                );
                yield return null;
            }
            Assert.That(GetWeaponDisplayName(weapon), Is.EqualTo("Precision Rifle"));
            Assert.That(
                GetIntProperty(weapon, "CurrentMagazineAmmo"),
                Is.EqualTo(precisionMagazine),
                "Switching must not overwrite the Precision Rifle's magazine."
            );
            Assert.That(
                scopeSight.GetType().GetProperty("IsScopeEligible")
                    ?.GetValue(scopeSight),
                Is.EqualTo(true)
            );
            Assert.That(
                scopeRenderers.All(renderer => renderer.enabled),
                Is.True,
                "Returning to the Precision Rifle must restore the authored optic renderers."
            );
            Assert.That(
                weaponVisuals.GetType().GetProperty("IsAssaultVisualActive")
                    ?.GetValue(weaponVisuals),
                Is.EqualTo(false)
            );
            Assert.That(
                assaultRenderers.All(renderer => !renderer.enabled),
                Is.True
            );

            loadout.GetType().GetMethod("RequestSlot")
                ?.Invoke(loadout, new object[] { 1 });
            switchDeadline = Time.realtimeSinceStartup + 4f;
            while (
                GetIntProperty(loadout, "EquippedIndex") != 1 ||
                (bool)loadout.GetType().GetProperty("IsSwitching")
                    .GetValue(loadout)
            )
            {
                Assert.That(
                    Time.realtimeSinceStartup,
                    Is.LessThan(switchDeadline)
                );
                yield return null;
            }
            Assert.That(
                GetIntProperty(weapon, "CurrentMagazineAmmo"),
                Is.EqualTo(29),
                "Each loadout slot must retain its own ammunition state."
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
            Assert.That(animator.layerCount, Is.EqualTo(4));
            int baseLayerIndex = RequireLayerIndex(animator, BaseLayerName);
            int forwardWeaponPoseLayerIndex = RequireLayerIndex(
                animator,
                ForwardWeaponPoseLayerName
            );
            int weaponActionsLayerIndex = RequireLayerIndex(
                animator,
                WeaponActionsLayerName
            );
            int boltCycleActionLayerIndex = RequireLayerIndex(
                animator,
                BoltCycleActionLayerName
            );
            animator.SetLayerWeight(forwardWeaponPoseLayerIndex, 0f);
            animator.SetLayerWeight(boltCycleActionLayerIndex, 0f);
            animator.SetLayerWeight(weaponActionsLayerIndex, 1f);
            Assert.That(
                animator.GetLayerWeight(forwardWeaponPoseLayerIndex),
                Is.LessThan(0.01f)
            );
            Assert.That(
                animator.GetLayerWeight(weaponActionsLayerIndex),
                Is.GreaterThan(0.99f)
            );

            animator.Rebind();
            animator.SetBool("IsFlying", false);
            animator.SetBool("IsAiming", false);
            animator.SetBool("WeaponStowed", false);
            animator.SetFloat("MovementY", 1f);
            animator.SetFloat("LocomotionPlaybackSpeed", 2f);
            animator.Play("Ready Locomotion", baseLayerIndex, 0f);
            animator.Play(
                "Forward Weapon Pose",
                forwardWeaponPoseLayerIndex,
                0f
            );
            animator.Play(
                "No Bolt Cycle",
                boltCycleActionLayerIndex,
                0f
            );
            animator.Play(
                "No Weapon Action",
                weaponActionsLayerIndex,
                0f
            );
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
                weaponActionsLayerIndex,
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
                weaponActionsLayerIndex,
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
                AdvanceUntilState(
                    animator,
                    weaponActionsLayerIndex,
                    "Reload",
                    0.5f
                ),
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
                IsInState(animator, baseLayerIndex, "Ready Locomotion"),
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
                AdvanceUntilState(
                    animator,
                    weaponActionsLayerIndex,
                    "No Weapon Action",
                    3.5f
                ),
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
            animator.SetLayerWeight(boltCycleActionLayerIndex, 1f);
            Assert.That(
                AdvanceUntilState(
                    animator,
                    boltCycleActionLayerIndex,
                    "Bolt Cycle",
                    0.5f
                ),
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
                IsInState(animator, baseLayerIndex, "Ready Locomotion"),
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
                AdvanceUntilState(
                    animator,
                    boltCycleActionLayerIndex,
                    "No Bolt Cycle",
                    1.2f
                ),
                Is.True,
                "Bolt Cycle must return to No Bolt Cycle."
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
            animator.Play("Hover", baseLayerIndex, 0f);
            animator.Update(0f);

            animator.SetBool("WeaponStowed", true);
            Assert.That(
                AdvanceUntilState(
                    animator,
                    baseLayerIndex,
                    "Stowed Hover",
                    0.5f
                ),
                Is.True,
                "Stowing while airborne must select Stowed Hover."
            );

            animator.SetBool("WeaponStowed", false);
            Assert.That(
                AdvanceUntilState(
                    animator,
                    baseLayerIndex,
                    "Hover",
                    0.5f
                ),
                Is.True,
                "Drawing while airborne must return to ready Hover."
            );
        }

        private static void AssertWeaponActionRoundTrip(
            Animator animator,
            int weaponActionsLayerIndex,
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
                AdvanceUntilState(
                    animator,
                    weaponActionsLayerIndex,
                    actionStateName,
                    0.5f
                ),
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
                    weaponActionsLayerIndex,
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

        private static int RequireLayerIndex(Animator animator, string layerName)
        {
            int layerIndex = animator.GetLayerIndex(layerName);
            Assert.That(
                layerIndex,
                Is.GreaterThanOrEqualTo(0),
                $"Animator layer '{layerName}' is missing."
            );
            return layerIndex;
        }

        private static bool IsStableState(
            Animator animator,
            int layerIndex,
            string stateName
        )
        {
            return !animator.IsInTransition(layerIndex) &&
                animator.GetCurrentAnimatorStateInfo(layerIndex).IsName(stateName);
        }

        private static void SetAimRequested(
            Component controller,
            bool requested
        )
        {
            FieldInfo requestField = controller.GetType().GetField(
                "aimRequested",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            MethodInfo refreshMethod = controller.GetType().GetMethod(
                "RefreshAimAvailability",
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(requestField, Is.Not.Null, "aimRequested");
            Assert.That(refreshMethod, Is.Not.Null, "RefreshAimAvailability");

            requestField.SetValue(controller, requested);
            refreshMethod.Invoke(controller, null);
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value
        )
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static bool GetBoolProperty(Component component, string propertyName)
        {
            PropertyInfo property = component.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(property, Is.Not.Null, propertyName);
            return (bool)property.GetValue(component);
        }

        private static int GetIntProperty(Component component, string propertyName)
        {
            PropertyInfo property = component.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(property, Is.Not.Null, propertyName);
            return (int)property.GetValue(component);
        }

        private static string GetWeaponDisplayName(Component weapon)
        {
            object definition = weapon.GetType().GetProperty("Definition")
                ?.GetValue(weapon);
            Assert.That(definition, Is.Not.Null);
            PropertyInfo displayName = definition.GetType().GetProperty(
                "DisplayName",
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(displayName, Is.Not.Null);
            return displayName.GetValue(definition) as string;
        }

        private static void AssertWeaponBoreFacesForward(
            GameObject player,
            Component weapon,
            string context
        )
        {
            Transform muzzle = weapon.GetType()
                .GetProperty("MuzzleTransform")
                ?.GetValue(weapon) as Transform;
            Transform importedMuzzle = muzzle?.parent;
            Transform rifleRoot = importedMuzzle?.parent;
            Transform stock = rifleRoot?.Find("Rifle_StockContact");
            Assert.That(muzzle, Is.Not.Null, context);
            Assert.That(importedMuzzle, Is.Not.Null, context);
            Assert.That(stock, Is.Not.Null, context);

            Vector3 physicalBore =
                (importedMuzzle.position - stock.position).normalized;
            Assert.That(
                Vector3.Dot(physicalBore, player.transform.forward),
                Is.GreaterThan(0.9f),
                $"The physical rifle bore must face gameplay forward during {context}."
            );
            Assert.That(
                Vector3.Dot(muzzle.forward, player.transform.forward),
                Is.GreaterThan(0.9f),
                $"The muzzle adapter must face gameplay forward during {context}."
            );
        }

        private static void AssertRendererBoundsInsideViewport(
            GameObject player,
            Camera camera,
            float minimumX,
            float maximumX,
            float minimumY,
            float maximumY,
            float minimumVerticalOccupancy,
            float maximumVerticalOccupancy,
            string context
        )
        {
            Renderer[] renderers = player.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
                .ToArray();
            Assert.That(renderers, Is.Not.Empty, context);

            float viewportMinX = float.PositiveInfinity;
            float viewportMinY = float.PositiveInfinity;
            float viewportMaxX = float.NegativeInfinity;
            float viewportMaxY = float.NegativeInfinity;
            foreach (Renderer renderer in renderers)
            {
                Vector3 minimum = renderer.bounds.min;
                Vector3 maximum = renderer.bounds.max;
                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 viewport = camera.WorldToViewportPoint(
                                new Vector3(
                                    x == 0 ? minimum.x : maximum.x,
                                    y == 0 ? minimum.y : maximum.y,
                                    z == 0 ? minimum.z : maximum.z
                                )
                            );
                            Assert.That(
                                viewport.z,
                                Is.GreaterThan(0f),
                                $"{context}: a renderer bound is behind the camera."
                            );
                            viewportMinX = Mathf.Min(viewportMinX, viewport.x);
                            viewportMinY = Mathf.Min(viewportMinY, viewport.y);
                            viewportMaxX = Mathf.Max(viewportMaxX, viewport.x);
                            viewportMaxY = Mathf.Max(viewportMaxY, viewport.y);
                        }
                    }
                }
            }

            float verticalOccupancy = viewportMaxY - viewportMinY;
            Assert.That(viewportMinX, Is.GreaterThanOrEqualTo(minimumX), context);
            Assert.That(viewportMaxX, Is.LessThanOrEqualTo(maximumX), context);
            Assert.That(viewportMinY, Is.GreaterThanOrEqualTo(minimumY), context);
            Assert.That(viewportMaxY, Is.LessThanOrEqualTo(maximumY), context);
            Assert.That(
                verticalOccupancy,
                Is.InRange(minimumVerticalOccupancy, maximumVerticalOccupancy),
                context
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

        private static Type FindType(string typeName)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
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
