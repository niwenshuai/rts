using System;
using System.Globalization;

namespace AIRTS.Lockstep.Math
{
    [Serializable]
    public readonly struct Fix64 : IComparable<Fix64>, IEquatable<Fix64>
    {
        public const long Scale = 10000L;

        public static readonly Fix64 Zero = new Fix64(0);
        public static readonly Fix64 One = new Fix64(Scale);
        public static readonly Fix64 Half = new Fix64(Scale / 2);
        public static readonly Fix64 Epsilon = new Fix64(1);

        public long RawValue { get; }

        public Fix64(long rawValue)
        {
            RawValue = rawValue;
        }

        public static Fix64 FromRaw(long rawValue)
        {
            return new Fix64(rawValue);
        }

        public static Fix64 FromInt(int value)
        {
            return new Fix64(value * Scale);
        }

        public static Fix64 FromLong(long value)
        {
            return new Fix64(value * Scale);
        }

        public static Fix64 FromFloat(float value)
        {
            return new Fix64((long)System.Math.Round(value * Scale));
        }

        public static Fix64 FromDouble(double value)
        {
            return new Fix64((long)System.Math.Round(value * Scale));
        }

        public int ToInt()
        {
            return (int)(RawValue / Scale);
        }

        public float ToFloat()
        {
            return RawValue / (float)Scale;
        }

        public double ToDouble()
        {
            return RawValue / (double)Scale;
        }

        public int CompareTo(Fix64 other)
        {
            return RawValue.CompareTo(other.RawValue);
        }

        public bool Equals(Fix64 other)
        {
            return RawValue == other.RawValue;
        }

        public override bool Equals(object obj)
        {
            return obj is Fix64 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return RawValue.GetHashCode();
        }

        public override string ToString()
        {
            return ToDouble().ToString("0.####", CultureInfo.InvariantCulture);
        }

        public static Fix64 operator +(Fix64 a, Fix64 b)
        {
            return new Fix64(a.RawValue + b.RawValue);
        }

        public static Fix64 operator -(Fix64 a, Fix64 b)
        {
            return new Fix64(a.RawValue - b.RawValue);
        }

        public static Fix64 operator -(Fix64 value)
        {
            return new Fix64(-value.RawValue);
        }

        public static Fix64 operator *(Fix64 a, Fix64 b)
        {
            return new Fix64(a.RawValue * b.RawValue / Scale);
        }

        public static Fix64 operator /(Fix64 a, Fix64 b)
        {
            if (b.RawValue == 0)
            {
                throw new DivideByZeroException();
            }

            return new Fix64(a.RawValue * Scale / b.RawValue);
        }

        public static bool operator ==(Fix64 a, Fix64 b)
        {
            return a.RawValue == b.RawValue;
        }

        public static bool operator !=(Fix64 a, Fix64 b)
        {
            return a.RawValue != b.RawValue;
        }

        public static bool operator <(Fix64 a, Fix64 b)
        {
            return a.RawValue < b.RawValue;
        }

        public static bool operator >(Fix64 a, Fix64 b)
        {
            return a.RawValue > b.RawValue;
        }

        public static bool operator <=(Fix64 a, Fix64 b)
        {
            return a.RawValue <= b.RawValue;
        }

        public static bool operator >=(Fix64 a, Fix64 b)
        {
            return a.RawValue >= b.RawValue;
        }

        public static implicit operator Fix64(int value)
        {
            return FromInt(value);
        }
    }
}
