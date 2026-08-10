using UnityEngine;

namespace Powersuit.Combat.UnityAdapters
{
    /// <summary>
    /// Explicit boundary between engine-independent combat contracts and Unity.
    /// </summary>
    public static class CombatVectorConversion
    {
        public static CombatVector3 ToCombat(Vector3 value)
        {
            return new CombatVector3(value.x, value.y, value.z);
        }

        public static Vector3 ToUnity(CombatVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }
    }
}
