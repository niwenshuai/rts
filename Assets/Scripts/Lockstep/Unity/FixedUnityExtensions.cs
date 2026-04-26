using AIRTS.Lockstep.Math;
using UnityEngine;

namespace AIRTS.Lockstep.Unity
{
    public static class FixedUnityExtensions
    {
        public static Vector3 ToUnityVector3(this FixedVector3 value)
        {
            return new Vector3(value.X.ToFloat(), value.Y.ToFloat(), value.Z.ToFloat());
        }

        public static FixedVector3 ToFixedVector3(this Vector3 value)
        {
            return new FixedVector3(Fix64.FromFloat(value.x), Fix64.FromFloat(value.y), Fix64.FromFloat(value.z));
        }

        public static Vector2 ToUnityVector2(this FixedVector2 value)
        {
            return new Vector2(value.X.ToFloat(), value.Y.ToFloat());
        }

        public static FixedVector2 ToFixedVector2(this Vector2 value)
        {
            return new FixedVector2(Fix64.FromFloat(value.x), Fix64.FromFloat(value.y));
        }
    }
}
