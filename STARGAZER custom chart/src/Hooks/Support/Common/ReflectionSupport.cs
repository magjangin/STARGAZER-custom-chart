using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace STARGAZER_custom_chart
{
    public sealed partial class StargazerCustomChartMod
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
                    catch (Exception ex)
                    {
                        if (LogOnce($"Reflection.GetProp.{Name}.{instance.GetType().FullName}"))
                        {
                            MelonLogger.Warning($"[Reflection] 프로퍼티 읽기 실패({instance.GetType().Name}.{Name}): {ex.Message}");
                        }
                    }
                }

                if (Field is not null)
                {
                    try
                    {
                        return Field.GetValue(instance);
                    }
                    catch (Exception ex)
                    {
                        if (LogOnce($"Reflection.GetField.{Name}.{instance.GetType().FullName}"))
                        {
                            MelonLogger.Warning($"[Reflection] 필드 읽기 실패({instance.GetType().Name}.{Name}): {ex.Message}");
                        }
                    }
                }

                if (Getter is not null)
                {
                    try
                    {
                        return Getter.Invoke(instance, Array.Empty<object>());
                    }
                    catch (Exception ex)
                    {
                        if (LogOnce($"Reflection.GetMethod.{Name}.{instance.GetType().FullName}"))
                        {
                            MelonLogger.Warning($"[Reflection] 게터 호출 실패({instance.GetType().Name}.{Name}): {ex.Message}");
                        }
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
                    catch (Exception ex)
                    {
                        if (LogOnce($"Reflection.SetProp.{Property.Name}.{owner.GetType().FullName}"))
                        {
                            MelonLogger.Warning($"[Reflection] 프로퍼티 쓰기 실패({owner.GetType().Name}.{Property.Name}): {ex.Message}");
                        }
                    }
                }

                if (Field is not null)
                {
                    try
                    {
                        Field.SetValue(owner, value);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        if (LogOnce($"Reflection.SetField.{Field.Name}.{owner.GetType().FullName}"))
                        {
                            MelonLogger.Warning($"[Reflection] 필드 쓰기 실패({owner.GetType().Name}.{Field.Name}): {ex.Message}");
                        }
                    }
                }

                if (Method is not null)
                {
                    try
                    {
                        Method.Invoke(owner, new[] { value });
                        return true;
                    }
                    catch (Exception ex)
                    {
                        if (LogOnce($"Reflection.SetMethod.{Method.Name}.{owner.GetType().FullName}"))
                        {
                            MelonLogger.Warning($"[Reflection] 세터 호출 실패({owner.GetType().Name}.{Method.Name}): {ex.Message}");
                        }
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

            CollectTypeMatches(type, pType => MatchesShortTypeName(pType, expectedTypeShortName, matchGenericArgs), nonBackingMatches, backingMatches);

            // List<T> 로 못 찾았지만 게임 업데이트로 T[] / Il2CppReferenceArray<T> / IList<T> 등
            // 다른 컬렉션 컨테이너로 바뀐 경우, 원소 타입 T가 같은 유일 컬렉션 멤버로 한 단계 더 폴백한다.
            if (nonBackingMatches.Count == 0
                && backingMatches.Count == 0
                && TryExtractListElementShortName(expectedTypeShortName, out string elementShortName))
            {
                CollectTypeMatches(type, pType => MatchesCollectionElementShortName(pType, elementShortName), nonBackingMatches, backingMatches);
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

        private static void CollectTypeMatches(
            Type type,
            Func<Type, bool> predicate,
            List<(string Name, MemberAccessor Accessor)> nonBackingMatches,
            List<(string Name, MemberAccessor Accessor)> backingMatches)
        {
            try
            {
                foreach (PropertyInfo prop in type.GetProperties(InstanceMemberFlags))
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    if (!predicate(prop.PropertyType))
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
                    if (!predicate(field.FieldType))
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
        }

        private static bool TryExtractListElementShortName(string expectedTypeShortName, out string elementShortName)
        {
            if (expectedTypeShortName.StartsWith("List<", StringComparison.OrdinalIgnoreCase)
                && expectedTypeShortName.EndsWith(">", StringComparison.Ordinal)
                && expectedTypeShortName.Length > 6)
            {
                elementShortName = expectedTypeShortName.Substring(5, expectedTypeShortName.Length - 6).Trim();
                return elementShortName.Length > 0;
            }

            elementShortName = string.Empty;
            return false;
        }

        private static bool MatchesCollectionElementShortName(Type memberType, string elementShortName)
        {
            if (memberType.IsArray)
            {
                Type? elem = memberType.GetElementType();
                return elem is not null && MatchesShortTypeName(elem, elementShortName, matchGenericArgs: false);
            }

            if (!memberType.IsGenericType)
            {
                return false;
            }

            Type[] args = memberType.GetGenericArguments();
            if (args.Length != 1)
            {
                return false;
            }

            string containerName = StripGenericArity(memberType.Name);
            bool isCollectionContainer = containerName.IndexOf("List", StringComparison.OrdinalIgnoreCase) >= 0
                || containerName.IndexOf("Array", StringComparison.OrdinalIgnoreCase) >= 0
                || containerName.IndexOf("Collection", StringComparison.OrdinalIgnoreCase) >= 0
                || containerName.IndexOf("Enumerable", StringComparison.OrdinalIgnoreCase) >= 0;

            return isCollectionContainer && MatchesShortTypeName(args[0], elementShortName, matchGenericArgs: false);
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

        // candidates는 밑줄 없는 소문자(예: "displayname")로 넘긴다.
        // exact면 정규화한 이름이 후보와 완전히 같아야 하고, 아니면 포함만 해도 된다.
        private static bool NameMatchesAny(string sourceName, IReadOnlyList<string> candidates, bool exact = false)
        {
            string normalized = sourceName.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (exact
                    ? string.Equals(normalized, candidates[i], StringComparison.Ordinal)
                    : normalized.Contains(candidates[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TrySetValueByNameCandidates(object owner, IReadOnlyList<string> names, object? value)
        {
            if (owner is null || names.Count == 0)
            {
                return false;
            }

            Type type = owner.GetType();
            var key = (type, GetCandidatesKey(names));

            if (!MemberSetterCache.TryGetValue(key, out MemberSetter? setter))
            {
                setter = ResolveMemberSetter(type, names);
                MemberSetterCache.TryAdd(key, setter);
            }

            return !setter.IsEmpty && setter.TrySetValue(owner, value);
        }

        // 후보 목록 -> 캐시 키 문자열. BMS 주입 루프는 노트마다 같은 정적 배열로 부르므로, 후보가 여럿이면
        // 매번 string.Join으로 키를 만들던 할당을 목록 인스턴스별 1회로 줄인다. ConditionalWeakTable이라
        // 호출부가 임시 배열을 넘겨도 배열이 수거될 때 항목도 같이 사라져 누수가 없다.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<string>, string> CandidatesKeyCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<string>, string>();

        private static string GetCandidatesKey(IReadOnlyList<string> names)
        {
            if (names.Count == 1)
            {
                return names[0];
            }

            return CandidatesKeyCache.GetValue(names, list => string.Join("|", list));
        }

        // 1차: 정규화 이름이 후보와 "완전히 같은" 프로퍼티/필드. 2차: 이름에 후보를 "포함"하는 것(예전 동작).
        // 포함 일치만 쓰면 "displayname"이 선언 순서에 따라 displayNameEN에 먼저 걸릴 수 있고,
        // "targetlayer"가 <TargetLayer>k__BackingField에 걸릴 수 있다. 정확 일치를 먼저 보면 그런 오접속이 없다.
        private static MemberSetter ResolveMemberSetter(Type type, IReadOnlyList<string> names)
        {
            PropertyInfo[] properties = type.GetProperties(InstanceMemberFlags);
            FieldInfo[] fields = type.GetFields(InstanceMemberFlags);

            foreach (bool exact in new[] { true, false })
            {
                foreach (PropertyInfo property in properties)
                {
                    if (!property.CanWrite || property.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    if (NameMatchesAny(property.Name, names, exact))
                    {
                        return new MemberSetter(property, null, null);
                    }
                }

                foreach (FieldInfo field in fields)
                {
                    if (field.IsInitOnly)
                    {
                        continue;
                    }

                    if (NameMatchesAny(field.Name, names, exact))
                    {
                        return new MemberSetter(null, field, null);
                    }
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
    }
}
