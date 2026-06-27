using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static void TryPatchTrackLoaderOnLoadedCallback(object[] args)
        {
            if (args.Length == 0 || args[0] is null)
            {
                return;
            }

            try
            {
                object callback = args[0];
                Type delegateType = callback.GetType();

                MethodInfo? invokeMethod = delegateType.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
                if (invokeMethod is null)
                {
                    return;
                }

                string invokeMethodKey = BuildMethodPatchKey(invokeMethod);
                if (!TrackLoaderCallbackPatchedMethods.Add(invokeMethodKey))
                {
                    return;
                }

                RuntimeHarmonyInstance.Patch(
                    invokeMethod,
                    postfix: new HarmonyMethod(typeof(GameTypeEnumeratorMod).GetMethod(nameof(TrackLoaderOnLoadedCallbackPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackLoader] failed to patch onLoaded callback: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void TrackLoaderOnLoadedCallbackPostfix(MethodBase __originalMethod, object[]? __args)
        {
            try
            {
                object[] args = __args ?? Array.Empty<object>();
                if (args.Length == 0 || args[0] is null)
                {
                    return;
                }

                // IL2CPP는 참조 타입 제네릭 메서드의 네이티브 구현을 공유하는 경우가 있어,
                // Action<List<ITrackData>>.Invoke에 건 패치가 Action<AudioClip>.Invoke 등
                // 전혀 다른 콜백 호출에도 잘못 걸릴 수 있다(관측됨: LoadPreviewClip 콜백 호출 시
                // 이 postfix가 같이 발동해 "Loaded 0 tracks successfully"를 잘못 찍는 문제).
                // 트랙 목록이 아닌 값(Unity 오브젝트나 비-열거형)이면 조용히 무시한다.
                if (args[0] is UnityEngine.Object || args[0] is not IEnumerable)
                {
                    return;
                }

                if (!TrackLoaderListLogged)
                {
                    TrackLoaderListLogged = true;
                    int loadedCount = EnumerateCollectionItems(args[0], 2048).Count();
                    MelonLogger.Msg($"[TrackLoader] onLoaded callback invoked. Loaded {loadedCount} tracks successfully.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackLoader] failed to inspect loaded track list: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}