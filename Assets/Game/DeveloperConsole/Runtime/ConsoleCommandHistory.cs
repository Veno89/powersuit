using System;

namespace Powersuit.DeveloperConsole
{
    /// <summary>
    /// Fixed-capacity history with shell-style previous/next navigation. A ring
    /// buffer keeps capacity enforcement allocation-free after construction.
    /// </summary>
    public sealed class ConsoleCommandHistory
    {
        private readonly string[] entries;
        private int start;
        private int count;
        private int navigationIndex;
        private string navigationDraft = string.Empty;

        public ConsoleCommandHistory(int capacity = 64)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            entries = new string[capacity];
        }

        public int Capacity => entries.Length;
        public int Count => count;

        public string GetAt(int chronologicalIndex)
        {
            if (chronologicalIndex < 0 || chronologicalIndex >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(chronologicalIndex));
            }

            return entries[PhysicalIndex(chronologicalIndex)];
        }

        public void Add(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                ResetNavigation();
                return;
            }

            string normalized = command.Trim();
            if (
                count > 0 &&
                string.Equals(GetAt(count - 1), normalized, StringComparison.Ordinal)
            )
            {
                ResetNavigation();
                return;
            }

            if (count < entries.Length)
            {
                entries[PhysicalIndex(count)] = normalized;
                count++;
            }
            else
            {
                entries[start] = normalized;
                start = (start + 1) % entries.Length;
            }

            ResetNavigation();
        }

        public bool TryPrevious(string currentInput, out string command)
        {
            if (count == 0)
            {
                command = currentInput ?? string.Empty;
                return false;
            }

            if (navigationIndex == count)
            {
                navigationDraft = currentInput ?? string.Empty;
            }

            if (navigationIndex > 0)
            {
                navigationIndex--;
            }

            command = GetAt(navigationIndex);
            return true;
        }

        public bool TryNext(out string command)
        {
            if (count == 0 || navigationIndex >= count)
            {
                command = navigationDraft;
                return false;
            }

            navigationIndex++;
            command = navigationIndex == count
                ? navigationDraft
                : GetAt(navigationIndex);
            return true;
        }

        public void ResetNavigation()
        {
            navigationIndex = count;
            navigationDraft = string.Empty;
        }

        private int PhysicalIndex(int chronologicalIndex)
        {
            return (start + chronologicalIndex) % entries.Length;
        }
    }
}
