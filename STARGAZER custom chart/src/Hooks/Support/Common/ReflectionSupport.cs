using System;
using System.Reflection;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static object? TryGetMemberValue(object instance, Type type, string memberName)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                PropertyInfo? property = type.GetProperty(memberName, Flags);
                if (property is not null && property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(instance);
                }
            }
            catch
            {
            }

            try
            {
                FieldInfo? field = type.GetField(memberName, Flags);
                if (field is not null)
                {
                    return field.GetValue(instance);
                }
            }
            catch
            {
            }

            try
            {
                MethodInfo? getter = type.GetMethod($"get_{memberName}", Flags, null, Type.EmptyTypes, null);
                if (getter is not null)
                {
                    return getter.Invoke(instance, Array.Empty<object>());
                }
            }
            catch
            {
            }

            return null;
        }
    }
}