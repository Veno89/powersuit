using System;
using System.Collections.Generic;
using System.Globalization;

namespace Powersuit.DeveloperConsole
{
    public delegate ConsoleCommandResult ConsoleCommandHandler(
        IReadOnlyList<string> arguments
    );

    public delegate bool ConsoleValueParser<T>(string token, out T value);

    public readonly struct ConsoleCommandInfo
    {
        public ConsoleCommandInfo(string name, string usage, string description)
        {
            Name = name;
            Usage = usage;
            Description = description;
        }

        public string Name { get; }
        public string Usage { get; }
        public string Description { get; }
    }

    /// <summary>
    /// Extensible registry for independent commands. Typed helpers centralize
    /// validation so malformed input cannot leak into gameplay adapters.
    /// </summary>
    public sealed class ConsoleCommandRegistry
    {
        private sealed class RegisteredCommand
        {
            public RegisteredCommand(
                ConsoleCommandInfo info,
                ConsoleCommandHandler handler
            )
            {
                Info = info;
                Handler = handler;
            }

            public ConsoleCommandInfo Info { get; }
            public ConsoleCommandHandler Handler { get; }
        }

        private readonly Dictionary<string, RegisteredCommand> byName =
            new Dictionary<string, RegisteredCommand>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ConsoleCommandInfo> commandInfo =
            new List<ConsoleCommandInfo>();

        public int Count => commandInfo.Count;
        public IReadOnlyList<ConsoleCommandInfo> Commands => commandInfo;

        public void Register(
            string name,
            string usage,
            string description,
            ConsoleCommandHandler handler
        )
        {
            ValidateCommandName(name);
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (byName.ContainsKey(name))
            {
                throw new InvalidOperationException(
                    $"A console command named '{name}' is already registered."
                );
            }

            var info = new ConsoleCommandInfo(
                name,
                string.IsNullOrWhiteSpace(usage) ? name : usage.Trim(),
                description?.Trim() ?? string.Empty
            );
            byName.Add(name, new RegisteredCommand(info, handler));
            commandInfo.Add(info);
        }

        public void RegisterValue<T>(
            string name,
            string valueName,
            string description,
            ConsoleValueParser<T> parser,
            Func<T, ConsoleCommandResult> handler
        )
        {
            if (parser == null)
            {
                throw new ArgumentNullException(nameof(parser));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            string safeValueName = string.IsNullOrWhiteSpace(valueName)
                ? "value"
                : valueName.Trim();

            Register(
                name,
                $"{name} <{safeValueName}>",
                description,
                arguments =>
                {
                    if (arguments.Count != 1)
                    {
                        return ConsoleCommandResult.Error(
                            $"Usage: {name} <{safeValueName}>"
                        );
                    }

                    if (!parser(arguments[0], out T value))
                    {
                        return ConsoleCommandResult.Error(
                            $"'{arguments[0]}' is not a valid {safeValueName}."
                        );
                    }

                    return handler(value);
                }
            );
        }

        public void RegisterBoolean(
            string name,
            string description,
            Func<bool, ConsoleCommandResult> handler
        )
        {
            RegisterValue<bool>(
                name,
                "on|off",
                description,
                TryParseBoolean,
                handler
            );
        }

        public void RegisterClampedFloat(
            string name,
            string valueName,
            string description,
            float minimum,
            float maximum,
            Func<float, ConsoleCommandResult> handler
        )
        {
            RequireFiniteRange(minimum, maximum);
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            RegisterValue<float>(
                name,
                valueName,
                description,
                TryParseFiniteFloat,
                requested =>
                {
                    float clamped = Clamp(requested, minimum, maximum);
                    ConsoleCommandResult result = handler(clamped);
                    if (!result.Succeeded || requested == clamped)
                    {
                        return result;
                    }

                    string clampNotice = string.Format(
                        CultureInfo.InvariantCulture,
                        "Value {0:0.###} was clamped to {1:0.###}.",
                        requested,
                        clamped
                    );
                    return result.WithMessage(
                        string.IsNullOrWhiteSpace(result.Message)
                            ? clampNotice
                            : $"{clampNotice} {result.Message}"
                    );
                }
            );
        }

        public void RegisterClampedInteger(
            string name,
            string valueName,
            string description,
            int minimum,
            int maximum,
            Func<int, ConsoleCommandResult> handler
        )
        {
            if (minimum > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(minimum));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            RegisterValue<int>(
                name,
                valueName,
                description,
                TryParseInteger,
                requested =>
                {
                    int clamped = Math.Max(minimum, Math.Min(maximum, requested));
                    ConsoleCommandResult result = handler(clamped);
                    if (!result.Succeeded || requested == clamped)
                    {
                        return result;
                    }

                    string clampNotice = string.Format(
                        CultureInfo.InvariantCulture,
                        "Value {0} was clamped to {1}.",
                        requested,
                        clamped
                    );
                    return result.WithMessage(
                        string.IsNullOrWhiteSpace(result.Message)
                            ? clampNotice
                            : $"{clampNotice} {result.Message}"
                    );
                }
            );
        }

        public ConsoleCommandResult Execute(string input)
        {
            if (!ConsoleCommandLineParser.TryParse(input, out ConsoleCommandLine line, out string error))
            {
                return ConsoleCommandResult.Error(error);
            }

            if (!byName.TryGetValue(line.CommandName, out RegisteredCommand command))
            {
                return ConsoleCommandResult.Error(
                    $"Unknown command '{line.CommandName}'. Enter 'help' to list commands."
                );
            }

            try
            {
                return command.Handler(line.Arguments);
            }
            catch (Exception exception)
            {
                return ConsoleCommandResult.Error(
                    $"Command '{command.Info.Name}' failed safely ({exception.GetType().Name})."
                );
            }
        }

        public bool TryGetCommand(string name, out ConsoleCommandInfo info)
        {
            if (!string.IsNullOrWhiteSpace(name) && byName.TryGetValue(name, out RegisteredCommand command))
            {
                info = command.Info;
                return true;
            }

            info = default;
            return false;
        }

        public static bool TryParseBoolean(string token, out bool value)
        {
            if (
                string.Equals(token, "on", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "1", StringComparison.Ordinal)
            )
            {
                value = true;
                return true;
            }

            if (
                string.Equals(token, "off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "0", StringComparison.Ordinal)
            )
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        public static bool TryParseFiniteFloat(string token, out float value)
        {
            bool parsed = float.TryParse(
                token,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value
            );
            return parsed && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool TryParseInteger(string token, out int value)
        {
            return int.TryParse(
                token,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            );
        }

        private static void ValidateCommandName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A command name is required.", nameof(name));
            }

            for (int index = 0; index < name.Length; index++)
            {
                char character = name[index];
                if (
                    !char.IsLetterOrDigit(character) &&
                    character != '.' &&
                    character != '_' &&
                    character != '-'
                )
                {
                    throw new ArgumentException(
                        "Command names may only contain letters, numbers, '.', '_' and '-'.",
                        nameof(name)
                    );
                }
            }
        }

        private static void RequireFiniteRange(float minimum, float maximum)
        {
            if (
                float.IsNaN(minimum) ||
                float.IsInfinity(minimum) ||
                float.IsNaN(maximum) ||
                float.IsInfinity(maximum) ||
                minimum > maximum
            )
            {
                throw new ArgumentOutOfRangeException(nameof(minimum));
            }
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
