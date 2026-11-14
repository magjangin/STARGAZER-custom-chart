using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private const BindingFlags InstanceMemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private sealed class MemberAccessor
        {
            public static readonly MemberAccessor Empty = new MemberAccessor(null, null, null, null);

            public MemberAccessor(string? name, PropertyInfo? property, FieldInfo? field, MethodInfo? getter)
            {
                Name = name;
                Property = property;
                Field = field;
                Getter = getter;
            }

            public string? Name { get; }
            public PropertyInfo? Property { get; }
            public FieldInfo? Field { get; }
            public MethodInfo? Getter { get; }
            public bool IsEmpty => Property is null && Field is null && Getter is null;

            public object? GetValue(object instance)
            {
                if (Property is not null)
                {
                    try
                    {
                        return Property.GetValue(instance);
                    }
                    catch
                    {
                    }
                }

                if (Field is not null)
                {
                    try
                    {
                        return Field.GetValue(instance);
                    }
                    catch
                    {
                    }
                }

                if (Getter is not null)
                {
                    try
                    {
                        return Getter.Invoke(instance, Array.Empty<object>());
                    }
                    catch
                    {
                    }
                }

                return null;
            }
        }

        private sealed class MemberSetter
        {
            public static readonly MemberSetter Empty = new MemberSetter(null, null, null);

            public MemberSetter(PropertyInfo? property, FieldInfo? field, MethodInfo? method)
            {
                Property = property;
                Field = field;
                Method = method;
            }

            public PropertyInfo? Property { get; }
            public FieldInfo? Field { get; }
            public MethodInfo? Method { get; }
            public bool IsEmpty => Property is null && Field is null && Method is null;

            public bool TrySetValue(object owner, object? value)
            {
                if (Property is not null)
                {
                    try
                    {
                        Property.SetValue(owner, value);
                        return true;
                    }
                    catch
                    {
                    }
                }

                if (Field is not null)
                {
                    try
                    {
                        Field.SetValue(owner, value);
                        return true;
                    }
                    catch
                    {
                    }
                }

                if (Method is not null)
                {
                    try
                    {
                        Method.Invoke(owner, new[] { value });
                        return true;
                    }
                    catch
                    {
                    }
                }

                return false;
            }
        }

        private static readonly ConcurrentDictionary<(Type Type, string Name), MemberAccessor> MemberAccessorCache =
            new ConcurrentDictionary<(Type Type, string Name), MemberAccessor>();

        private static readonly ConcurrentDictionary<(Type Type, string ExpectedType), MemberAccessor> TypeFallbackAccessorCache =
            new ConcurrentDictionary<(Type Type, string ExpectedType), MemberAccessor>();

        private static readonly ConcurrentDictionary<(Type Type, string CandidatesKey), MemberSetter> MemberSetterCache =
            new ConcurrentDictionary<(Type Type, string CandidatesKey), MemberSetter>();

        private static readonly ConcurrentDictionary<Type, Func<object?>?> InstantiatorCache =
            new ConcurrentDictionary<Type, Func<object?>?>();

        private static readonly ConcurrentDictionary<Type, MethodInfo?> CallbackInvokeCache =
            new ConcurrentDictionary<Type, MethodInfo?>();

        private static readonly MethodInfo? ScriptableObjectCreateInstanceMethod =
            typeof(ScriptableObject).GetMethod(
                "CreateInstance",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Type) },
                null);

        private static MemberAccessor GetOrResolveMemberAccessor(Type type, string memberName)
        {
            var key = (type, memberName);
            if (MemberAccessorCache.TryGetValue(key, out MemberAccessor? cached))
            {
                return cached;
            }

            PropertyInfo? property = null;
            try
            {
                PropertyInfo? foundProp = type.GetProperty(memberName, InstanceMemberFlags);
                if (foundProp is not null && foundProp.CanRead && foundProp.GetIndexParameters().Length == 0)
                {
                    property = foundProp;
                }
            }
            catch
            {
            }

            FieldInfo? field = null;
            try
            {
                field = type.GetField(memberName, InstanceMemberFlags);
            }
            catch
            {
            }

            MethodInfo? getter = null;
            try
            {
                getter = type.GetMethod($"get_{memberName}", InstanceMemberFlags, null, Type.EmptyTypes, null);
            }
            catch
            {
            }

            MemberAccessor resolved = property is null && field is null && getter is null
                ? MemberAccessor.Empty
                : new MemberAccessor(memberName, property, field, getter);

            MemberAccessorCache.TryAdd(key, resolved);
            return resolved;
        }

        private static object? TryGetMemberValue(object instance, Type type, string memberName)
        {
            MemberAccessor accessor = GetOrResolveMemberAccessor(type, memberName);
            return accessor.IsEmpty ? null : accessor.GetValue(instance);
        }

        private static object? TryGetMemberValue(object? instance, params string[] memberNames)
        {
            if (instance is null || memberNames.Length == 0)
            {
                return null;
            }

            Type type = instance.GetType();
            for (int i = 0; i < memberNames.Length; i++)
            {
                MemberAccessor accessor = GetOrResolveMemberAccessor(type, memberNames[i]);
                if (accessor.IsEmpty)
                {
                    continue;
                }

                object? value = accessor.GetValue(instance);
                if (value is not null)
                {
                    return value;
                }
            }

            return null;
        }

        // 업데이트 내성용 타입 기반 멤버 조회:
        // 1) preferredNames 중 하나라도 타입에 존재하면 이름 기반 조회 결과만 돌려준다(런타임 값이 null이어도 타입 스캔 금지).
        // 2) preferredNames가 모두 타입에서 사라졌을 때만(이름 변경 업데이트) expectedTypeShortName에 해당하는
        //    유일한 읽기 가능 프로퍼티/필드를 찾아 캐시하고 [HookPatch][resolve] 경고를 1회 남긴다.
        private static object? TryGetMemberByType(object? instance, string expectedTypeShortName, params string[] preferredNames)
        {
            if (instance is null)
            {
                return null;
            }

            Type type = instance.GetType();
            bool anyPreferredMemberExists = false;

            for (int i = 0; i < preferredNames.Length; i++)
            {
                MemberAccessor accessor = GetOrResolveMemberAccessor(type, preferredNames[i]);
                if (accessor.IsEmpty)
                {
                    continue;
                }

                anyPreferredMemberExists = true;
                object? value = accessor.GetValue(instance);
                if (value is not null)
                {
                    return value;
                }
            }

            if (anyPreferredMemberExists)
            {
                return null;
            }

            MemberAccessor fallback = GetOrResolveUniqueMemberByType(type, expectedTypeShortName);
            return fallback.IsEmpty ? null : fallback.GetValue(instance);
        }

        private static MemberAccessor GetOrResolveUniqueMemberByType(Type type, string expectedTypeShortName)
        {
            var key = (type, expectedTypeShortName);
            if (TypeFallbackAccessorCache.TryGetValue(key, out MemberAccessor? cached))
            {
                return cached;
            }

            bool matchGenericArgs = expectedTypeShortName.IndexOf('<') >= 0;
            var nonBackingMatches = new List<(string Name, MemberAccessor Accessor)>();
            var backingMatches = new List<(string Name, MemberAccessor Accessor)>();

            try
            {
                foreach (PropertyInfo prop in type.GetProperties(InstanceMemberFlags))
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    if (!MatchesShortTypeName(prop.PropertyType, expectedTypeShortName, matchGenericArgs))
                    {
                        continue;
                    }

                    var targetList = IsCompilerBackingMember(prop.Name) ? backingMatches : nonBackingMatches;
                    AddDistinctCaseInsensitiveMatch(targetList, prop.Name, new MemberAccessor(prop.Name, prop, null, null));
                }
            }
            catch
            {
            }

            try
            {
                foreach (FieldInfo field in type.GetFields(InstanceMemberFlags))
                {
                    if (!MatchesShortTypeName(field.FieldType, expectedTypeShortName, matchGenericArgs))
                    {
                        continue;
                    }

                    var targetList = IsCompilerBackingMember(field.Name) ? backingMatches : nonBackingMatches;
                    AddDistinctCaseInsensitiveMatch(targetList, field.Name, new MemberAccessor(field.Name, null, field, null));
                }
            }
            catch
            {
            }

            List<(string Name, MemberAccessor Accessor)> candidates = nonBackingMatches.Count > 0 ? nonBackingMatches : backingMatches;
            MemberAccessor resolved = MemberAccessor.Empty;

            if (candidates.Count == 1)
            {
                resolved = candidates[0].Accessor;
                if (LogOnce($"member.resolve.{type.FullName}.{expectedTypeShortName}"))
                {
                    MelonLogger.Warning($"[HookPatch][resolve] {type.FullName} 에서 기대 이름 멤버를 못 찾아 타입 '{expectedTypeShortName}' 유일 멤버 '{candidates[0].Name}' 로 대체 해석했습니다.");
                }
            }
            else if (candidates.Count > 1)
            {
                if (LogOnce($"member.ambiguous.{type.FullName}.{expectedTypeShortName}"))
                {
                    string joined = string.Join(", ", candidates.Select(c => c.Name));
                    MelonLogger.Warning($"[HookPatch][resolve] {type.FullName} 에서 타입 '{expectedTypeShortName}' 후보가 {candidates.Count}개라 모호하여 쓰지 않습니다: {joined}");
                }
            }

            TypeFallbackAccessorCache.TryAdd(key, resolved);
            return resolved;
        }

        private static void AddDistinctCaseInsensitiveMatch(
            List<(string Name, MemberAccessor Accessor)> list,
            string name,
            MemberAccessor accessor)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            list.Add((name, accessor));
        }

        private static bool IsCompilerBackingMember(string name)
        {
            return name.EndsWith("k__BackingField", StringComparison.Ordinal)
                || name.StartsWith("<", StringComparison.Ordinal);
        }

        private static bool MatchesShortTypeName(Type memberType, string expectedTypeShortName, bool matchGenericArgs)
        {
            string actual = matchGenericArgs
                ? FormatShortTypeName(memberType)
                : StripGenericArity(memberType.Name);
            return string.Equals(actual, expectedTypeShortName, StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatShortTypeName(Type type)
        {
            if (type.IsByRef || type.IsPointer)
            {
                Type? element = type.GetElementType();
                if (element is not null)
                {
                    return FormatShortTypeName(element);
                }
            }

            string baseName = StripGenericArity(type.Name);
            if (!type.IsGenericType)
            {
                return baseName;
            }

            Type[] args = type.GetGenericArguments();
            if (args.Length == 0)
            {
                return baseName;
            }

            string formattedArgs = string.Join(",", args.Select(FormatShortTypeName));
            return $"{baseName}<{formattedArgs}>";
        }

        private static string StripGenericArity(string typeName)
        {
            int tick = typeName.IndexOf('`');
            return tick < 0 ? typeName : typeName.Substring(0, tick);
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
            if (owner is null || names.Count == 0)
            {
                return false;
            }

            Type type = owner.GetType();
            string candidatesKey = names.Count == 1 ? names[0] : string.Join("|", names);
            var key = (type, candidatesKey);

            if (!MemberSetterCache.TryGetValue(key, out MemberSetter? setter))
            {
                setter = ResolveMemberSetter(type, names);
                MemberSetterCache.TryAdd(key, setter);
            }

            return !setter.IsEmpty && setter.TrySetValue(owner, value);
        }

        private static MemberSetter ResolveMemberSetter(Type type, IReadOnlyList<string> names)
        {
            foreach (PropertyInfo property in type.GetProperties(InstanceMemberFlags))
            {
                if (!property.CanWrite || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (NameMatchesAny(property.Name, names))
                {
                    return new MemberSetter(property, null, null);
                }
            }

            foreach (FieldInfo field in type.GetFields(InstanceMemberFlags))
            {
                if (field.IsInitOnly)
                {
                    continue;
                }

                if (NameMatchesAny(field.Name, names))
                {
                    return new MemberSetter(null, field, null);
                }
            }

            foreach (MethodInfo method in type.GetMethods(InstanceMemberFlags))
            {
                if (method.GetParameters().Length != 1)
                {
                    continue;
                }

                if (NameMatchesAny(method.Name, names))
                {
                    return new MemberSetter(null, null, method);
                }
            }

            return MemberSetter.Empty;
        }

        private static void LogInstantiateStrategyOnce(Type type, string strategy)
        {
            if (LogOnce($"Instantiate.{type.FullName}.{strategy}"))
            {
                MelonLogger.Msg($"[Instantiate] Created {type.Name} using {strategy} (이 조합은 처음 1회만 기록합니다)");
            }
        }

        // IL2CPP 래퍼 타입의 빈 인스턴스 생성을 타입별로 캐시해 반복 생성 비용을 줄인다.
        private static object? InstantiateIl2CppObject(Type type)
        {
            if (InstantiatorCache.TryGetValue(type, out Func<object?>? cachedFactory))
            {
                if (cachedFactory is null)
                {
                    return null;
                }

                try
                {
                    return cachedFactory();
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Instantiate] Cached factory failed for {type.Name}: {ex.Message}");
                    return null;
                }
            }

            // 시도 1: ScriptableObject.CreateInstance
            if (typeof(ScriptableObject).IsAssignableFrom(type) && ScriptableObjectCreateInstanceMethod is not null)
            {
                try
                {
                    object? obj = ScriptableObjectCreateInstanceMethod.Invoke(null, new object[] { type });
                    if (obj is not null)
                    {
                        LogInstantiateStrategyOnce(type, "ScriptableObject.CreateInstance");
                        InstantiatorCache.TryAdd(type, () => ScriptableObjectCreateInstanceMethod.Invoke(null, new object[] { type }));
                        return obj;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Instantiate] ScriptableObject.CreateInstance failed for {type.Name}: {ex.Message}");
                }
            }

            // 시도 2: 매개변수 없는 생성자 (public 또는 non-public)
            try
            {
                ConstructorInfo? paramCtor = type.GetConstructor(InstanceMemberFlags, null, Type.EmptyTypes, null);
                if (paramCtor is not null)
                {
                    object obj = paramCtor.Invoke(null);
                    LogInstantiateStrategyOnce(type, "empty constructor");
                    InstantiatorCache.TryAdd(type, () => paramCtor.Invoke(null));
                    return obj;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Instantiate] Empty constructor failed for {type.Name}: {ex.Message}");
            }

            // 시도 3: Activator.CreateInstance
            try
            {
                object? obj = Activator.CreateInstance(type);
                if (obj is not null)
                {
                    LogInstantiateStrategyOnce(type, "Activator.CreateInstance");
                    InstantiatorCache.TryAdd(type, () => Activator.CreateInstance(type));
                    return obj;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Instantiate] Activator.CreateInstance failed for {type.Name}: {ex.Message}");
            }

            // 시도 4: 디버깅용 생성자 출력
            try
            {
                ConstructorInfo[] ctors = type.GetConstructors(InstanceMemberFlags);
                MelonLogger.Msg($"[Instantiate] Available constructors for {type.FullName}: {ctors.Length}");
                foreach (ConstructorInfo ctor in ctors)
                {
                    string paramsText = string.Join(", ", ctor.GetParameters().Select(p => $"{p.ParameterType.FullName} {p.Name}"));
                    MelonLogger.Msg($"  Ctor: {type.Name}({paramsText})");
                }
            }
            catch
            {
            }

            InstantiatorCache.TryAdd(type, null);
            return null;
        }

        private static bool TryInvokeSingleArgCallback(object? callback, object? arg, string logTag)
        {
            if (callback is null)
            {
                return false;
            }

            try
            {
                Type callbackType = callback.GetType();
                if (!CallbackInvokeCache.TryGetValue(callbackType, out MethodInfo? invoke))
                {
                    invoke = callbackType.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
                    CallbackInvokeCache.TryAdd(callbackType, invoke);
                }

                if (invoke is null)
                {
                    MelonLogger.Warning($"[{logTag}] 콜백 객체에서 Invoke 메서드를 찾지 못했습니다.");
                    return false;
                }

                invoke.Invoke(callback, new[] { arg });
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{logTag}] Failed to invoke callback: {ex.Message}");
                return false;
            }
        }
    }
}
