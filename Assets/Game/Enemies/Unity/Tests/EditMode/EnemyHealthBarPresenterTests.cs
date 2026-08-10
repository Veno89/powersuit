using System.Collections.Generic;
using NUnit.Framework;
using Powersuit.Combat;
using UnityEngine;

namespace Powersuit.Enemies.UnityAdapters.Tests
{
    public sealed class EnemyHealthBarPresenterTests
    {
        private readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = created.Count - 1; index >= 0; index--)
            {
                if (created[index] != null)
                {
                    Object.DestroyImmediate(created[index]);
                }
            }
            created.Clear();
        }

        [Test]
        public void Presenter_TracksDamageDeathAndPoolResetWithoutCanvas()
        {
            GameObject enemy = Create("Enemy");
            EnemyArchetypeController controller =
                enemy.AddComponent<EnemyArchetypeController>();
            Transform barRoot = Create("Bar", enemy.transform).transform;
            Transform fill = Create("Fill", barRoot).transform;
            fill.localScale = new Vector3(2f, 0.1f, 0.05f);

            EnemyHealthBarPresenter presenter =
                enemy.AddComponent<EnemyHealthBarPresenter>();
            presenter.Configure(controller, barRoot, fill, 55f);
            controller.Initialize(
                EnemyArchetypeCatalog.StationarySentry,
                explicitTarget: null
            );

            float halfDamage = controller.MaximumHealth * 0.5f;
            controller.ApplyDamage(
                new DamageInfo(
                    this,
                    CombatFaction.Player,
                    DamageType.Kinetic,
                    halfDamage,
                    CombatVector3.Zero,
                    CombatVector3.Zero
                )
            );

            Assert.That(fill.localScale.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(barRoot.gameObject.activeSelf, Is.True);

            controller.MarkDead();
            Assert.That(barRoot.gameObject.activeSelf, Is.False);

            controller.OnPoolRecycled();
            controller.OnPoolSpawned();
            Assert.That(controller.HealthFraction, Is.EqualTo(1f).Within(0.001f));
            Assert.That(fill.localScale.x, Is.EqualTo(2f).Within(0.001f));
            Assert.That(barRoot.gameObject.activeSelf, Is.True);
        }

        private GameObject Create(string name, Transform parent = null)
        {
            GameObject value = new GameObject(name);
            if (parent != null)
            {
                value.transform.SetParent(parent, false);
            }
            created.Add(value);
            return value;
        }
    }
}
