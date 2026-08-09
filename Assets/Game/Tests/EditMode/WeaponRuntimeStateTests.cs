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
            Assert.That(config.ReloadDurationSeconds, Is.EqualTo(2.8f));
            Assert.That(config.ReloadCommitNormalizedTime, Is.EqualTo(0.89f));
            Assert.That(config.CriticalChance, Is.EqualTo(0.1f));
            Assert.That(config.CriticalDamageMultiplier, Is.EqualTo(2f));
            Assert.That(config.RequiresManualCycle, Is.True);
            Assert.That(config.ManualCycleDurationSeconds, Is.EqualTo(0.67f));
            Assert.That(config.ProjectileSpeed, Is.EqualTo(100f));
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
