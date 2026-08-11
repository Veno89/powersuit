using NUnit.Framework;
using Powersuit.Core;

namespace Powersuit.Tests.EditMode
{
    public sealed class PowerSuitPerformanceSoakTests
    {
        [Test]
        public void Accumulator_ProducesDeterministicDistribution()
        {
            PerformanceSampleAccumulator samples = new(8);
            samples.Add(8d);
            samples.Add(2d);
            samples.Add(6d);
            samples.Add(4d);

            PerformanceSampleSummary summary = samples.CreateSummary();

            Assert.That(summary.Count, Is.EqualTo(4));
            Assert.That(summary.Average, Is.EqualTo(5d));
            Assert.That(summary.Maximum, Is.EqualTo(8d));
            Assert.That(summary.Percentile50, Is.EqualTo(5d));
            Assert.That(summary.Percentile95, Is.EqualTo(7.7d).Within(0.0001d));
            Assert.That(summary.Percentile99, Is.EqualTo(7.94d).Within(0.0001d));
        }

        [Test]
        public void Accumulator_IsFixedCapacityAndRejectsInvalidSamples()
        {
            PerformanceSampleAccumulator samples = new(2);
            Assert.That(samples.Add(double.NaN), Is.False);
            Assert.That(samples.Add(-1d), Is.False);
            Assert.That(samples.Add(1d), Is.True);
            Assert.That(samples.Add(2d), Is.True);
            Assert.That(samples.Add(3d), Is.False);

            PerformanceSampleSummary summary = samples.CreateSummary();
            Assert.That(summary.Count, Is.EqualTo(2));
            Assert.That(summary.DroppedCount, Is.EqualTo(1));
        }

        [Test]
        public void Options_StayDisabledWithoutExplicitFlag()
        {
            Assert.That(
                PerformanceSoakOptions.TryParse(
                    new[] { "game.exe" },
                    out PerformanceSoakOptions options,
                    out string error
                ),
                Is.True
            );
            Assert.That(error, Is.Empty);
            Assert.That(options.Enabled, Is.False);
        }

        [Test]
        public void Options_ParseBoundedStressConfiguration()
        {
            string[] arguments =
            {
                "game.exe",
                "-powersuit-soak",
                "-powersuit-soak-duration=90",
                "-powersuit-soak-warmup", "12",
                "-powersuit-soak-enemies", "40",
                "-powersuit-soak-fps", "120",
                "-powersuit-soak-output", "report.json",
                "-powersuit-soak-exit"
            };

            Assert.That(
                PerformanceSoakOptions.TryParse(arguments, out PerformanceSoakOptions options, out string error),
                Is.True,
                error
            );
            Assert.That(options.Enabled, Is.True);
            Assert.That(options.DurationSeconds, Is.EqualTo(90));
            Assert.That(options.WarmupSeconds, Is.EqualTo(12));
            Assert.That(options.EnemyCap, Is.EqualTo(40));
            Assert.That(options.TargetFrameRate, Is.EqualTo(120));
            Assert.That(options.OutputPath, Is.EqualTo("report.json"));
            Assert.That(options.ExitWhenFinished, Is.True);
        }

        [TestCase("-powersuit-soak-duration", "9")]
        [TestCase("-powersuit-soak-warmup", "0")]
        [TestCase("-powersuit-soak-enemies", "129")]
        [TestCase("-powersuit-soak-fps", "241")]
        public void Options_RejectOutOfRangeValues(string name, string value)
        {
            Assert.That(
                PerformanceSoakOptions.TryParse(
                    new[] { "game.exe", "-powersuit-soak", name, value },
                    out _,
                    out string error
                ),
                Is.False
            );
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void Options_RejectWarmupThatConsumesMeasuredDuration()
        {
            Assert.That(
                PerformanceSoakOptions.TryParse(
                    new[]
                    {
                        "game.exe",
                        "-powersuit-soak",
                        "-powersuit-soak-duration", "10",
                        "-powersuit-soak-warmup", "10"
                    },
                    out _,
                    out string error
                ),
                Is.False
            );
            Assert.That(error, Does.Contain("shorter"));
        }
    }
}
