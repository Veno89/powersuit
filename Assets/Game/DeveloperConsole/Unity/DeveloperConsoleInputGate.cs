using System;
using System.Collections.Generic;
using UnityEngine;

namespace Powersuit.DeveloperConsole.UnityAdapters
{
    /// <summary>
    /// Shared input-suspension signal for gameplay adapters. The overlay also
    /// supports explicitly disabling serialized input behaviours, so adopting
    /// this gate can be incremental.
    /// </summary>
    public static class DeveloperConsoleInputGate
    {
        private static readonly HashSet<EntityId> BlockingOwners =
            new HashSet<EntityId>();

        public static event Action<bool> BlockedChanged;

        public static bool IsGameplayInputBlocked => BlockingOwners.Count > 0;

        internal static void SetBlocked(EntityId ownerId, bool blocked)
        {
            bool wasBlocked = IsGameplayInputBlocked;
            if (blocked)
            {
                BlockingOwners.Add(ownerId);
            }
            else
            {
                BlockingOwners.Remove(ownerId);
            }

            bool isBlocked = IsGameplayInputBlocked;
            if (wasBlocked != isBlocked)
            {
                BlockedChanged?.Invoke(isBlocked);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            BlockingOwners.Clear();
            BlockedChanged = null;
        }
    }
}
