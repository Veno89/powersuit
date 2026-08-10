using System;
using System.Collections.Generic;
using System.Text;

namespace Powersuit.DeveloperConsole
{
    public readonly struct ConsoleCommandLine
    {
        public ConsoleCommandLine(string commandName, string[] arguments)
        {
            CommandName = commandName ?? string.Empty;
            Arguments = arguments ?? Array.Empty<string>();
        }

        public string CommandName { get; }
        public IReadOnlyList<string> Arguments { get; }
    }

    /// <summary>
    /// Small command-line tokenizer with quoted arguments and backslash escapes.
    /// Parsing only occurs when a command is submitted, never in a frame loop.
    /// </summary>
    public static class ConsoleCommandLineParser
    {
        public static bool TryParse(
            string input,
            out ConsoleCommandLine commandLine,
            out string error
        )
        {
            commandLine = default;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Enter a command.";
                return false;
            }

            var tokens = new List<string>(4);
            var token = new StringBuilder(input.Length);
            bool insideQuotes = false;
            bool escaping = false;
            bool tokenStarted = false;

            for (int index = 0; index < input.Length; index++)
            {
                char character = input[index];

                if (escaping)
                {
                    token.Append(character);
                    tokenStarted = true;
                    escaping = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaping = true;
                    tokenStarted = true;
                    continue;
                }

                if (character == '"')
                {
                    insideQuotes = !insideQuotes;
                    tokenStarted = true;
                    continue;
                }

                if (char.IsWhiteSpace(character) && !insideQuotes)
                {
                    FlushToken(tokens, token, ref tokenStarted);
                    continue;
                }

                token.Append(character);
                tokenStarted = true;
            }

            if (escaping)
            {
                error = "A command cannot end with an unfinished escape character.";
                return false;
            }

            if (insideQuotes)
            {
                error = "A quoted argument is missing its closing quote.";
                return false;
            }

            FlushToken(tokens, token, ref tokenStarted);
            if (tokens.Count == 0)
            {
                error = "Enter a command.";
                return false;
            }

            string[] arguments;
            if (tokens.Count == 1)
            {
                arguments = Array.Empty<string>();
            }
            else
            {
                arguments = new string[tokens.Count - 1];
                tokens.CopyTo(1, arguments, 0, arguments.Length);
            }

            commandLine = new ConsoleCommandLine(tokens[0], arguments);
            return true;
        }

        private static void FlushToken(
            ICollection<string> tokens,
            StringBuilder token,
            ref bool tokenStarted
        )
        {
            if (!tokenStarted)
            {
                return;
            }

            tokens.Add(token.ToString());
            token.Clear();
            tokenStarted = false;
        }
    }
}
