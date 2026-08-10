using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Powersuit.UI.HUD.Tests
{
    public sealed class PowerSuitHudTests
    {
        [Test]
        public void SnapshotSections_SanitizeInvalidExternalValues()
        {
            HudHealthState health = new HudHealthState(
                true,
                float.PositiveInfinity,
                float.NaN,
                false
            );
            HudWeaponState weapon = new HudWeaponState(
                true,
                magazine: 20,
                magazineCapacity: 5,
                reserve: -4,
                usesInfiniteAmmo: false,
                isReloading: true,
                reloadNormalized: float.NaN
            );
            HudAbilityState ability = new HudAbilityState(
                true,
                float.NegativeInfinity,
                4f,
                false,
                false
            );
            HudUltimateState ultimate = new HudUltimateState(
                true,
                -2f,
                false,
                false
            );

            Assert.That(health.Current, Is.Zero);
            Assert.That(health.Maximum, Is.EqualTo(1f));
            Assert.That(health.Normalized, Is.Zero);
            Assert.That(weapon.Magazine, Is.EqualTo(5));
            Assert.That(weapon.Reserve, Is.Zero);
            Assert.That(weapon.ReloadNormalized, Is.Zero);
            Assert.That(ability.CooldownRemaining, Is.Zero);
            Assert.That(ability.CooldownNormalized, Is.EqualTo(1f));
            Assert.That(ability.ReadyNormalized, Is.Zero);
            Assert.That(ultimate.MeterNormalized, Is.Zero);
        }

        [Test]
        public void ChangeDetector_FirstCaptureIsAllAndIdenticalCaptureIsNone()
        {
            PowerSuitHudChangeDetector detector = new PowerSuitHudChangeDetector();
            PowerSuitHudSnapshot snapshot = CreateSnapshot();

            Assert.That(
                detector.Capture(snapshot),
                Is.EqualTo(PowerSuitHudDirtyFlags.All)
            );
            Assert.That(
                detector.Capture(snapshot),
                Is.EqualTo(PowerSuitHudDirtyFlags.None)
            );
            Assert.That(detector.Previous, Is.EqualTo(snapshot));
        }

        [Test]
        public void ChangeDetector_SeparatesAmmunitionFromReloadProgress()
        {
            PowerSuitHudChangeDetector detector = new PowerSuitHudChangeDetector();
            detector.Capture(CreateSnapshot());

            PowerSuitHudSnapshot reloadChanged = CreateSnapshot(
                weapon: new HudWeaponState(true, 3, 5, 20, false, true, 0.5f)
            );
            Assert.That(
                detector.Capture(reloadChanged),
                Is.EqualTo(PowerSuitHudDirtyFlags.Reload)
            );

            PowerSuitHudSnapshot ammoChanged = CreateSnapshot(
                weapon: new HudWeaponState(true, 2, 5, 20, false, true, 0.5f)
            );
            Assert.That(
                detector.Capture(ammoChanged),
                Is.EqualTo(PowerSuitHudDirtyFlags.Ammunition)
            );
        }

        [Test]
        public void ChangeDetector_MarksOnlyChangedAbilitySection()
        {
            PowerSuitHudChangeDetector detector = new PowerSuitHudChangeDetector();
            detector.Capture(CreateSnapshot());

            PowerSuitHudSnapshot changed = CreateSnapshot(
                lightning: new HudAbilityState(true, 2f, 0.4f, false, true)
            );

            Assert.That(
                detector.Capture(changed),
                Is.EqualTo(PowerSuitHudDirtyFlags.Lightning)
            );
        }

        [Test]
        public void Formatter_ProducesStableReadableCombatLabels()
        {
            Assert.That(
                PowerSuitHudFormatter.FormatHealth(
                    new HudHealthState(true, 72.1f, 100f, false)
                ),
                Is.EqualTo("HP 73 / 100")
            );
            Assert.That(
                PowerSuitHudFormatter.FormatAmmunition(
                    new HudWeaponState(true, 3, 5, 20, false, false, 0f)
                ),
                Is.EqualTo("AMMO 3 / 5  RES 20")
            );
            Assert.That(
                PowerSuitHudFormatter.FormatReload(
                    new HudWeaponState(true, 3, 5, 20, false, true, 0.425f)
                ),
                Is.EqualTo("RELOADING 43%")
            );
            Assert.That(
                PowerSuitHudFormatter.FormatAbility(
                    "ROCKET",
                    new HudAbilityState(true, 3.25f, 0.5f, false, false)
                ),
                Is.EqualTo("ROCKET 3.3s")
            );
            Assert.That(
                PowerSuitHudFormatter.FormatUltimate(
                    new HudUltimateState(true, 0.756f, false, false)
                ),
                Is.EqualTo("VOID 76%")
            );
        }

        [Test]
        public void Formatter_PrioritizesUnavailableActiveAndReadyStates()
        {
            Assert.That(
                PowerSuitHudFormatter.FormatAbility("ROCKET", HudAbilityState.Missing),
                Is.EqualTo("ROCKET --")
            );
            Assert.That(
                PowerSuitHudFormatter.FormatAbility(
                    "LIGHTNING",
                    new HudAbilityState(true, 0f, 0f, true, true)
                ),
                Is.EqualTo("LIGHTNING ACTIVE")
            );
            Assert.That(
                PowerSuitHudFormatter.FormatAbility(
                    "ROCKET",
                    new HudAbilityState(true, 0f, 0f, true, false)
                ),
                Is.EqualTo("ROCKET READY")
            );
            Assert.That(
                PowerSuitHudFormatter.FormatUltimate(
                    new HudUltimateState(true, 1f, true, false)
                ),
                Is.EqualTo("VOID READY")
            );
            Assert.That(
                PowerSuitHudFormatter.FormatHealth(
                    new HudHealthState(true, 0f, 100f, true)
                ),
                Is.EqualTo("SUIT DISABLED")
            );
        }

        [Test]
        public void Reset_ForcesNextSnapshotToRefreshEverySection()
        {
            PowerSuitHudChangeDetector detector = new PowerSuitHudChangeDetector();
            PowerSuitHudSnapshot snapshot = CreateSnapshot();
            detector.Capture(snapshot);
            detector.Reset();

            Assert.That(detector.HasPrevious, Is.False);
            Assert.That(
                detector.Capture(snapshot),
                Is.EqualTo(PowerSuitHudDirtyFlags.All)
            );
        }

        [Test]
        public void TextChangeDetector_IgnoresSubDisplayProgressChanges()
        {
            PowerSuitHudTextChangeDetector detector =
                new PowerSuitHudTextChangeDetector();
            PowerSuitHudSnapshot initial = new PowerSuitHudSnapshot(
                new HudHealthState(true, 72.1f, 100f, false),
                new HudWeaponState(true, 3, 5, 20, false, true, 0.421f),
                new HudAbilityState(true, 3.21f, 0.5f, false, false),
                new HudAbilityState(true, 4.71f, 0.5f, false, false),
                new HudUltimateState(true, 0.421f, false, false)
            );
            detector.Capture(initial);

            PowerSuitHudSnapshot visuallyChanged = new PowerSuitHudSnapshot(
                new HudHealthState(true, 72.2f, 100f, false),
                new HudWeaponState(true, 3, 5, 20, false, true, 0.424f),
                new HudAbilityState(true, 3.24f, 0.49f, false, false),
                new HudAbilityState(true, 4.74f, 0.49f, false, false),
                new HudUltimateState(true, 0.424f, false, false)
            );

            Assert.That(
                detector.Capture(visuallyChanged),
                Is.EqualTo(PowerSuitHudDirtyFlags.None),
                "Smooth fill progress must not rebuild identical rounded text."
            );
        }

        [Test]
        public void TextChangeDetector_ReportsVisibleRoundedValueChanges()
        {
            PowerSuitHudTextChangeDetector detector =
                new PowerSuitHudTextChangeDetector();
            detector.Capture(new PowerSuitHudSnapshot(
                new HudHealthState(true, 72.1f, 100f, false),
                new HudWeaponState(true, 3, 5, 20, false, true, 0.424f),
                new HudAbilityState(true, 3.24f, 0.5f, false, false),
                new HudAbilityState(true, 4.74f, 0.5f, false, false),
                new HudUltimateState(true, 0.424f, false, false)
            ));

            PowerSuitHudDirtyFlags dirty = detector.Capture(
                new PowerSuitHudSnapshot(
                    new HudHealthState(true, 73.01f, 100f, false),
                    new HudWeaponState(true, 3, 5, 20, false, true, 0.425f),
                    new HudAbilityState(true, 3.25f, 0.49f, false, false),
                    new HudAbilityState(true, 4.75f, 0.49f, false, false),
                    new HudUltimateState(true, 0.425f, false, false)
                )
            );

            Assert.That(
                dirty,
                Is.EqualTo(
                    PowerSuitHudDirtyFlags.Health |
                    PowerSuitHudDirtyFlags.Reload |
                    PowerSuitHudDirtyFlags.ShoulderRocket |
                    PowerSuitHudDirtyFlags.Lightning |
                    PowerSuitHudDirtyFlags.Ultimate
                )
            );
        }

        [Test]
        public void UnityPresenter_MissingSourcesAndViewsAreGraceful()
        {
            Type presenterType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("PowerSuitHudPresenter"))
                .FirstOrDefault(type => type != null);
            Assert.That(presenterType, Is.Not.Null);

            GameObject host = new GameObject("HUD Presenter Missing Reference Test");
            try
            {
                Component presenter = host.AddComponent(presenterType);
                MethodInfo refresh = presenterType.GetMethod(
                    "RefreshNow",
                    BindingFlags.Instance | BindingFlags.Public
                );
                Assert.That(refresh, Is.Not.Null);
                Assert.DoesNotThrow(() => refresh.Invoke(presenter, null));
                Assert.DoesNotThrow(() => refresh.Invoke(presenter, null));

                object dirty = presenterType.GetProperty("LastDirtyFlags")?.GetValue(
                    presenter
                );
                Assert.That(
                    dirty,
                    Is.EqualTo(PowerSuitHudDirtyFlags.None),
                    "After the initial all-section refresh, an unchanged manual " +
                    "refresh should be allocation-free and clean."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static PowerSuitHudSnapshot CreateSnapshot(
            HudWeaponState? weapon = null,
            HudAbilityState? lightning = null
        )
        {
            return new PowerSuitHudSnapshot(
                new HudHealthState(true, 75f, 100f, false),
                weapon ?? new HudWeaponState(true, 3, 5, 20, false, false, 0f),
                new HudAbilityState(true, 0f, 0f, true, false),
                lightning ?? new HudAbilityState(true, 0f, 0f, true, false),
                new HudUltimateState(true, 0.5f, false, false)
            );
        }
    }
}
