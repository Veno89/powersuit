using System;

namespace Powersuit.DeveloperConsole
{
    /// <summary>
    /// Bounded console transcript. Once full, writes replace the oldest entry
    /// instead of growing managed memory during long stress-test sessions.
    /// </summary>
    public sealed class ConsoleOutputBuffer
    {
        private readonly ConsoleOutputEntry[] entries;
        private int start;
        private int count;
        private long nextSequence;

        public ConsoleOutputBuffer(int capacity = 96)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            entries = new ConsoleOutputEntry[capacity];
        }

        public int Capacity => entries.Length;
        public int Count => count;
        public int Version { get; private set; }

        public ConsoleOutputEntry GetAt(int chronologicalIndex)
        {
            if (chronologicalIndex < 0 || chronologicalIndex >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(chronologicalIndex));
            }

            return entries[(start + chronologicalIndex) % entries.Length];
        }

        public void Add(string text, ConsoleMessageType messageType)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var entry = new ConsoleOutputEntry(nextSequence++, messageType, text);
            if (count < entries.Length)
            {
                entries[(start + count) % entries.Length] = entry;
                count++;
            }
            else
            {
                entries[start] = entry;
                start = (start + 1) % entries.Length;
            }

            Version++;
        }

        public void Clear()
        {
            Array.Clear(entries, 0, entries.Length);
            start = 0;
            count = 0;
            Version++;
        }
    }
}
