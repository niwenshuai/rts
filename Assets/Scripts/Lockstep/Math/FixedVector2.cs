using System;

namespace AIRTS.Lockstep.Math
{
    [Serializable]
    public readonly struct FixedVector2 : IEquatable<FixedVector2>
    {
        public static readonly FixedVector2 Zero = new FixedVector2(Fix64.Zero, Fix64.Zero);
        public static readonly FixedVector2 One = new FixedVector2(Fix64.One, Fix64.One);

        public Fix64 X { get; }
        public Fix64 Y { get; }

        public Fix64 SqrMagnitude => X * X + Y * Y;
        public Fix64 Magnitude => FixedMath.Sqrt(SqrMagnitude);

        public FixedVector2(Fix64 x, Fix64 y)
        {
            X = x;
            Y = y;
        }

        public FixedVector2 Normalized
        {
            get
            {
                Fix64 magnitude = Magnitude;
                return magnitude <= Fix64.Epsilon ? Zero : this / magnitude;
            }
        }

        public bool Equals(FixedVector2 other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is FixedVector2 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        public override string ToString()
        {
            return "(" + X + ", " + Y + ")";
        }

        public static Fix64 Dot(FixedVector2 a, FixedVector2 b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        public static FixedVector2 operator +(FixedVector2 a, FixedVector2 b)
        {
            return new FixedVector2(a.X + b.X, a.Y + b.Y);
        }

        public static FixedVector2 operator -(FixedVector2 a, FixedVector2 b)
        {
            return new FixedVector2(a.X - b.X, a.Y - b.Y);
        }

        public static FixedVector2 operator *(FixedVector2 value, Fix64 scale)
        {
            return new FixedVector2(value.X * scale, value.Y * scale);
        }

        public static FixedVector2 operator /(FixedVector2 value, Fix64 scale)
        {
            return new FixedVector2(value.X / scale, value.Y / scale);
        }

        public static bool operator ==(FixedVector2 a, FixedVector2 b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(FixedVector2 a, FixedVector2 b)
        {
            return !a.Equals(b);
        }
    }
}
