using System.Collections.Generic;
using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.BehaviorTree
{
    public enum BehaviorValueType
    {
        None,
        Bool,
        Int,
        Fix64,
        FixedVector2,
        FixedVector3
    }

    public readonly struct BehaviorValue
    {
        public BehaviorValueType Type { get; }
        public bool BoolValue { get; }
        public int IntValue { get; }
        public Fix64 Fix64Value { get; }
        public FixedVector2 Vector2Value { get; }
        public FixedVector3 Vector3Value { get; }

        private BehaviorValue(
            BehaviorValueType type,
            bool boolValue,
            int intValue,
            Fix64 fix64Value,
            FixedVector2 vector2Value,
            FixedVector3 vector3Value)
        {
            Type = type;
            BoolValue = boolValue;
            IntValue = intValue;
            Fix64Value = fix64Value;
            Vector2Value = vector2Value;
            Vector3Value = vector3Value;
        }

        public static BehaviorValue FromBool(bool value)
        {
            return new BehaviorValue(BehaviorValueType.Bool, value, 0, Fix64.Zero, FixedVector2.Zero, FixedVector3.Zero);
        }

        public static BehaviorValue FromInt(int value)
        {
            return new BehaviorValue(BehaviorValueType.Int, false, value, Fix64.Zero, FixedVector2.Zero, FixedVector3.Zero);
        }

        public static BehaviorValue FromFix64(Fix64 value)
        {
            return new BehaviorValue(BehaviorValueType.Fix64, false, 0, value, FixedVector2.Zero, FixedVector3.Zero);
        }

        public static BehaviorValue FromFixedVector2(FixedVector2 value)
        {
            return new BehaviorValue(BehaviorValueType.FixedVector2, false, 0, Fix64.Zero, value, FixedVector3.Zero);
        }

        public static BehaviorValue FromFixedVector3(FixedVector3 value)
        {
            return new BehaviorValue(BehaviorValueType.FixedVector3, false, 0, Fix64.Zero, FixedVector2.Zero, value);
        }
    }

    public sealed class BehaviorBlackboard
    {
        private readonly Dictionary<string, BehaviorValue> _values = new Dictionary<string, BehaviorValue>();

        public int Count => _values.Count;

        public void Clear()
        {
            _values.Clear();
        }

        public bool Contains(string key)
        {
            return _values.ContainsKey(key);
        }

        public bool Remove(string key)
        {
            return _values.Remove(key);
        }

        public void Set(string key, BehaviorValue value)
        {
            _values[key] = value;
        }

        public void SetBool(string key, bool value)
        {
            Set(key, BehaviorValue.FromBool(value));
        }

        public void SetInt(string key, int value)
        {
            Set(key, BehaviorValue.FromInt(value));
        }

        public void SetFix64(string key, Fix64 value)
        {
            Set(key, BehaviorValue.FromFix64(value));
        }

        public void SetFixedVector2(string key, FixedVector2 value)
        {
            Set(key, BehaviorValue.FromFixedVector2(value));
        }

        public void SetFixedVector3(string key, FixedVector3 value)
        {
            Set(key, BehaviorValue.FromFixedVector3(value));
        }

        public bool TryGet(string key, out BehaviorValue value)
        {
            return _values.TryGetValue(key, out value);
        }

        public bool TryGetBool(string key, out bool value)
        {
            if (_values.TryGetValue(key, out var stored) && stored.Type == BehaviorValueType.Bool)
            {
                value = stored.BoolValue;
                return true;
            }

            value = false;
            return false;
        }

        public bool TryGetInt(string key, out int value)
        {
            if (_values.TryGetValue(key, out var stored) && stored.Type == BehaviorValueType.Int)
            {
                value = stored.IntValue;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetFix64(string key, out Fix64 value)
        {
            if (_values.TryGetValue(key, out var stored) && stored.Type == BehaviorValueType.Fix64)
            {
                value = stored.Fix64Value;
                return true;
            }

            value = Fix64.Zero;
            return false;
        }

        public bool TryGetFixedVector2(string key, out FixedVector2 value)
        {
            if (_values.TryGetValue(key, out var stored) && stored.Type == BehaviorValueType.FixedVector2)
            {
                value = stored.Vector2Value;
                return true;
            }

            value = FixedVector2.Zero;
            return false;
        }

        public bool TryGetFixedVector3(string key, out FixedVector3 value)
        {
            if (_values.TryGetValue(key, out var stored) && stored.Type == BehaviorValueType.FixedVector3)
            {
                value = stored.Vector3Value;
                return true;
            }

            value = FixedVector3.Zero;
            return false;
        }
    }
}
