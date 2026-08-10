using NUnit.Framework;
using Powersuit.Combat;
using UnityEngine;

namespace Powersuit.Abilities.UnityAdapters.Tests
{
    public sealed class AbilityAdapterTests
    {
        [Test]
        public void AreaEffect_AppliesDamageAndFactionSafeExternalForce()
        {
            object source = new object();
            AbilityAreaEffect effect = new AbilityAreaEffect(
                source,
                CombatFaction.Player,
                DamageType.Void,
                Vector3.zero,
                Vector3.up,
                radius: 10f,
                damage: 100f,
                minimumDamageMultiplier: 0.5f,
                forceMode: AbilityExternalForceMode.Pull,
                forceMagnitude: 12f
            );
            FakeReceiver enemy = new FakeReceiver(CombatFaction.Enemy);
            FakeReceiver player = new FakeReceiver(CombatFaction.Player);

            DamageResult damage = effect.ApplyDamage(
                enemy,
                new Vector3(5f, 0f, 0f)
            );
            bool pulled = effect.ApplyExternalForce(
                enemy,
                new Vector3(5f, 0f, 0f)
            );

            Assert.That(damage.WasApplied, Is.True);
            Assert.That(damage.AppliedAmount, Is.EqualTo(75f).Within(0.001f));
            Assert.That(enemy.LastDamage.DamageType, Is.EqualTo(DamageType.Void));
            Assert.That(enemy.LastForce.X, Is.EqualTo(-12f).Within(0.001f));
            Assert.That(enemy.LastForceSource, Is.SameAs(source));
            Assert.That(pulled, Is.True);
            Assert.That(
                effect.ApplyDamage(player, new Vector3(1f, 0f, 0f)).WasApplied,
                Is.False
            );
            Assert.That(
                effect.ApplyExternalForce(
                    player,
                    new Vector3(1f, 0f, 0f)
                ),
                Is.False
            );
        }

        [Test]
        public void ShoulderAdapter_EmitsLaunchAndExplosionContract()
        {
            GameObject host = new GameObject("Shoulder Rocket Adapter Test");
            try
            {
                host.transform.position = new Vector3(1f, 2f, 3f);
                ShoulderRocketAbility ability =
                    host.AddComponent<ShoulderRocketAbility>();
                ShoulderRocketLaunchCommand command = default;
                int launchCount = 0;
                ability.LaunchRequested += value =>
                {
                    command = value;
                    launchCount++;
                };

                Assert.That(
                    ability.TryLaunch(new Vector3(1f, 2f, 13f)),
                    Is.True
                );
                Assert.That(launchCount, Is.EqualTo(1));
                Assert.That(command.Direction, Is.EqualTo(Vector3.forward));
                Assert.That(command.ProjectileSpeed, Is.GreaterThan(0f));
                Assert.That(
                    ability.TryLaunch(new Vector3(1f, 2f, 13f)),
                    Is.False
                );

                AbilityAreaEffect explosion = command.CreateExplosion(
                    new Vector3(1f, 2f, 13f),
                    Vector3.up
                );
                DamageInfo damage = explosion.CreateDamageInfo(
                    new Vector3(1f, 2f, 13f)
                );
                Assert.That(damage.DamageType, Is.EqualTo(DamageType.Explosive));
                Assert.That(damage.Faction, Is.EqualTo(CombatFaction.Player));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void LightningAdapter_EmitsOnlyValidReleaseAndCancelsInvalidRelease()
        {
            GameObject host = new GameObject("Lightning Adapter Test");
            try
            {
                LightningStrikeAbility ability =
                    host.AddComponent<LightningStrikeAbility>();
                int casts = 0;
                int cancellations = 0;
                LightningAreaCastCommand command = default;
                ability.CastRequested += value =>
                {
                    command = value;
                    casts++;
                };
                ability.TargetingCancelled += () => cancellations++;

                Assert.That(ability.TryBeginTargeting(), Is.True);
                ability.UpdateTarget(
                    Vector3.zero,
                    Vector3.forward * 5f,
                    Vector3.up,
                    hasSurface: true,
                    isObstructed: true
                );
                Assert.That(ability.ReleaseTargeting(), Is.False);
                Assert.That(cancellations, Is.EqualTo(1));
                Assert.That(ability.CooldownRemaining, Is.Zero);

                Assert.That(ability.TryBeginTargeting(), Is.True);
                Assert.That(
                    ability.UpdateTarget(
                        Vector3.zero,
                        Vector3.forward * 5f,
                        Vector3.up,
                        hasSurface: true,
                        isObstructed: false
                    ).IsValid,
                    Is.True
                );
                Assert.That(ability.ReleaseTargeting(), Is.True);
                Assert.That(casts, Is.EqualTo(1));
                Assert.That(command.Effect.DamageType, Is.EqualTo(DamageType.Lightning));
                Assert.That(command.Radius, Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void VoidAdapter_EmitsTicksThenFinalBurstAndCompletion()
        {
            GameObject host = new GameObject("Void Ultimate Adapter Test");
            try
            {
                VoidUltimateAbility ability =
                    host.AddComponent<VoidUltimateAbility>();
                int activations = 0;
                int ticks = 0;
                int bursts = 0;
                int completions = 0;
                VoidUltimateTickCommand lastTick = default;
                VoidUltimateBurstCommand burst = default;
                ability.Activated += _ => activations++;
                ability.TickRequested += value =>
                {
                    ticks++;
                    lastTick = value;
                };
                ability.FinalBurstRequested += value =>
                {
                    bursts++;
                    burst = value;
                };
                ability.Completed += () => completions++;

                ability.FillMeter();
                Assert.That(
                    ability.TryActivate(
                        Vector3.zero,
                        Vector3.forward * 5f,
                        Vector3.up,
                        hasSurface: true,
                        isObstructed: false
                    ),
                    Is.True
                );
                VoidAdvanceResult result = ability.AdvanceAbility(100f);

                Assert.That(activations, Is.EqualTo(1));
                Assert.That(result.FinalBurstTriggered, Is.True);
                Assert.That(ticks, Is.GreaterThan(0));
                Assert.That(lastTick.Sequence, Is.EqualTo(ticks));
                Assert.That(
                    lastTick.Effect.ForceMode,
                    Is.EqualTo(AbilityExternalForceMode.Pull)
                );
                Assert.That(bursts, Is.EqualTo(1));
                Assert.That(
                    burst.Effect.ForceMode,
                    Is.EqualTo(AbilityExternalForceMode.Push)
                );
                Assert.That(completions, Is.EqualTo(1));
                Assert.That(ability.IsActive, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AreaExecutor_DeduplicatesMultipleCollidersPerReceiverRoot()
        {
            const int TestLayer = 31;
            GameObject enemyRoot = new GameObject("Area Executor Enemy");
            GameObject firstColliderObject = new GameObject("First Collider");
            GameObject secondColliderObject = new GameObject("Second Collider");
            try
            {
                enemyRoot.layer = TestLayer;
                firstColliderObject.layer = TestLayer;
                secondColliderObject.layer = TestLayer;
                AbilityExecutorTestReceiver receiver =
                    enemyRoot.AddComponent<AbilityExecutorTestReceiver>();
                firstColliderObject.transform.SetParent(
                    enemyRoot.transform,
                    worldPositionStays: false
                );
                secondColliderObject.transform.SetParent(
                    enemyRoot.transform,
                    worldPositionStays: false
                );
                firstColliderObject.transform.localPosition = Vector3.left;
                secondColliderObject.transform.localPosition = Vector3.right;
                firstColliderObject.AddComponent<SphereCollider>().radius = 1f;
                secondColliderObject.AddComponent<SphereCollider>().radius = 1f;

                Physics.SyncTransforms();
                AbilityAreaEffect effect = new AbilityAreaEffect(
                    source: this,
                    sourceFaction: CombatFaction.Player,
                    damageType: DamageType.Explosive,
                    center: Vector3.zero,
                    surfaceNormal: Vector3.up,
                    radius: 5f,
                    damage: 50f,
                    minimumDamageMultiplier: 1f,
                    forceMode: AbilityExternalForceMode.Push,
                    forceMagnitude: 8f
                );
                AbilityAreaEffectExecutor executor =
                    new AbilityAreaEffectExecutor(capacity: 8);

                AbilityAreaEffectExecutionResult result = executor.Execute(
                    effect,
                    layerMask: 1 << TestLayer
                );

                Assert.That(result.ColliderCount, Is.EqualTo(2));
                Assert.That(result.LogicalTargetCount, Is.EqualTo(1));
                Assert.That(result.DamagedTargetCount, Is.EqualTo(1));
                Assert.That(result.ForcedTargetCount, Is.EqualTo(1));
                Assert.That(result.QueryCapacityReached, Is.False);
                Assert.That(receiver.DamageCallCount, Is.EqualTo(1));
                Assert.That(receiver.ForceCallCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(enemyRoot);
            }
        }

        [Test]
        public void AreaExecutor_ReportsCapacityAndRejectsInvalidCapacity()
        {
            const int TestLayer = 31;
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new AbilityAreaEffectExecutor(0)
            );

            GameObject root = new GameObject("Capacity Receiver");
            try
            {
                root.layer = TestLayer;
                root.AddComponent<AbilityExecutorTestReceiver>();
                root.AddComponent<SphereCollider>().radius = 1f;
                Physics.SyncTransforms();

                AbilityAreaEffect effect = new AbilityAreaEffect(
                    this,
                    CombatFaction.Player,
                    DamageType.Lightning,
                    Vector3.zero,
                    Vector3.up,
                    5f,
                    10f,
                    1f,
                    AbilityExternalForceMode.None,
                    0f
                );
                AbilityAreaEffectExecutionResult result =
                    new AbilityAreaEffectExecutor(1).Execute(
                        effect,
                        layerMask: 1 << TestLayer
                    );

                Assert.That(result.ColliderCount, Is.EqualTo(1));
                Assert.That(result.QueryCapacityReached, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeTuning_AbilityValuesClampAndCooldownBypassIsImmediate()
        {
            GameObject host = new GameObject("Ability Runtime Tuning Test");
            try
            {
                ShoulderRocketAbility rocket =
                    host.AddComponent<ShoulderRocketAbility>();
                LightningStrikeAbility lightning =
                    host.AddComponent<LightningStrikeAbility>();
                VoidUltimateAbility voidUltimate =
                    host.AddComponent<VoidUltimateAbility>();

                Assert.That(
                    rocket.SetExplosionDamage(float.PositiveInfinity),
                    Is.EqualTo(ShoulderRocketAbility.MaximumTunableDamage)
                );
                Assert.That(
                    rocket.SetExplosionRadius(float.NegativeInfinity),
                    Is.EqualTo(0.01f)
                );
                rocket.SetCooldownsEnabled(false);
                Assert.That(rocket.TryLaunch(Vector3.forward * 10f), Is.True);
                Assert.That(rocket.TryLaunch(Vector3.forward * 10f), Is.True);
                Assert.That(rocket.CooldownRemaining, Is.Zero);

                Assert.That(lightning.SetDamage(123f), Is.EqualTo(123f));
                Assert.That(
                    lightning.SetRadius(float.PositiveInfinity),
                    Is.EqualTo(LightningStrikeAbility.MaximumTunableRadius)
                );
                lightning.SetCooldownsEnabled(false);
                Assert.That(lightning.CooldownsEnabled, Is.False);

                Assert.That(voidUltimate.SetDamage(17f), Is.EqualTo(17f));
                Assert.That(voidUltimate.SetFinalDamage(190f), Is.EqualTo(190f));
                Assert.That(voidUltimate.SetRadius(9f), Is.EqualTo(9f));
                Assert.That(
                    voidUltimate.SetPullImpulsePerTick(float.PositiveInfinity),
                    Is.EqualTo(VoidUltimateAbility.MaximumTunableImpulse)
                );
                Assert.That(voidUltimate.TickDamage, Is.EqualTo(17f));
                Assert.That(voidUltimate.FinalDamage, Is.EqualTo(190f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private sealed class FakeReceiver : IDamageReceiver, IExternalForceReceiver
        {
            public FakeReceiver(CombatFaction faction)
            {
                Faction = faction;
            }

            public CombatFaction Faction { get; }
            public bool CanReceiveDamage => true;
            public bool CanReceiveExternalForce => true;
            public DamageInfo LastDamage { get; private set; }
            public CombatVector3 LastForce { get; private set; }
            public object LastForceSource { get; private set; }

            public DamageResult ApplyDamage(DamageInfo damage)
            {
                if (!CombatFactionPolicy.CanDamage(damage.Faction, Faction))
                {
                    return DamageResult.Ignored;
                }

                LastDamage = damage;
                return DamageResult.Applied(damage.Amount, false);
            }

            public void ApplyExternalForce(CombatVector3 force, object source)
            {
                LastForce = force;
                LastForceSource = source;
            }
        }
    }

    public sealed class AbilityExecutorTestReceiver :
        MonoBehaviour,
        IDamageReceiver,
        IExternalForceReceiver
    {
        public CombatFaction Faction => CombatFaction.Enemy;
        public bool CanReceiveDamage => true;
        public bool CanReceiveExternalForce => true;
        public int DamageCallCount { get; private set; }
        public int ForceCallCount { get; private set; }

        public DamageResult ApplyDamage(DamageInfo damage)
        {
            if (!CombatFactionPolicy.CanDamage(damage.Faction, Faction))
            {
                return DamageResult.Ignored;
            }

            DamageCallCount++;
            return DamageResult.Applied(damage.Amount, false);
        }

        public void ApplyExternalForce(CombatVector3 force, object source)
        {
            ForceCallCount++;
        }
    }
}
