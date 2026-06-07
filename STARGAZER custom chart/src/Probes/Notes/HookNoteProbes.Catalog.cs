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
    }
}
