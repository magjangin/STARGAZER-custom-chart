using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static bool TryGetDoubleByNameCandidates(object owner, IReadOnlyList<string> names, out double? value)
        {
            value = null;
            if (!TryGetValueByNameCandidates(owner, names, out object? obj) || obj is null)
            {
                return false;
            }

            try
            {
                value = Convert.ToDouble(obj);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetValueByNameCandidates(object owner, IReadOnlyList<string> names, out object? value)
        {
            value = null;
            Type type = owner.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (!NameMatchesAny(property.Name, names))
                {
                    continue;
                }

                try
                {
                    value = property.GetValue(owner);
                    if (value is not null)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (!NameMatchesAny(field.Name, names))
                {
                    continue;
                }

                try
                {
                    value = field.GetValue(owner);
                    if (value is not null)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.GetParameters().Length != 0)
                {
                    continue;
                }

                if (!NameMatchesAny(method.Name, names))
                {
                    continue;
                }

                try
                {
                    value = method.Invoke(owner, Array.Empty<object>());
                    if (value is not null)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static bool NameMatchesAny(string sourceName, IReadOnlyList<string> candidates)
        {
            string normalized = sourceName.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (normalized.Contains(candidates[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        private static bool LooksLikeTimeName(string name, IReadOnlyList<string> candidates)
        {
            string normalized = name.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            foreach (string candidate in candidates)
            {
                if (normalized.Contains(candidate, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryConvertToDouble(object? value, out double result)
        {
            result = 0d;
            if (value is null)
            {
                return false;
            }

            try
            {
                result = Convert.ToDouble(value);
                return !double.IsNaN(result) && !double.IsInfinity(result);
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetValueByNameCandidates(object owner, IReadOnlyList<string> names, object? value)
        {
            Type type = owner.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (!property.CanWrite || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (!NameMatchesAny(property.Name, names))
                {
                    continue;
                }

                try
                {
                    property.SetValue(owner, value);
                    return true;
                }
                catch
                {
                }
            }

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.GetParameters().Length != 1)
                {
                    continue;
                }

                if (!NameMatchesAny(method.Name, names))
                {
                    continue;
                }

                try
                {
                    method.Invoke(owner, new[] { value });
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }
    }
}
