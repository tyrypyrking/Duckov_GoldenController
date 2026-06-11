using System;
using System.Reflection;

namespace DuckovController
{
    // Shared hierarchy-walking reflection helpers. Type.GetField does NOT return base-class private fields;
    // Unity serialized fields are usually private, so we walk t.BaseType at each level.
    internal static class ReflectionUtil
    {
        internal const BindingFlags InstanceAll =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        // First field named `name` on `type` or any base type; null if absent.
        internal static FieldInfo? WalkField(Type? type, string name, BindingFlags flags = InstanceAll)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, flags);
                if (f != null) return f;
            }
            return null;
        }

        // First method named `name` on `type` or any base type; null if absent.
        // Pass argTypes (e.g. Type.EmptyTypes) to disambiguate by signature.
        internal static MethodInfo? WalkMethod(Type? type, string name,
            BindingFlags flags = InstanceAll, Type[]? argTypes = null)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var m = argTypes == null
                    ? t.GetMethod(name, flags)
                    : t.GetMethod(name, flags, null, argTypes, null);
                if (m != null) return m;
            }
            return null;
        }
    }
}
