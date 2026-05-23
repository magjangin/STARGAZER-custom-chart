using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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

        private static string BuildNoteSignature(object note)
        {
            Type type = note.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var parts = new List<string>();

            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (!LooksLikeUsefulNoteMember(property.Name, property.PropertyType))
                {
                    continue;
                }

                object? value = null;
                try
                {
                    value = property.GetValue(note);
                }
                catch
                {
                }

                parts.Add($"P:{property.Name}={FormatSignatureValue(value, property.PropertyType)}");
                if (parts.Count >= 12)
                {
                    break;
                }
            }

            if (parts.Count < 12)
            {
                foreach (FieldInfo field in type.GetFields(flags))
                {
                    if (!LooksLikeUsefulNoteMember(field.Name, field.FieldType))
                    {
                        continue;
                    }

                    object? value = null;
                    try
                    {
                        value = field.GetValue(note);
                    }
                    catch
                    {
                    }

                    parts.Add($"F:{field.Name}={FormatSignatureValue(value, field.FieldType)}");
                    if (parts.Count >= 12)
                    {
                        break;
                    }
                }
            }

            return $"{type.Name}[{string.Join(", ", parts)}]";
        }

        private static bool LooksLikeUsefulNoteMember(string name, Type type)
        {
            string normalized = name.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            if (normalized.Contains("time", StringComparison.Ordinal)
                || normalized.Contains("tick", StringComparison.Ordinal)
                || normalized.Contains("type", StringComparison.Ordinal)
                || normalized.Contains("hold", StringComparison.Ordinal)
                || normalized.Contains("long", StringComparison.Ordinal)
                || normalized.Contains("link", StringComparison.Ordinal)
                || normalized.Contains("group", StringComparison.Ordinal)
                || normalized.Contains("head", StringComparison.Ordinal)
                || normalized.Contains("tail", StringComparison.Ordinal)
                || normalized.Contains("next", StringComparison.Ordinal)
                || normalized.Contains("prev", StringComparison.Ordinal)
                || normalized.Contains("lane", StringComparison.Ordinal)
                || normalized.Contains("start", StringComparison.Ordinal)
                || normalized.Contains("end", StringComparison.Ordinal)
                || normalized.Contains("duration", StringComparison.Ordinal)
                || normalized.Contains("length", StringComparison.Ordinal))
            {
                return true;
            }

            return type.IsEnum
                || type == typeof(int)
                || type == typeof(float)
                || type == typeof(double)
                || type == typeof(bool)
                || type == typeof(string);
        }

        private static string FormatSignatureValue(object? value, Type declaredType)
        {
            if (value is null)
            {
                return "null";
            }

            if (declaredType.IsEnum)
            {
                return value.ToString() ?? "<enum>";
            }

            if (value is IConvertible)
            {
                return value.ToString() ?? "<value>";
            }

            return value.GetType().Name;
        }

        private static string BuildNoteMemberCatalog(object note)
        {
            Type type = note.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            string props = string.Join(", ",
                type.GetProperties(flags)
                    .Where(property => property.GetIndexParameters().Length == 0)
                    .Take(48)
                    .Select(property =>
                    {
                        string kind = property.CanWrite ? "rw" : "ro";
                        string propType = property.PropertyType.Name;
                        return $"{property.Name}:{propType}:{kind}";
                    }));

            string fields = string.Join(", ",
                type.GetFields(flags)
                    .Take(48)
                    .Select(field => $"{field.Name}:{field.FieldType.Name}"));

            string methods = string.Join(", ",
                type.GetMethods(flags)
                    .Where(method => method.GetParameters().Length == 0 && (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("is_", StringComparison.Ordinal)))
                    .Take(48)
                    .Select(method => method.Name));

            if (string.IsNullOrWhiteSpace(props))
            {
                props = "<none>";
            }

            if (string.IsNullOrWhiteSpace(fields))
            {
                fields = "<none>";
            }

            if (string.IsNullOrWhiteSpace(methods))
            {
                methods = "<none>";
            }

            return $"type={type.FullName}; props=[{props}] ; fields=[{fields}] ; methods=[{methods}]";
        }

        private static string BuildObjectMemberCatalog(string label, object instance)
        {
            Type type = instance.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            string props = string.Join(", ",
                type.GetProperties(flags)
                    .Where(property => property.GetIndexParameters().Length == 0)
                    .Take(64)
                    .Select(property =>
                    {
                        string kind = property.CanWrite ? "rw" : "ro";
                        return $"{property.Name}:{property.PropertyType.Name}:{kind}";
                    }));
            if (string.IsNullOrWhiteSpace(props))
            {
                props = "<none>";
            }

            string fields = string.Join(", ",
                type.GetFields(flags)
                    .Take(64)
                    .Select(field => $"{field.Name}:{field.FieldType.Name}"));
            if (string.IsNullOrWhiteSpace(fields))
            {
                fields = "<none>";
            }

            string methods = string.Join(", ",
                type.GetMethods(flags)
                    .Where(method => method.GetParameters().Length == 0
                        && (method.Name.StartsWith("get_", StringComparison.Ordinal)
                            || method.Name.StartsWith("is_", StringComparison.Ordinal)
                            || method.Name.IndexOf("beat", StringComparison.OrdinalIgnoreCase) >= 0
                            || method.Name.IndexOf("time", StringComparison.OrdinalIgnoreCase) >= 0))
                    .Take(64)
                    .Select(method => method.Name));
            if (string.IsNullOrWhiteSpace(methods))
            {
                methods = "<none>";
            }

            return $"{label}.type={type.FullName}; props=[{props}] ; fields=[{fields}] ; methods=[{methods}]";
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

        private static bool TryResolveAreaNotesMember(object area, out PropertyInfo notesProperty, out object? notesValue)
        {
            notesProperty = null!;
            notesValue = null;

            Type areaType = area.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo? prop = areaType.GetProperty("notes", flags)
                ?? areaType.GetProperty("Notes", flags);
            if (prop is null || !prop.CanRead)
            {
                return false;
            }

            try
            {
                notesValue = prop.GetValue(area);
            }
            catch
            {
                return false;
            }

            notesProperty = prop;
            return true;
        }

        private static IEnumerable<object?> EnumerateCollectionItems(object collectionValue, int maxCount)
        {
            if (maxCount <= 0)
            {
                yield break;
            }

            if (collectionValue is System.Collections.IEnumerable enumerable)
            {
                int count = 0;
                System.Collections.IEnumerator? enumerator = null;
                try
                {
                    enumerator = enumerable.GetEnumerator();
                }
                catch
                {
                    enumerator = null;
                }

                if (enumerator is not null)
                {
                    while (count < maxCount)
                    {
                        bool moved;
                        try
                        {
                            moved = enumerator.MoveNext();
                        }
                        catch
                        {
                            break;
                        }

                        if (!moved)
                        {
                            break;
                        }

                        object? current;
                        try
                        {
                            current = enumerator.Current;
                        }
                        catch
                        {
                            break;
                        }

                        count++;
                        yield return current;
                    }

                    if (count > 0)
                    {
                        yield break;
                    }
                }
            }

            Type type = collectionValue.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo? countProperty = type.GetProperty("Count", flags) ?? type.GetProperty("Length", flags);
            MethodInfo? getItemMethod = type.GetMethod("get_Item", flags, null, new[] { typeof(int) }, null);
            if (countProperty is null || getItemMethod is null)
            {
                yield break;
            }

            object? countObj;
            try
            {
                countObj = countProperty.GetValue(collectionValue);
            }
            catch
            {
                yield break;
            }

            if (countObj is null)
            {
                yield break;
            }

            int reflectedCount;
            try
            {
                reflectedCount = Convert.ToInt32(countObj);
            }
            catch
            {
                yield break;
            }

            int limit = Math.Min(maxCount, reflectedCount);
            for (int i = 0; i < limit; i++)
            {
                object? item;
                try
                {
                    item = getItemMethod.Invoke(collectionValue, new object[] { i });
                }
                catch
                {
                    continue;
                }

                yield return item;
            }
        }

        private static int? TryGetCollectionCount(object? value)
        {
            if (value is null)
            {
                return null;
            }

            if (value is Array array)
            {
                return array.Length;
            }

            Type type = value.GetType();
            try
            {
                PropertyInfo? countProperty = type.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (countProperty is not null)
                {
                    object? countValue = countProperty.GetValue(value);
                    if (countValue is not null)
                    {
                        return Convert.ToInt32(countValue);
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
