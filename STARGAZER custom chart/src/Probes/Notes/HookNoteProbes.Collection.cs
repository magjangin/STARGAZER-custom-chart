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
