using System;
using System.Linq;
using NUnit.Framework;

namespace Powersuit.DeveloperConsole.Tests
{
    public sealed class DeveloperConsoleRuntimeTests
    {
        private sealed class FakeHost : IDeveloperConsoleHost
        {
            public bool ShowStatistics { get; set; }
            public float TimeScale { get; set; } = 1f;
        }

        [Test]
        public void Parser_SupportsWhitespaceQuotesEscapesAndEmptyArgument()
        {
            bool parsed = ConsoleCommandLineParser.TryParse(
                "  spawn  \"patrol rifleman\"  path\\ with\\ spaces \"\" ",
                out ConsoleCommandLine line,
                out string error
            );

            Assert.That(parsed, Is.True, error);
            Assert.That(line.CommandName, Is.EqualTo("spawn"));
            Assert.That(
                line.Arguments,
                Is.EqualTo(new[] { "patrol rifleman", "path with spaces", string.Empty })
            );
        }

        [TestCase("help \\", "unfinished escape")]
        [TestCase("help \"timescale", "closing quote")]
        [TestCase("   ", "Enter a command")]
        public void Parser_ReportsMalformedInputWithoutThrowing(
            string input,
            string expectedMessage
        )
        {
            bool parsed = ConsoleCommandLineParser.TryParse(
                input,
                out _,
                out string error
            );

            Assert.That(parsed, Is.False);
            Assert.That(error, Does.Contain(expectedMessage).IgnoreCase);
        }

        [Test]
        public void Registry_DispatchesCaseInsensitivelyAndProtectsAgainstExceptions()
        {
            var registry = new ConsoleCommandRegistry();
            registry.Register(
                "safe",
                "safe",
                "Succeeds.",
                arguments => ConsoleCommandResult.Success("done")
            );
            registry.Register(
                "throws",
                "throws",
                "Throws internally.",
                arguments => throw new InvalidOperationException("secret details")
            );

            Assert.That(registry.Execute("SAFE").Succeeded, Is.True);
            ConsoleCommandResult failed = registry.Execute("throws");
            Assert.That(failed.Succeeded, Is.False);
            Assert.That(failed.Message, Does.Contain("failed safely"));
            Assert.That(failed.Message, Does.Not.Contain("secret details"));
            Assert.That(registry.Execute("missing").Message, Does.Contain("Unknown command"));
        }

        [Test]
        public void Registry_RejectsDuplicateOrInvalidNames()
        {
            var registry = new ConsoleCommandRegistry();
            registry.Register(
                "player.hp",
                "player.hp",
                string.Empty,
                arguments => ConsoleCommandResult.Success(string.Empty)
            );

            Assert.Throws<InvalidOperationException>(() => registry.Register(
                "PLAYER.HP",
                "PLAYER.HP",
                string.Empty,
                arguments => ConsoleCommandResult.Success(string.Empty)
            ));
            Assert.Throws<ArgumentException>(() => registry.Register(
                "not valid",
                "not valid",
                string.Empty,
                arguments => ConsoleCommandResult.Success(string.Empty)
            ));
        }

        [TestCase("on", true)]
        [TestCase("TRUE", true)]
        [TestCase("1", true)]
        [TestCase("off", false)]
        [TestCase("False", false)]
        [TestCase("0", false)]
        public void BooleanRegistration_ParsesSupportedValues(string token, bool expected)
        {
            var registry = new ConsoleCommandRegistry();
            bool received = !expected;
            registry.RegisterBoolean(
                "feature",
                "Toggles a feature.",
                value =>
                {
                    received = value;
                    return ConsoleCommandResult.Success("set");
                }
            );

            ConsoleCommandResult result = registry.Execute($"feature {token}");

            Assert.That(result.Succeeded, Is.True);
            Assert.That(received, Is.EqualTo(expected));
        }

        [Test]
        public void TypedRegistration_ValidatesArityAndValue()
        {
            var registry = new ConsoleCommandRegistry();
            int received = -1;
            registry.RegisterValue<int>(
                "seed",
                "integer",
                "Sets a seed.",
                ConsoleCommandRegistry.TryParseInteger,
                value =>
                {
                    received = value;
                    return ConsoleCommandResult.Success("seeded");
                }
            );

            Assert.That(registry.Execute("seed").Succeeded, Is.False);
            Assert.That(registry.Execute("seed 1 2").Succeeded, Is.False);
            Assert.That(registry.Execute("seed nope").Succeeded, Is.False);
            Assert.That(registry.Execute("seed -42").Succeeded, Is.True);
            Assert.That(received, Is.EqualTo(-42));
        }

        [Test]
        public void ClampedFloat_UsesInvariantFiniteValuesAndReportsClamping()
        {
            var registry = new ConsoleCommandRegistry();
            float received = -1f;
            registry.RegisterClampedFloat(
                "timescale",
                "value",
                "Sets time scale.",
                0f,
                4f,
                value =>
                {
                    received = value;
                    return ConsoleCommandResult.Success($"set {value}");
                }
            );

            ConsoleCommandResult clamped = registry.Execute("timescale 999");
            Assert.That(clamped.Succeeded, Is.True);
            Assert.That(received, Is.EqualTo(4f));
            Assert.That(clamped.Message, Does.Contain("clamped"));

            Assert.That(registry.Execute("timescale NaN").Succeeded, Is.False);
            Assert.That(registry.Execute("timescale Infinity").Succeeded, Is.False);
            Assert.That(registry.Execute("timescale 1,5").Succeeded, Is.False);
        }

        [Test]
        public void ClampedInteger_ClampsBothEnds()
        {
            var registry = new ConsoleCommandRegistry();
            int received = 0;
            registry.RegisterClampedInteger(
                "enemy.cap",
                "count",
                "Sets cap.",
                1,
                100,
                value =>
                {
                    received = value;
                    return ConsoleCommandResult.Success("set");
                }
            );

            Assert.That(registry.Execute("enemy.cap -20").Succeeded, Is.True);
            Assert.That(received, Is.EqualTo(1));
            Assert.That(registry.Execute("enemy.cap 999").Succeeded, Is.True);
            Assert.That(received, Is.EqualTo(100));
        }

        [Test]
        public void BuiltIns_ExposeHelpAndApplyHostSettings()
        {
            var registry = new ConsoleCommandRegistry();
            var host = new FakeHost();
            DeveloperConsoleBuiltIns.Register(registry, host);

            ConsoleCommandResult help = registry.Execute("help");
            Assert.That(help.Succeeded, Is.True);
            Assert.That(help.Message, Does.Contain("showstats <on|off>"));
            Assert.That(help.Message, Does.Contain("timescale <value>"));

            Assert.That(registry.Execute("showstats ON").Succeeded, Is.True);
            Assert.That(host.ShowStatistics, Is.True);
            Assert.That(registry.Execute("timescale -2").Succeeded, Is.True);
            Assert.That(host.TimeScale, Is.EqualTo(0f));
        }

        [Test]
        public void ClearCommand_ClearsTranscriptThenLeavesConfirmation()
        {
            var registry = new ConsoleCommandRegistry();
            DeveloperConsoleBuiltIns.Register(registry, new FakeHost());
            var session = new DeveloperConsoleSession(registry, 8, 8);

            session.Submit("help timescale");
            Assert.That(session.Output.Count, Is.GreaterThan(1));
            session.Submit("clear");

            Assert.That(session.Output.Count, Is.EqualTo(1));
            Assert.That(session.Output.GetAt(0).Text, Is.EqualTo("Console cleared."));
            Assert.That(session.Output.GetAt(0).MessageType, Is.EqualTo(ConsoleMessageType.Success));
        }

        [Test]
        public void History_IsBoundedDeduplicatedAndRestoresDraft()
        {
            var history = new ConsoleCommandHistory(3);
            history.Add("one");
            history.Add("two");
            history.Add("two");
            history.Add("three");
            history.Add("four");

            Assert.That(history.Count, Is.EqualTo(3));
            Assert.That(
                Enumerable.Range(0, history.Count).Select(history.GetAt),
                Is.EqualTo(new[] { "two", "three", "four" })
            );

            Assert.That(history.TryPrevious("draft", out string command), Is.True);
            Assert.That(command, Is.EqualTo("four"));
            history.TryPrevious(command, out command);
            Assert.That(command, Is.EqualTo("three"));
            history.TryPrevious(command, out command);
            Assert.That(command, Is.EqualTo("two"));
            history.TryPrevious(command, out command);
            Assert.That(command, Is.EqualTo("two"));

            history.TryNext(out command);
            history.TryNext(out command);
            Assert.That(command, Is.EqualTo("four"));
            history.TryNext(out command);
            Assert.That(command, Is.EqualTo("draft"));
        }

        [Test]
        public void OutputBuffer_ReusesCapacityInChronologicalOrder()
        {
            var output = new ConsoleOutputBuffer(2);
            output.Add("one", ConsoleMessageType.Information);
            output.Add("two", ConsoleMessageType.Success);
            output.Add("three", ConsoleMessageType.Error);

            Assert.That(output.Count, Is.EqualTo(2));
            Assert.That(output.GetAt(0).Text, Is.EqualTo("two"));
            Assert.That(output.GetAt(1).Text, Is.EqualTo("three"));
            Assert.That(output.GetAt(1).Sequence, Is.GreaterThan(output.GetAt(0).Sequence));
        }
    }
}
