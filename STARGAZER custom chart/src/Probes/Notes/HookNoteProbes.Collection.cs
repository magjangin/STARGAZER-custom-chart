using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private sealed class CollectionAccessor
        {
            public CollectionAccessor(PropertyInfo? countProp, MethodInfo? getItemMethod, MethodInfo? addMethod, MethodInfo? clearMethod)
            {
                CountProp = countProp;
                GetItemMethod = getItemMethod;
                AddMethod = addMethod;
                ClearMethod = clearMethod;
            }

            public PropertyInfo? CountProp { get; }
            public MethodInfo? GetItemMethod { get; }
            public MethodInfo? AddMethod { get; }
            public MethodInfo? ClearMethod { get; }
        }

        private static readonly ConcurrentDictionary<Type, CollectionAccessor> CollectionAccessorCache =
            new ConcurrentDictionary<Type, CollectionAccessor>();

        private static CollectionAccessor GetCollectionAccessor(Type type)
        {
            if (CollectionAccessorCache.TryGetValue(type, out CollectionAccessor? cached))
            {
                return cached;
            }

            PropertyInfo? countProp = type.GetProperty("Count", InstanceMemberFlags)
                ?? type.GetProperty("Length", InstanceMemberFlags);
            MethodInfo? getItemMethod = type.GetMethod("get_Item", InstanceMemberFlags, null, new[] { typeof(int) }, null);
            MethodInfo? addMethod = type.GetMethods(InstanceMemberFlags)
                .FirstOrDefault(m => string.Equals(m.Name, "Add", StringComparison.Ordinal) && m.GetParameters().Length == 1);
            MethodInfo? clearMethod = type.GetMethod("Clear", InstanceMemberFlags, null, Type.EmptyTypes, null);

            var resolved = new CollectionAccessor(countProp, getItemMethod, addMethod, clearMethod);
            CollectionAccessorCache.TryAdd(type, resolved);
            return resolved;
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

            CollectionAccessor accessor = GetCollectionAccessor(collectionValue.GetType());
            if (accessor.CountProp is null || accessor.GetItemMethod is null)
            {
                yield break;
            }

            object? countObj;
            try
            {
                countObj = accessor.CountProp.GetValue(collectionValue);
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
            object[] indexArgs = new object[1];
            for (int i = 0; i < limit; i++)
            {
                object? item;
                try
                {
                    indexArgs[0] = i;
                    item = accessor.GetItemMethod.Invoke(collectionValue, indexArgs);
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

            CollectionAccessor accessor = GetCollectionAccessor(value.GetType());
            if (accessor.CountProp is null)
            {
                return null;
            }

            try
            {
                object? countValue = accessor.CountProp.GetValue(value);
                if (countValue is not null)
                {
                    return Convert.ToInt32(countValue);
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
