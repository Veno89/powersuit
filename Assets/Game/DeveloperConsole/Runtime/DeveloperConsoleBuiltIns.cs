using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Powersuit.DeveloperConsole
{
    /// <summary>
    /// Narrow host boundary for engine-specific tuning. Future gameplay command
    /// packs can define their own adapter interfaces without changing the core.
    /// </summary>
    public interface IDeveloperConsoleHost
    {
        bool ShowStatistics { get; set; }
        float TimeScale { get; set; }
    }

    public static class DeveloperConsoleBuiltIns
    {
        public const float MinimumTimeScale = 0f;
        public const float MaximumTimeScale = 4f;

        public static void Register(
            ConsoleCommandRegistry registry,
            IDeveloperConsoleHost host
        )
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            registry.Register(
                "help",
                "help [command]",
                "Lists available commands or explains one command.",
                arguments => BuildHelp(registry, arguments)
            );

            registry.Register(
                "clear",
                "clear",
                "Clears the console transcript.",
                arguments => arguments.Count == 0
                    ? ConsoleCommandResult.Clear()
                    : ConsoleCommandResult.Error("Usage: clear")
            );

            registry.RegisterBoolean(
                "showstats",
                "Shows or hides the compact developer statistics overlay.",
                enabled =>
                {
                    host.ShowStatistics = enabled;
                    return ConsoleCommandResult.Success(
                        $"Developer statistics {(enabled ? "enabled" : "disabled")}."
                    );
                }
            );

            registry.RegisterClampedFloat(
                "timescale",
                "value",
                $"Sets Unity time scale ({MinimumTimeScale:0.#} to {MaximumTimeScale:0.#}).",
                MinimumTimeScale,
                MaximumTimeScale,
                value =>
                {
                    host.TimeScale = value;
                    return ConsoleCommandResult.Success(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Time scale set to {0:0.###}.",
                            value
                        )
                    );
                }
            );
        }

        private static ConsoleCommandResult BuildHelp(
            ConsoleCommandRegistry registry,
            IReadOnlyList<string> arguments
        )
        {
            if (arguments.Count > 1)
            {
                return ConsoleCommandResult.Error("Usage: help [command]");
            }

            if (arguments.Count == 1)
            {
                if (!registry.TryGetCommand(arguments[0], out ConsoleCommandInfo command))
                {
                    return ConsoleCommandResult.Error(
                        $"Unknown command '{arguments[0]}'."
                    );
                }

                return ConsoleCommandResult.Information(
                    $"{command.Usage}\n  {command.Description}"
                );
            }

            var builder = new StringBuilder(256);
            builder.Append("Available commands:");
            IReadOnlyList<ConsoleCommandInfo> commands = registry.Commands;
            for (int index = 0; index < commands.Count; index++)
            {
                ConsoleCommandInfo command = commands[index];
                builder.Append('\n');
                builder.Append("  ");
                builder.Append(command.Usage);
                if (!string.IsNullOrEmpty(command.Description))
                {
                    builder.Append(" - ");
                    builder.Append(command.Description);
                }
            }

            return ConsoleCommandResult.Information(builder.ToString());
        }
    }
}
