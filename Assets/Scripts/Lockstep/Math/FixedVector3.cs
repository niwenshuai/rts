using System;

namespace AIRTS.Lockstep.Math
{
    [Serializable]
    public readonly struct FixedVector3 : IEquatable<FixedVector3>
    {
        public static readonly FixedVector3 Zero = new FixedVector3(Fix64.Zero, Fix64.Zero, Fix64.Zero);
        public static readonly FixedVector3 One = new FixedVector3(Fix64.One, Fix64.One, Fix64.One);

        public Fix64 X { get; }
        public Fix64 Y { get; }
        public Fix64 Z { get; }

        public Fix64 SqrMagnitude => X * X + Y * Y + Z * Z;
        public Fix64 Magnitude => FixedMath.Sqrt(SqrMagnitude);

        public FixedVector3(Fix64 x, Fix64 y, Fix64 z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public FixedVector3 Normalized
        {
            get
            {
                Fix64 magnitude = Magnitude;
                return magnitude <= Fix64.Epsilon ? Zero : this / magnitude;
            }
        }

        public bool Equals(FixedVector3 other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is FixedVector3 other && Equals(other);
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

        public override string ToString()
        {
            return "(" + X + ", " + Y + ", " + Z + ")";
        }

        public static Fix64 Dot(FixedVector3 a, FixedVector3 b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        public static FixedVector3 operator +(FixedVector3 a, FixedVector3 b)
        {
            return new FixedVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static FixedVector3 operator -(FixedVector3 a, FixedVector3 b)
        {
            return new FixedVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static FixedVector3 operator *(FixedVector3 value, Fix64 scale)
        {
            return new FixedVector3(value.X * scale, value.Y * scale, value.Z * scale);
        }

        public static FixedVector3 operator /(FixedVector3 value, Fix64 scale)
        {
            return new FixedVector3(value.X / scale, value.Y / scale, value.Z / scale);
        }

        public static bool operator ==(FixedVector3 a, FixedVector3 b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(FixedVector3 a, FixedVector3 b)
        {
            return !a.Equals(b);
        }
    }
}
