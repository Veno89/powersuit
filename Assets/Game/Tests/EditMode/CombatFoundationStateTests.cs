using System;
using NUnit.Framework;
using Powersuit.Combat;

namespace Powersuit.Tests.EditMode
{
    public sealed class CombatFoundationStateTests
    {
        [Test]
        public void CombatVector3_RequiresFiniteComponentsAndReportsMagnitude()
        {
            CombatVector3 value = new CombatVector3(3f, 4f, 12f);

            Assert.That(value.SqrMagnitude, Is.EqualTo(169f));
            Assert.That(value.Magnitude, Is.EqualTo(13f));
            Assert.That(CombatVector3.Zero.IsZero, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CombatVector3(float.NaN, 0f, 0f)
            );
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CombatVector3(0f, float.PositiveInfinity, 0f)
            );
        }

        [Test]
        public void DamageInfo_PreservesTransactionDataAndRejectsInvalidValues()
        {
            object source = new object();
            CombatVector3 position = new CombatVector3(1f, 2f, 3f);
            CombatVector3 direction = new CombatVector3(0f, 0f, 1f);
            DamageInfo info = new DamageInfo(
                source,
                CombatFaction.Player,
                DamageType.Explosive,
                75f,
                position,
                direction,
                isCritical: true
            );

            Assert.That(info.Source, Is.SameAs(source));
            Assert.That(info.Faction, Is.EqualTo(CombatFaction.Player));
            Assert.That(info.DamageType, Is.EqualTo(DamageType.Explosive));
            Assert.That(info.Amount, Is.EqualTo(75f));
            Assert.That(info.Position, Is.EqualTo(position));
            Assert.That(info.Direction, Is.EqualTo(direction));
            Assert.That(info.IsCritical, Is.True);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new DamageInfo(
                    source,
                    CombatFaction.Player,
                    -1f,
                    position,
                    direction
                )
            );
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new DamageInfo(
                    source,
                    CombatFaction.Player,
                    float.NaN,
                    position,
                    direction
                )
            );
        }

        [Test]
        public void DamageResult_DistinguishesIgnoredDamageAndKills()
        {
            DamageResult ignored = DamageResult.Ignored;
            DamageResult killed = DamageResult.Applied(60f, wasKilled: true);

            Assert.That(ignored.WasApplied, Is.False);
            Assert.That(ignored.AppliedAmount, Is.Zero);
            Assert.That(ignored.WasKilled, Is.False);
            Assert.That(killed.WasApplied, Is.True);
            Assert.That(killed.AppliedAmount, Is.EqualTo(60f));
            Assert.That(killed.WasKilled, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DamageResult.Applied(float.PositiveInfinity, false)
            );
        }

        [Test]
        public void FactionPolicy_BlocksFriendlyAndUnassignedDamageByDefault()
        {
            Assert.That(
                CombatFactionPolicy.CanDamage(
                    CombatFaction.Player,
                    CombatFaction.Enemy
                ),
                Is.True
            );
            Assert.That(
                CombatFactionPolicy.CanDamage(
                    CombatFaction.Player,
                    CombatFaction.Player
                ),
                Is.False
            );
            Assert.That(
                CombatFactionPolicy.CanDamage(
                    CombatFaction.Player,
                    CombatFaction.Player,
                    allowFriendlyFire: true
                ),
                Is.True
            );
            Assert.That(
                CombatFactionPolicy.CanDamage(
                    CombatFaction.None,
                    CombatFaction.Player
                ),
                Is.False
            );
            Assert.That(
                CombatFactionPolicy.CanDamage(
                    CombatFaction.None,
                    CombatFaction.Player,
                    allowUnassigned: true
                ),
                Is.True
            );
            Assert.That(
                CombatFactionPolicy.CanDamage(
                    CombatFaction.None,
                    CombatFaction.Neutral
                ),
                Is.False
            );
            Assert.That(
                CombatFactionPolicy.CanDamage(
                    CombatFaction.None,
                    CombatFaction.Player,
                    allowFriendlyFire: true
                ),
                Is.False
            );
            Assert.That(
                CombatFactionPolicy.CanDamage(
                    CombatFaction.Neutral,
                    CombatFaction.Enemy
                ),
                Is.True
            );
            Assert.That(
                CombatFactionPolicy.CanDamage(
                    (CombatFaction)999,
                    CombatFaction.Enemy
                ),
                Is.False
            );
        }

        [Test]
        public void AbilityCooldown_ConsumesAdvancesClampsAndResets()
        {
            AbilityCooldownState cooldown = new AbilityCooldownState(
                durationSeconds: 5f,
                initialRemainingSeconds: 99f
            );

            Assert.That(cooldown.RemainingSeconds, Is.EqualTo(5f));
            Assert.That(cooldown.IsReady, Is.False);
            Assert.That(cooldown.TryConsume(), Is.False);

            cooldown.Advance(2f);
            Assert.That(cooldown.RemainingSeconds, Is.EqualTo(3f));
            Assert.That(cooldown.NormalizedRemaining, Is.EqualTo(0.6f).Within(0.0001f));

            cooldown.Advance(10f);
            Assert.That(cooldown.IsReady, Is.True);
            Assert.That(cooldown.TryConsume(), Is.True);
            Assert.That(cooldown.RemainingSeconds, Is.EqualTo(5f));

            cooldown.Reset(20f);
            Assert.That(cooldown.RemainingSeconds, Is.EqualTo(5f));
            cooldown.Reset();
            Assert.That(cooldown.IsReady, Is.True);

            Assert.Throws<ArgumentOutOfRangeException>(() => cooldown.Advance(-1f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AbilityCooldownState(float.NaN)
            );
        }

        [Test]
        public void ZeroDurationCooldown_RemainsImmediatelyReusable()
        {
            AbilityCooldownState cooldown = new AbilityCooldownState(0f);

            Assert.That(cooldown.TryConsume(), Is.True);
            Assert.That(cooldown.IsReady, Is.True);
            Assert.That(cooldown.NormalizedRemaining, Is.Zero);
        }

        [Test]
        public void UltimateMeter_GainsConsumesClampsFillsAndResets()
        {
            UltimateMeterState meter = new UltimateMeterState(100f, 150f);

            Assert.That(meter.CurrentValue, Is.EqualTo(100f));
            Assert.That(meter.IsFull, Is.True);
            Assert.That(meter.Gain(10f), Is.Zero);
            Assert.That(meter.TryConsume(60f), Is.True);
            Assert.That(meter.CurrentValue, Is.EqualTo(40f));
            Assert.That(meter.TryConsume(50f), Is.False);
            Assert.That(meter.CurrentValue, Is.EqualTo(40f));
            Assert.That(meter.Gain(80f), Is.EqualTo(60f));
            Assert.That(meter.NormalizedValue, Is.EqualTo(1f));

            meter.Reset(25f);
            Assert.That(meter.CurrentValue, Is.EqualTo(25f));
            meter.Fill();
            Assert.That(meter.IsFull, Is.True);
            meter.Reset();
            Assert.That(meter.IsEmpty, Is.True);

            Assert.Throws<ArgumentOutOfRangeException>(() => meter.Gain(-1f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new UltimateMeterState(0f)
            );
        }
    }
}
