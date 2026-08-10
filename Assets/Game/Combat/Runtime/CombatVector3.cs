using System;

namespace Powersuit.Combat
{
    /// <summary>
    /// Small engine-independent vector value used by combat-domain contracts.
    /// Unity adapters can translate to and from UnityEngine.Vector3 at their boundaries.
    /// </summary>
    public readonly struct CombatVector3 : IEquatable<CombatVector3>
    {
        public CombatVector3(float x, float y, float z)
        {
            RequireFinite(x, nameof(x));
            RequireFinite(y, nameof(y));
            RequireFinite(z, nameof(z));

            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public static CombatVector3 Zero => default;

        public float SqrMagnitude => X * X + Y * Y + Z * Z;
        public float Magnitude => (float)Math.Sqrt(SqrMagnitude);
        public bool IsZero => X == 0f && Y == 0f && Z == 0f;

        public bool Equals(CombatVector3 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is CombatVector3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = X.GetHashCode();
                hashCode = (hashCode * 397) ^ Y.GetHashCode();
                hashCode = (hashCode * 397) ^ Z.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(CombatVector3 left, CombatVector3 right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(CombatVector3 left, CombatVector3 right)
        {
            return !left.Equals(right);
        }

        private static void RequireFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Combat-vector components must be finite."
                );
            }
        }
    }
}
