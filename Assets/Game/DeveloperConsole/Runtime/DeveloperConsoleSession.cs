namespace Powersuit.DeveloperConsole
{
    /// <summary>
    /// UI-independent console session coordinating dispatch, history and a
    /// bounded transcript. A Unity overlay is only one possible presentation.
    /// </summary>
    public sealed class DeveloperConsoleSession
    {
        public DeveloperConsoleSession(
            ConsoleCommandRegistry registry,
            int historyCapacity = 64,
            int outputCapacity = 96
        )
        {
            Registry = registry ?? throw new System.ArgumentNullException(nameof(registry));
            History = new ConsoleCommandHistory(historyCapacity);
            Output = new ConsoleOutputBuffer(outputCapacity);
        }

        public ConsoleCommandRegistry Registry { get; }
        public ConsoleCommandHistory History { get; }
        public ConsoleOutputBuffer Output { get; }

        public ConsoleCommandResult Submit(string input)
        {
            string submitted = input?.Trim() ?? string.Empty;
            if (submitted.Length > 0)
            {
                History.Add(submitted);
                Output.Add($"> {submitted}", ConsoleMessageType.Information);
            }

            ConsoleCommandResult result = Registry.Execute(submitted);
            if (result.ClearOutput)
            {
                Output.Clear();
            }

            if (!string.IsNullOrEmpty(result.Message))
            {
                Output.Add(result.Message, result.MessageType);
            }

            return result;
        }
    }
}
