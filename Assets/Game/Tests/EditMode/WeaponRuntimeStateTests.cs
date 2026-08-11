using System;
using System.Collections.Generic;
using NUnit.Framework;
using Powersuit.Combat;
using UnityEditor;

namespace Powersuit.Tests.EditMode
{
    public sealed class WeaponRuntimeStateTests
    {
        private const string PrecisionRifleAssetPath =
            "Assets/Game/Content/Weapons/PrecisionRifle.asset";
        private const string AssaultRifleAssetPath =
            "Assets/Game/Content/Weapons/AssaultRifle.asset";
        private const string HeavyPlasmaAssetPath =
            "Assets/Game/Content/Weapons/HeavyPlasmaCannon.asset";

        [Test]
        public void ChargeState_ShortReleaseCancelsWithoutAValidShot()
        {
            WeaponChargeState state = new WeaponChargeState(
                0.8f,
                0.3f,
                0.75f,
                1.55f,
                0.8f,
                1.25f
            );

            Assert.That(state.Begin(), Is.True);
            state.Advance(0.16f);
            WeaponChargeReleaseResult release = state.Release();

            Assert.That(release.ShouldFire, Is.False);
            Assert.That(release.NormalizedCharge, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(state.IsCharging, Is.False);
        }

        [Test]
        public void ChargeState_FullReleaseReturnsAuthoredDamageAndRadius()
        {
            WeaponChargeState state = new WeaponChargeState(
                0.8f,
                0.3f,
                0.75f,
                1.55f,
                0.8f,
                1.25f
            );

            state.Begin();
            state.Advance(1f);
            WeaponChargeReleaseResult release = state.Release();

            Assert.That(release.ShouldFire, Is.True);
            Assert.That(release.NormalizedCharge, Is.EqualTo(1f));
            Assert.That(release.DamageMultiplier, Is.EqualTo(1.55f));
            Assert.That(release.RadiusMultiplier, Is.EqualTo(1.25f));
        }

        [Test]
        public void HeavyPlasmaAsset_IsChargedExplosiveThirdRole()
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(
                HeavyPlasmaAssetPath
            );

            Assert.That(asset, Is.Not.Null);
            WeaponRuntimeConfig config = asset.GetType()
                .GetMethod("CreateRuntimeConfig")
                ?.Invoke(asset, null) as WeaponRuntimeConfig;

            Assert.That(config, Is.Not.Null);
            Assert.That(config.GetValidationErrors(), Is.Empty);
            Assert.That(config.WeaponClass, Is.EqualTo(WeaponClass.HeavyWeapon));
            Assert.That(config.TriggerMode, Is.EqualTo(WeaponTriggerMode.SemiAutomatic));
            Assert.That(config.BaseDamage, Is.EqualTo(112f));
            Assert.That(config.MagazineCapacity, Is.EqualTo(4));
            Assert.That(config.ProjectileSpeed, Is.EqualTo(35f));
            Assert.That(
                asset.GetType().GetProperty("UsesChargeShot")?.GetValue(asset),
                Is.EqualTo(true)
            );
            Assert.That(
                asset.GetType().GetProperty("SplashDamageRadius")?.GetValue(asset),
                Is.EqualTo(5.5f).Within(0.001f)
            );
            Assert.That(
                asset.GetType().GetProperty("ProjectilePrefabOverride")
                    ?.GetValue(asset),
                Is.Not.Null
            );
            Assert.That(
                asset.GetType().GetProperty("ReticleStyle")?.GetValue(asset)
                    ?.ToString(),
                Is.EqualTo("HeavyCharge")
            );
        }

        [Test]
        public void PrecisionRifleAsset_HasPlannedManualActionConfiguration()
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(
                PrecisionRifleAssetPath
            );

            Assert.That(asset, Is.Not.Null);

            object configObject = asset.GetType()
                .GetMethod("CreateRuntimeConfig")
                ?.Invoke(asset, null);
            WeaponRuntimeConfig config = configObject as WeaponRuntimeConfig;

            Assert.That(config, Is.Not.Null);
            Assert.That(config.GetValidationErrors(), Is.Empty);
            Assert.That(config.WeaponClass, Is.EqualTo(WeaponClass.PrecisionRifle));
            Assert.That(config.TriggerMode, Is.EqualTo(WeaponTriggerMode.SemiAutomatic));
            Assert.That(config.BaseDamage, Is.EqualTo(60f));
            Assert.That(config.RoundsPerMinute, Is.EqualTo(45f));
            Assert.That(config.MagazineCapacity, Is.EqualTo(5));
            Assert.That(config.StartingReserveAmmo, Is.EqualTo(25));
            Assert.That(
                asset.GetType().GetProperty("AutoReloadWhenEmpty")?.GetValue(asset),
                Is.EqualTo(true)
            );
            Assert.That(config.ReloadDurationSeconds, Is.EqualTo(2.8f));
            Assert.That(config.ReloadCommitNormalizedTime, Is.EqualTo(0.89f));
            Assert.That(config.CriticalChance, Is.EqualTo(0.1f));
            Assert.That(config.CriticalDamageMultiplier, Is.EqualTo(2f));
            Assert.That(config.RequiresManualCycle, Is.True);
            Assert.That(config.ManualCycleDurationSeconds, Is.EqualTo(0.67f));
            Assert.That(config.ProjectileSpeed, Is.EqualTo(100f));
        }

        [Test]
        public void AssaultRifleAsset_IsAutomaticAndDistinctFromPrecisionRifle()
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(
                AssaultRifleAssetPath
            );

            Assert.That(asset, Is.Not.Null);
            WeaponRuntimeConfig config = asset.GetType()
                .GetMethod("CreateRuntimeConfig")
                ?.Invoke(asset, null) as WeaponRuntimeConfig;

            Assert.That(config, Is.Not.Null);
            Assert.That(config.GetValidationErrors(), Is.Empty);
            Assert.That(config.WeaponClass, Is.EqualTo(WeaponClass.AssaultRifle));
            Assert.That(config.TriggerMode, Is.EqualTo(WeaponTriggerMode.Automatic));
            Assert.That(config.BaseDamage, Is.EqualTo(22f));
            Assert.That(config.RoundsPerMinute, Is.EqualTo(720f));
            Assert.That(config.MagazineCapacity, Is.EqualTo(30));
            Assert.That(config.StartingReserveAmmo, Is.EqualTo(120));
            Assert.That(config.RequiresManualCycle, Is.False);
            Assert.That(
                asset.GetType().GetProperty("SupportsScope")?.GetValue(asset),
                Is.EqualTo(false)
            );
            Assert.That(
                asset.GetType().GetProperty("ProjectilePrewarmCount")?.GetValue(asset),
                Is.EqualTo(48)
            );
            Assert.That(
                asset.GetType().GetProperty("ReticleStyle")?.GetValue(asset)
                    ?.ToString(),
                Is.EqualTo("AssaultDynamic")
            );
            Assert.That(
                asset.GetType().GetProperty("ReticleBaseGapPixels")
                    ?.GetValue(asset),
                Is.EqualTo(7f).Within(0.001f)
            );
            Assert.That(
                asset.GetType().GetProperty("MuzzleFlashIntensity")
                    ?.GetValue(asset),
                Is.EqualTo(10f).Within(0.001f)
            );
            Assert.That(
                asset.GetType().GetProperty("VisualRecoilDistance")
                    ?.GetValue(asset),
                Is.EqualTo(0.035f).Within(0.001f)
            );
        }

        [Test]
        public void Loadout_PreservesIndependentAmmoAndCadenceAcrossSwitches()
        {
            WeaponRuntimeConfig precision = CreateConfiguration(
                "precision",
                WeaponClass.PrecisionRifle,
                WeaponTriggerMode.SemiAutomatic,
                roundsPerMinute: 60f,
                magazineCapacity: 5
            );
            WeaponRuntimeConfig assault = CreateConfiguration(
                "assault",
                WeaponClass.AssaultRifle,
                WeaponTriggerMode.Automatic,
                roundsPerMinute: 600f,
                magazineCapacity: 30
            );
            WeaponLoadoutState loadout = new WeaponLoadoutState(
                new[] { precision, assault }
            );

            Assert.That(loadout.EquippedWeapon.TryFire().Fired, Is.True);
            Assert.That(loadout.EquippedWeapon.CurrentMagazineAmmo, Is.EqualTo(4));
            Assert.That(
                loadout.RequestSelection(1),
                Is.EqualTo(WeaponSelectionRequestResult.Queued)
            );
            Assert.That(loadout.TryCommitPendingSelection(false), Is.False);
            Assert.That(loadout.EquippedIndex, Is.Zero);
            Assert.That(loadout.TryCommitPendingSelection(true), Is.True);

            Assert.That(loadout.EquippedWeapon.TryFire().Fired, Is.True);
            Assert.That(loadout.EquippedWeapon.CurrentMagazineAmmo, Is.EqualTo(29));
            loadout.AdvanceInactive(0.4f);
            loadout.RequestSelection(0);
            Assert.That(loadout.TryCommitPendingSelection(true), Is.True);
            Assert.That(loadout.EquippedWeapon.CurrentMagazineAmmo, Is.EqualTo(4));
            Assert.That(
                loadout.EquippedWeapon.CurrentFireBlockReason,
                Is.EqualTo(WeaponFireBlockReason.FireCadence),
                "Holstering must not erase the precision rifle's cadence."
            );
        }

        [Test]
        public void PrepareForUnequip_CancelsActionsButPreservesCadenceAndAmmo()
        {
            WeaponRuntimeState state = CreateState(
                roundsPerMinute: 60f,
                requiresManualCycle: true,
                manualCycleDuration: 0.67f
            );
            int cancelledCycles = 0;
            state.ManualCycleCancelled += () => cancelledCycles++;

            Assert.That(state.TryFire().Fired, Is.True);
            state.PrepareForUnequip();

            Assert.That(cancelledCycles, Is.EqualTo(1));
            Assert.That(state.IsManualCycleInProgress, Is.False);
            Assert.That(state.CurrentMagazineAmmo, Is.EqualTo(2));
            Assert.That(
                state.CurrentFireBlockReason,
                Is.EqualTo(WeaponFireBlockReason.FireCadence)
            );
        }

        [Test]
        public void Configuration_RejectsInvalidGameplayValues()
        {
            WeaponRuntimeConfig invalid = new WeaponRuntimeConfig(
                weaponId: "",
                displayName: "",
                weaponClass: (WeaponClass)99,
                triggerMode: (WeaponTriggerMode)99,
                baseDamage: 0f,
                roundsPerMinute: 0f,
                usesInfiniteAmmo: false,
                magazineCapacity: 0,
                startingReserveAmmo: 6,
                maximumReserveAmmo: 5,
                reloadDurationSeconds: -1f,
                reloadCommitNormalizedTime: 1.1f,
                criticalChance: -0.1f,
                criticalDamageMultiplier: 0.5f,
                requiresManualCycle: true,
                manualCycleDurationSeconds: 0f,
                projectileSpeed: 0f,
                projectileLifetimeSeconds: 0f,
                projectileRadius: 0f,
                aimSpreadDegrees: -1f,
                hipSpreadDegrees: -1f,
                aimRecoilPitch: -1f,
                aimRecoilYaw: -1f,
                hipRecoilPitch: -1f,
                hipRecoilYaw: -1f
            );

            IReadOnlyList<string> errors = invalid.GetValidationErrors();

            Assert.That(errors.Count, Is.GreaterThanOrEqualTo(12));
            Assert.Throws<ArgumentException>(() => new WeaponRuntimeState(invalid));
        }

        [Test]
        public void Fire_ConsumesMagazineAndEnforcesCadence()
        {
            WeaponRuntimeState state = CreateState(roundsPerMinute: 600f);

            WeaponFireResult first = state.TryFire();
            WeaponFireResult blocked = state.TryFire();

            Assert.That(first.Fired, Is.True);
            Assert.That(first.Damage, Is.EqualTo(60f));
            Assert.That(first.RemainingMagazineAmmo, Is.EqualTo(2));
            Assert.That(state.CurrentMagazineAmmo, Is.EqualTo(2));
            Assert.That(blocked.Fired, Is.False);
            Assert.That(blocked.BlockReason, Is.EqualTo(WeaponFireBlockReason.FireCadence));

            state.Advance(0.1f);

            Assert.That(state.TryFire().Fired, Is.True);
            Assert.That(state.CurrentMagazineAmmo, Is.EqualTo(1));
        }

        [Test]
        public void CriticalDamage_UsesInjectedDeterministicRandomSource()
        {
            SequenceRandomSource random = new SequenceRandomSource(0.09d, 0.1d);
            WeaponRuntimeState state = CreateState(
                roundsPerMinute: 600f,
                criticalChance: 0.1f,
                criticalMultiplier: 2f,
                randomSource: random
            );

            WeaponFireResult critical = state.TryFire();
            state.Advance(0.1f);
            WeaponFireResult normal = state.TryFire();

            Assert.That(critical.IsCritical, Is.True);
            Assert.That(critical.Damage, Is.EqualTo(120f));
            Assert.That(normal.IsCritical, Is.False);
            Assert.That(normal.Damage, Is.EqualTo(60f));
        }

        [Test]
        public void EmptyMagazine_BlocksUntilTimedReloadCommits()
        {
            WeaponRuntimeState state = CreateState(
                magazineCapacity: 2,
                startingReserve: 3,
                roundsPerMinute: 600f,
                reloadDuration: 2f,
                reloadCommitNormalizedTime: 0.5f
            );

            Assert.That(state.TryFire().Fired, Is.True);
            state.Advance(0.1f);
            Assert.That(state.TryFire().Fired, Is.True);
            state.Advance(0.1f);
            Assert.That(
                state.TryFire().BlockReason,
                Is.EqualTo(WeaponFireBlockReason.EmptyMagazine)
            );

            Assert.That(state.TryStartReload(), Is.EqualTo(WeaponReloadStartResult.Started));
            Assert.That(state.IsReloading, Is.True);
            Assert.That(state.TryFire().BlockReason, Is.EqualTo(WeaponFireBlockReason.Reloading));

            state.Advance(0.99f);
            Assert.That(state.CurrentMagazineAmmo, Is.Zero);
            Assert.That(state.CurrentReserveAmmo, Is.EqualTo(3));

            state.Advance(0.01f);
            Assert.That(state.HasReloadCommitted, Is.True);
            Assert.That(state.CurrentMagazineAmmo, Is.EqualTo(2));
            Assert.That(state.CurrentReserveAmmo, Is.EqualTo(1));

            state.Advance(1f);
            Assert.That(state.IsReloading, Is.False);
            Assert.That(state.TryFire().Fired, Is.True);
        }

        [Test]
        public void Reload_TransfersOnlyAvailableReserveRounds()
        {
            WeaponRuntimeState state = CreateState(
                magazineCapacity: 3,
                startingReserve: 1,
                roundsPerMinute: 600f,
                reloadDuration: 1f,
                reloadCommitNormalizedTime: 0.5f
            );

            state.TryFire();
            state.Advance(0.1f);
            state.TryFire();
            state.Advance(0.1f);
            state.TryFire();
            state.Advance(0.1f);

            state.TryStartReload();
            state.Advance(1f);

            Assert.That(state.CurrentMagazineAmmo, Is.EqualTo(1));
            Assert.That(state.CurrentReserveAmmo, Is.Zero);
        }

        [Test]
        public void CancelReload_BeforeCommitDoesNotMoveAmmo()
        {
            WeaponRuntimeState state = CreateState(
                magazineCapacity: 3,
                startingReserve: 3,
                roundsPerMinute: 600f,
                reloadDuration: 2f,
                reloadCommitNormalizedTime: 0.75f
            );

            state.TryFire();
            state.Advance(0.1f);
            state.TryStartReload();
            state.Advance(1f);

            Assert.That(state.CancelReload(), Is.True);
            Assert.That(state.CurrentMagazineAmmo, Is.EqualTo(2));
            Assert.That(state.CurrentReserveAmmo, Is.EqualTo(3));
        }

        [Test]
        public void CancelReload_AfterCommitKeepsTransferredAmmo()
        {
            WeaponRuntimeState state = CreateState(
                magazineCapacity: 3,
                startingReserve: 3,
                roundsPerMinute: 600f,
                reloadDuration: 2f,
                reloadCommitNormalizedTime: 0.5f
            );

            state.TryFire();
            state.Advance(0.1f);
            state.TryStartReload();
            state.Advance(1f);

            Assert.That(state.CancelReload(), Is.True);
            Assert.That(state.CurrentMagazineAmmo, Is.EqualTo(3));
            Assert.That(state.CurrentReserveAmmo, Is.EqualTo(2));
        }

        [Test]
        public void ManualCycle_BlocksNextShotUntilCycleCompletes()
        {
            WeaponRuntimeState state = CreateState(
                roundsPerMinute: 600f,
                requiresManualCycle: true,
                manualCycleDuration: 0.5f
            );

            Assert.That(state.TryFire().Fired, Is.True);
            state.Advance(0.1f);

            Assert.That(state.IsManualCycleInProgress, Is.True);
            Assert.That(
                state.TryFire().BlockReason,
                Is.EqualTo(WeaponFireBlockReason.ManualCycleInProgress)
            );

            Assert.That(state.CompleteManualCycle(), Is.True);
            Assert.That(state.TryFire().Fired, Is.True);
        }

        [Test]
        public void AutomaticReload_WaitsForManualCycleAndRequiresReserveAmmo()
        {
            WeaponRuntimeState state = CreateState(
                magazineCapacity: 1,
                startingReserve: 2,
                roundsPerMinute: 600f,
                requiresManualCycle: true,
                manualCycleDuration: 0.5f
            );

            Assert.That(state.CanStartAutomaticReload, Is.False);
            Assert.That(state.TryFire().Fired, Is.True);
            Assert.That(state.CurrentMagazineAmmo, Is.Zero);
            Assert.That(
                state.CanStartAutomaticReload,
                Is.False,
                "The bolt cycle must finish before an automatic reload can start."
            );

            state.Advance(0.5f);

            Assert.That(state.CanStartAutomaticReload, Is.True);
            Assert.That(
                state.TryStartReload(),
                Is.EqualTo(WeaponReloadStartResult.Started)
            );
            Assert.That(state.CanStartAutomaticReload, Is.False);

            WeaponRuntimeState noReserve = CreateState(
                magazineCapacity: 1,
                startingReserve: 0,
                roundsPerMinute: 600f
            );
            Assert.That(noReserve.TryFire().Fired, Is.True);
            Assert.That(noReserve.CanStartAutomaticReload, Is.False);
        }

        [Test]
        public void PowerSuitWeapon_AutomaticReloadHonorsPresentationGate()
        {
            Type weaponType = Type.GetType(
                "PowerSuitWeapon, Assembly-CSharp",
                throwOnError: true
            );
            UnityEngine.GameObject gameObject = new UnityEngine.GameObject(
                "Auto Reload Test Weapon"
            );
            gameObject.SetActive(false);

            try
            {
                UnityEngine.Component weapon = gameObject.AddComponent(weaponType);
                WeaponRuntimeState state = CreateState(
                    magazineCapacity: 1,
                    startingReserve: 2,
                    roundsPerMinute: 600f
                );
                Assert.That(state.TryFire().Fired, Is.True);
                Assert.That(state.CanStartAutomaticReload, Is.True);

                weaponType.GetField(
                    "runtimeState",
                    System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic
                )?.SetValue(weapon, state);
                System.Reflection.MethodInfo tryAutoReload = weaponType.GetMethod(
                    "TryStartAutomaticReload",
                    System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic
                );
                Assert.That(tryAutoReload, Is.Not.Null);

                weaponType.GetProperty("PresentationAllowsReload")?.SetValue(
                    weapon,
                    false
                );
                tryAutoReload.Invoke(weapon, null);
                Assert.That(state.IsReloading, Is.False);

                weaponType.GetProperty("PresentationAllowsReload")?.SetValue(
                    weapon,
                    true
                );
                tryAutoReload.Invoke(weapon, null);
                Assert.That(state.IsReloading, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ResetTransientState_CancelsReloadWithoutChangingAmmo()
        {
            WeaponRuntimeState state = CreateState(
                roundsPerMinute: 600f,
                reloadDuration: 2f,
                reloadCommitNormalizedTime: 0.75f
            );
            int cancellationCount = 0;
            state.ReloadCancelled += () => cancellationCount++;

            state.TryFire();
            state.Advance(0.1f);
            state.TryStartReload();
            state.Advance(0.5f);
            int magazineBeforeReset = state.CurrentMagazineAmmo;
            int reserveBeforeReset = state.CurrentReserveAmmo;

            state.ResetTransientState();

            Assert.That(state.IsReloading, Is.False);
            Assert.That(state.ReloadElapsed, Is.Zero);
            Assert.That(state.FireCooldownRemaining, Is.Zero);
            Assert.That(state.CurrentMagazineAmmo, Is.EqualTo(magazineBeforeReset));
            Assert.That(state.CurrentReserveAmmo, Is.EqualTo(reserveBeforeReset));
            Assert.That(cancellationCount, Is.EqualTo(1));
        }

        [Test]
        public void ResetTransientState_CancelsManualCycleAndClearsCadence()
        {
            WeaponRuntimeState state = CreateState(
                roundsPerMinute: 600f,
                requiresManualCycle: true,
                manualCycleDuration: 0.5f
            );
            int cancellationCount = 0;
            int completionCount = 0;
            state.ManualCycleCancelled += () => cancellationCount++;
            state.ManualCycleCompleted += () => completionCount++;

            state.TryFire();
            int magazineBeforeReset = state.CurrentMagazineAmmo;

            state.ResetTransientState();

            Assert.That(state.IsManualCycleInProgress, Is.False);
            Assert.That(state.ManualCycleRemaining, Is.Zero);
            Assert.That(state.FireCooldownRemaining, Is.Zero);
            Assert.That(state.CurrentMagazineAmmo, Is.EqualTo(magazineBeforeReset));
            Assert.That(cancellationCount, Is.EqualTo(1));
            Assert.That(completionCount, Is.Zero);
        }

        [Test]
        public void LargeAdvance_CommitsAndCompletesReloadExactlyOnce()
        {
            WeaponRuntimeState state = CreateState(
                reloadDuration: 2f,
                reloadCommitNormalizedTime: 0.5f
            );
            int commitCount = 0;
            int completeCount = 0;
            state.ReloadAmmoCommitted += _ => commitCount++;
            state.ReloadCompleted += () => completeCount++;

            state.TryFire();
            state.Advance(state.Configuration.ShotIntervalSeconds);
            state.TryStartReload();
            state.Advance(10f);

            Assert.That(commitCount, Is.EqualTo(1));
            Assert.That(completeCount, Is.EqualTo(1));
            Assert.That(state.IsReloading, Is.False);
        }

        [Test]
        public void ReserveAmmoPickup_IsCappedAndRejectsNegativeAmounts()
        {
            WeaponRuntimeState state = CreateState(startingReserve: 3, maximumReserve: 5);

            Assert.That(state.AddReserveAmmo(10), Is.EqualTo(2));
            Assert.That(state.CurrentReserveAmmo, Is.EqualTo(5));
            Assert.That(state.AddReserveAmmo(1), Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => state.AddReserveAmmo(-1));
        }

        [Test]
        public void LegacyInfiniteAmmo_NeverConsumesOrReloads()
        {
            WeaponRuntimeConfig config = WeaponRuntimeConfig.CreateLegacyInfiniteAmmo(
                baseDamage: 25f,
                shotsPerSecond: 5f,
                projectileSpeed: 50f,
                projectileLifetimeSeconds: 4f,
                projectileRadius: 0.15f,
                aimSpreadDegrees: 0f,
                hipSpreadDegrees: 0f,
                aimRecoilPitch: 1.2f,
                aimRecoilYaw: 0.35f,
                hipRecoilPitch: 0.7f,
                hipRecoilYaw: 0.2f
            );
            WeaponRuntimeState state = new WeaponRuntimeState(config);

            for (int i = 0; i < 10; i++)
            {
                Assert.That(state.TryFire().Fired, Is.True);
                state.Advance(config.ShotIntervalSeconds);
            }

            Assert.That(state.CurrentMagazineAmmo, Is.EqualTo(config.MagazineCapacity));
            Assert.That(
                state.TryStartReload(),
                Is.EqualTo(WeaponReloadStartResult.InfiniteAmmo)
            );
        }

        [Test]
        public void Advance_RejectsNegativeOrNonFiniteTime()
        {
            WeaponRuntimeState state = CreateState();

            Assert.Throws<ArgumentOutOfRangeException>(() => state.Advance(-0.01f));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.Advance(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => state.Advance(float.PositiveInfinity)
            );
        }

        private static WeaponRuntimeState CreateState(
            float roundsPerMinute = 60f,
            int magazineCapacity = 3,
            int startingReserve = 6,
            int maximumReserve = 12,
            float reloadDuration = 2f,
            float reloadCommitNormalizedTime = 0.5f,
            float criticalChance = 0f,
            float criticalMultiplier = 2f,
            bool requiresManualCycle = false,
            float manualCycleDuration = 0f,
            IWeaponRandomSource randomSource = null
        )
        {
            WeaponRuntimeConfig config = new WeaponRuntimeConfig(
                weaponId: "test-rifle",
                displayName: "Test Rifle",
                weaponClass: WeaponClass.PrecisionRifle,
                triggerMode: WeaponTriggerMode.SemiAutomatic,
                baseDamage: 60f,
                roundsPerMinute: roundsPerMinute,
                usesInfiniteAmmo: false,
                magazineCapacity: magazineCapacity,
                startingReserveAmmo: startingReserve,
                maximumReserveAmmo: maximumReserve,
                reloadDurationSeconds: reloadDuration,
                reloadCommitNormalizedTime: reloadCommitNormalizedTime,
                criticalChance: criticalChance,
                criticalDamageMultiplier: criticalMultiplier,
                requiresManualCycle: requiresManualCycle,
                manualCycleDurationSeconds: manualCycleDuration,
                projectileSpeed: 100f,
                projectileLifetimeSeconds: 4f,
                projectileRadius: 0.15f,
                aimSpreadDegrees: 0.15f,
                hipSpreadDegrees: 1.25f,
                aimRecoilPitch: 0.9f,
                aimRecoilYaw: 0.25f,
                hipRecoilPitch: 1.6f,
                hipRecoilYaw: 0.5f
            );

            return new WeaponRuntimeState(config, randomSource);
        }

        private static WeaponRuntimeConfig CreateConfiguration(
            string id,
            WeaponClass weaponClass,
            WeaponTriggerMode triggerMode,
            float roundsPerMinute,
            int magazineCapacity
        )
        {
            return new WeaponRuntimeConfig(
                weaponId: id,
                displayName: id,
                weaponClass: weaponClass,
                triggerMode: triggerMode,
                baseDamage: 20f,
                roundsPerMinute: roundsPerMinute,
                usesInfiniteAmmo: false,
                magazineCapacity: magazineCapacity,
                startingReserveAmmo: magazineCapacity * 3,
                maximumReserveAmmo: magazineCapacity * 6,
                reloadDurationSeconds: 2.8f,
                reloadCommitNormalizedTime: 0.89f,
                criticalChance: 0f,
                criticalDamageMultiplier: 2f,
                requiresManualCycle: false,
                manualCycleDurationSeconds: 0f,
                projectileSpeed: 90f,
                projectileLifetimeSeconds: 4f,
                projectileRadius: 0.1f,
                aimSpreadDegrees: 0.4f,
                hipSpreadDegrees: 2f,
                aimRecoilPitch: 0.25f,
                aimRecoilYaw: 0.12f,
                hipRecoilPitch: 0.45f,
                hipRecoilYaw: 0.2f
            );
        }

        private sealed class SequenceRandomSource : IWeaponRandomSource
        {
            private readonly Queue<double> values;

            public SequenceRandomSource(params double[] values)
            {
                this.values = new Queue<double>(values);
            }

            public double NextUnitValue()
            {
                return values.Count > 0 ? values.Dequeue() : 1d;
            }
        }
    }
}
