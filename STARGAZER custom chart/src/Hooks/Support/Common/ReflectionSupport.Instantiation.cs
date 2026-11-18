using System;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace STARGAZER_custom_chart
{
    public sealed partial class StargazerCustomChartMod
    {
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
