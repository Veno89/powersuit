using System;

namespace Powersuit.DeveloperConsole
{
    public enum ConsoleMessageType
    {
        Information = 0,
        Success = 1,
        Error = 2
    }

    /// <summary>
    /// Immutable outcome returned by every console command. ClearOutput is an
    /// explicit side effect so the command registry stays independent of any UI.
    /// </summary>
    public readonly struct ConsoleCommandResult
    {
        private ConsoleCommandResult(
            bool succeeded,
            string message,
            ConsoleMessageType messageType,
            bool clearOutput
        )
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
            MessageType = messageType;
            ClearOutput = clearOutput;
        }

        public bool Succeeded { get; }
        public string Message { get; }
        public ConsoleMessageType MessageType { get; }
        public bool ClearOutput { get; }

        public static ConsoleCommandResult Success(string message)
        {
            return new ConsoleCommandResult(
                true,
                message,
                ConsoleMessageType.Success,
                false
            );
        }

        public static ConsoleCommandResult Information(string message)
        {
            return new ConsoleCommandResult(
                true,
                message,
                ConsoleMessageType.Information,
                false
            );
        }

        public static ConsoleCommandResult Error(string message)
        {
            return new ConsoleCommandResult(
                false,
                message,
                ConsoleMessageType.Error,
                false
            );
        }

        public static ConsoleCommandResult Clear(string message = "Console cleared.")
        {
            return new ConsoleCommandResult(
                true,
                message,
                ConsoleMessageType.Success,
                true
            );
        }

        internal ConsoleCommandResult WithMessage(string message)
        {
            return new ConsoleCommandResult(
                Succeeded,
                message,
                MessageType,
                ClearOutput
            );
        }
    }

    public readonly struct ConsoleOutputEntry
    {
        public ConsoleOutputEntry(
            long sequence,
            ConsoleMessageType messageType,
            string text
        )
        {
            Sequence = sequence;
            MessageType = messageType;
            Text = text ?? string.Empty;
        }

        public long Sequence { get; }
        public ConsoleMessageType MessageType { get; }
        public string Text { get; }
    }
}
