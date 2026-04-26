namespace AIRTS.Lockstep.Math
{
    public static class FixedMath
    {
        public static Fix64 Abs(Fix64 value)
        {
            return value.RawValue < 0 ? Fix64.FromRaw(-value.RawValue) : value;
        }

        public static Fix64 Min(Fix64 a, Fix64 b)
        {
            return a.RawValue <= b.RawValue ? a : b;
        }

        public static Fix64 Max(Fix64 a, Fix64 b)
        {
            return a.RawValue >= b.RawValue ? a : b;
        }

        public static Fix64 Clamp(Fix64 value, Fix64 min, Fix64 max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        public static Fix64 Lerp(Fix64 from, Fix64 to, Fix64 t)
        {
            t = Clamp(t, Fix64.Zero, Fix64.One);
            return from + (to - from) * t;
        }

        public static Fix64 Sqrt(Fix64 value)
        {
            if (value.RawValue < 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(value), "Cannot sqrt a negative fixed point value.");
            }

            if (value.RawValue == 0)
            {
                return Fix64.Zero;
            }

            long n = value.RawValue * Fix64.Scale;
            long x = n;
            long y = (x + 1) / 2;
            while (y < x)
            {
                x = y;
                y = (x + n / x) / 2;
            }

            return Fix64.FromRaw(x);
        }

        public static Fix64 MoveTowards(Fix64 current, Fix64 target, Fix64 maxDelta)
        {
            Fix64 delta = target - current;
            if (Abs(delta) <= maxDelta)
            {
                return target;
            }

            return current + (delta > Fix64.Zero ? maxDelta : -maxDelta);
        }
    }
}
