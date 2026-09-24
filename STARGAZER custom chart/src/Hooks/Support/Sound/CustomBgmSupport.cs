using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Networking;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static void LogBgmDebugFileInfo()
        {
            foreach (CustomAlbum album in CustomAlbumRegistry.GetAlbums())
            {
                if (album.MusicPath is null || !File.Exists(album.MusicPath))
                {
                    MelonLogger.Warning($"[BgmDebug] 음원 없음: {album.Name}");
                    continue;
                }

                var file = new FileInfo(album.MusicPath);
                MelonLogger.Msg($"[BgmDebug] 음원 확인({album.Name}): {file.Name} bytes={file.Length} modified={file.LastWriteTime:O}");
            }
        }

        private static readonly Dictionary<string, AudioClip> CustomBgmClipCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DownloadHandlerAudioClip> CustomBgmDownloadHandlerCache = new Dictionary<string, DownloadHandlerAudioClip>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> CustomBgmLoadsInProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<object>> PendingCustomBgmCallbacks = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);

        // 앨범마다 음원/프리뷰를 미리 로드해 둔다. 곡 선택에서 커서를 옮기는 즉시 프리뷰가
        // 나와야 하는데, 그때 로드를 시작하면 한 박자 늦기 때문이다.
        // 클립은 압축 상태(Vorbis)로 메모리에 두므로(TrySetCompressedInMemory) 앨범이 늘어도
        // 곡당 대략 파일 크기만큼만 차지한다. PCM으로 풀면 3분 스테레오 곡 하나가 수십 MB다.
        private static void StartCustomBgmPreload()
        {
            foreach (CustomAlbum album in CustomAlbumRegistry.GetAlbums())
            {
                if (album.MusicPath is not null)
                {
                    StartCustomAudioClipLoad(album.MusicPath);
                }

                if (album.PreviewPath is not null && !string.Equals(album.PreviewPath, album.MusicPath, StringComparison.OrdinalIgnoreCase))
                {
                    StartCustomAudioClipLoad(album.PreviewPath);
                }
            }
        }

        private static void UpdateCustomChartPlayState(object? travelArgs)
        {
            IsCustomChartPlayActive = false;
            CurrentPlayAlbum = null;
            if (travelArgs is null)
            {
                MelonLogger.Msg("[BgmDebug] play state travelArgs=<null>");
                return;
            }

            // 플레이 화면 난이도 표시에 쓰려고 이 시점에 재생 난이도를 기억해 둔다.
            CustomChartPlayLevelKey = TryGetMemberValue(travelArgs, travelArgs.GetType(), "PlayLevel")?.ToString();

            object? track = TryGetMemberValue(travelArgs, travelArgs.GetType(), "PlayTrack");
            if (track is null)
            {
                MelonLogger.Msg("[BgmDebug] play state track=<null>");
                return;
            }

            string title = TryGetMemberValue(track, track.GetType(), "TrackDisplayName")?.ToString()
                ?? TryGetMemberValue(track, track.GetType(), "TrackDisplayNameEN")?.ToString()
                ?? string.Empty;
            // 표시명은 사용자가 바꿀 수 있고, TrackID는 복제 원본인 공식 트랙과 동일하다.
            // 우리가 주입한 객체인지(객체 동일성)로만 확실하게 판별할 수 있다.
            // 재생 중인 앨범은 BMS 차트 주입과 플레이 화면 난이도 표시가 쓴다.
            CurrentPlayAlbum = TryGetAlbumForTrack(track);
            IsCustomChartPlayActive = CurrentPlayAlbum is not null;
            MelonLogger.Msg($"[BgmDebug] play state custom={IsCustomChartPlayActive} album={CurrentPlayAlbum?.Name ?? "<none>"} title={title}");
        }

        // 공용 PRE 훅이 모든 대상에서 부르므로, 로그를 남길 메서드인지 먼저 거른 뒤에만
        // 인스턴스 설명(리플렉션 + 문자열 조립)을 만든다.
        private static void LogBgmDebugInvocation(MethodBase method, object? instance, object[] args)
        {
            string methodName = method.Name;
            if (!string.Equals(methodName, "PlayBGM", StringComparison.Ordinal)
                && !string.Equals(methodName, "LoadBGMClip", StringComparison.Ordinal)
                && !string.Equals(methodName, "StopBGM", StringComparison.Ordinal))
            {
                return;
            }

            string typeName = method.DeclaringType?.FullName ?? "<unknown>";
            string instanceText = DescribeObjectIdentity(instance);
            if (string.Equals(methodName, "PlayBGM", StringComparison.Ordinal))
            {
                string clip = DescribeAudioClip(args.Length > 0 ? args[0] : null);
                string soundType = args.Length > 1 ? args[1]?.ToString() ?? "<null>" : "<missing>";
                MelonLogger.Msg($"[BgmDebug][PlayBGM] owner={typeName} instance={instanceText} soundType={soundType} inPlay={IsInPlayScene} custom={IsCustomChartPlayActive} clip={clip}");
            }
            else if (string.Equals(methodName, "LoadBGMClip", StringComparison.Ordinal))
            {
                string callback = args.Length > 0 && args[0] is not null
                    ? args[0]!.GetType().FullName ?? args[0]!.GetType().Name
                    : "<null>";
                MelonLogger.Msg($"[BgmDebug][LoadBGMClip] owner={typeName} instance={instanceText} inPlay={IsInPlayScene} custom={IsCustomChartPlayActive} callback={callback}");
            }
            else
            {
                MelonLogger.Msg($"[BgmDebug][StopBGM] owner={typeName} instance={instanceText} inPlay={IsInPlayScene} custom={IsCustomChartPlayActive}");
            }
        }

        private static string DescribeAudioClip(object? value)
        {
            if (value is not AudioClip clip)
            {
                return value is null ? "<null>" : $"<{value.GetType().FullName ?? value.GetType().Name}>";
            }

            try
            {
                return $"name={clip.name},instanceId={clip.GetInstanceID()},pointer={DescribePointer(clip)},length={clip.length:0.###},samples={clip.samples},channels={clip.channels},frequency={clip.frequency},loadState={clip.loadState}";
            }
            catch (Exception ex)
            {
                return $"<AudioClip inspect failed: {ex.GetType().Name}>";
            }
        }

        private static string DescribeObjectIdentity(object? value)
        {
            if (value is null)
            {
                return "<null>";
            }

            return $"{value.GetType().FullName ?? value.GetType().Name}@{DescribePointer(value)}";
        }

        private static string DescribePointer(object value)
        {
            IntPtr ptr = TryGetIl2CppPointer(value);
            return ptr == IntPtr.Zero ? "<unknown>" : $"0x{ptr.ToInt64():X}";
        }

        private static void InvokeActionOfAudioClip(object? callback, AudioClip? clip)
        {
            if (callback == null) return;
            try
            {
                MethodInfo? invoke = callback.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
                if (invoke != null)
                {
                    invoke.Invoke(callback, new object?[] { clip });
                }
                else
                {
                    MelonLogger.Warning("[CustomBgm] 콜백 객체에서 Invoke 메서드를 찾지 못했습니다.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CustomBgm] Failed to invoke callback: {ex.Message}");
            }
        }

        private static void StartCustomAudioClipLoad(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (CustomBgmClipCache.TryGetValue(fullPath, out AudioClip? cached))
            {
                if (cached != null)
                {
                    return;
                }

                CustomBgmClipCache.Remove(fullPath);
            }

            if (!File.Exists(fullPath)
                || !CustomBgmLoadsInProgress.Add(fullPath))
            {
                return;
            }

            MelonCoroutines.Start(LoadCustomAudioClipCoroutine(fullPath));
        }

        // "file:///" + 경로를 그대로 쓰면 폴더/파일 이름의 '%'나 '#'가 URI의 이스케이프/조각(fragment)으로
        // 해석돼 로드가 실패한다. 그 두 글자만 이스케이프해서, 지금 잘 되는 경로(한글·일본어 포함)는
        // 한 글자도 바뀌지 않게 한다.
        private static string BuildFileUri(string filePath)
        {
            string path = filePath.Replace('\\', '/')
                .Replace("%", "%25", StringComparison.Ordinal)
                .Replace("#", "%23", StringComparison.Ordinal);
            return "file:///" + path;
        }

        // DownloadHandlerAudioClip.compressed = true: 클립을 Vorbis 압축 상태로 메모리에 둔다.
        // 게임 빌드에서 이 세터가 스트리핑됐을 수도 있어 컴파일 타임 참조 대신 리플렉션으로 시도하고,
        // 없으면 예전처럼(해제된 PCM) 동작한다. config.txt의 CompressBgmInMemory=0이면 시도하지 않는다.
        private static void TrySetCompressedInMemory(DownloadHandlerAudioClip downloadHandler)
        {
            if (!CustomConfig.CompressBgmInMemory)
            {
                return;
            }

            try
            {
                PropertyInfo? compressed = downloadHandler.GetType().GetProperty("compressed", BindingFlags.Instance | BindingFlags.Public);
                if (compressed is not null && compressed.CanWrite)
                {
                    compressed.SetValue(downloadHandler, true);
                    return;
                }

                if (LogOnce("CustomBgm.compressed.missing"))
                {
                    MelonLogger.Warning("[CustomBgm] DownloadHandlerAudioClip.compressed를 찾지 못해 음원을 풀어서(PCM) 메모리에 둡니다.");
                }
            }
            catch (Exception ex)
            {
                if (LogOnce("CustomBgm.compressed.failed"))
                {
                    MelonLogger.Warning($"[CustomBgm] compressed 설정 실패, 풀어서 로드합니다: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static IEnumerator LoadCustomAudioClipCoroutine(string filePath)
        {
            string uri = BuildFileUri(filePath);
            MelonLogger.Msg($"[CustomBgm] Starting load from uri: {uri}");
            UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS);
            www.disposeDownloadHandlerOnDispose = false;
            DownloadHandlerAudioClip? downloadHandler = www.downloadHandler as DownloadHandlerAudioClip;
            try
            {
                if (downloadHandler != null)
                {
                    downloadHandler.streamAudio = false;
                    TrySetCompressedInMemory(downloadHandler);
                }

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    MelonLogger.Error($"[CustomBgm] 커스텀 BGM 로드에 실패했습니다: {www.error}");
                    CompletePendingCustomBgmCallbacks(filePath, null);
                }
                else
                {
                    AudioClip? clip = DownloadHandlerAudioClip.GetContent(www);
                    if (clip is not null)
                    {
                        clip.name = "CustomBGM_" + Path.GetFileNameWithoutExtension(filePath);
                        clip.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                        CustomBgmClipCache[filePath] = clip;
                        if (downloadHandler is not null)
                        {
                            CustomBgmDownloadHandlerCache[filePath] = downloadHandler;
                        }
                        MelonLogger.Msg($"[CustomBgm] Cached custom BGM: {filePath} clip={DescribeAudioClip(clip)}");
                        CompletePendingCustomBgmCallbacks(filePath, clip);
                    }
                    else
                    {
                        MelonLogger.Error("[CustomBgm] 로드된 클립이 null입니다!");
                        CompletePendingCustomBgmCallbacks(filePath, null);
                    }
                }
            }
            finally
            {
                CustomBgmLoadsInProgress.Remove(filePath);
                www.Dispose();
            }
        }

        private static void QueueCustomBgmCallback(string path, object callback)
        {
            string fullPath = Path.GetFullPath(path);
            if (!PendingCustomBgmCallbacks.TryGetValue(fullPath, out List<object>? callbacks))
            {
                callbacks = new List<object>();
                PendingCustomBgmCallbacks[fullPath] = callbacks;
            }

            callbacks.Add(callback);
            StartCustomAudioClipLoad(fullPath);
            MelonLogger.Msg($"[CustomBgm] Queued callback while cache loads: {fullPath} pending={callbacks.Count}");
        }

        private static void CompletePendingCustomBgmCallbacks(string path, AudioClip? clip)
        {
            if (!PendingCustomBgmCallbacks.Remove(path, out List<object>? callbacks))
            {
                return;
            }

            foreach (object callback in callbacks)
            {
                InvokeActionOfAudioClip(callback, clip);
            }

            MelonLogger.Msg($"[CustomBgm] Completed pending callbacks: {path} count={callbacks.Count} clipReady={clip != null}");
        }

        private static bool TryGetCachedCustomBgm(string path, out AudioClip? clip)
        {
            string fullPath = Path.GetFullPath(path);
            if (CustomBgmClipCache.TryGetValue(fullPath, out AudioClip? cached) && cached != null)
            {
                clip = cached;
                return true;
            }

            CustomBgmClipCache.Remove(fullPath);
            StartCustomAudioClipLoad(fullPath);
            clip = null;
            return false;
        }

        [HarmonyPatch]
        private static class CustomBgmLoaderPatch
        {
            private static readonly List<MethodBase> Targets = new List<MethodBase>();

            private static bool Prepare() => PrepareTargets(Targets, "CustomBgm", new[]
            {
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadBGMClip", 1, "Action"),
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadPreviewClip", 1, "Action"),
            });

            private static IEnumerable<MethodBase> TargetMethods() => Targets;

            private static bool Prefix(MethodBase __originalMethod, object __instance, object[] __args)
            {
                try
                {
                    if (__args.Length == 0 || __args[0] is null) return true;

                    // 공식 트랙을 건드리지 않도록 우리가 주입한 객체인지로 판별한다(TrackID는 원본과 동일해 못 씀).
                    // 어느 앨범 폴더의 음원을 쓸지도 이 조회 결과로 정해진다.
                    CustomAlbum? album = TryGetAlbumForTrack(__instance);
                    if (album is not null)
                    {
                        string? path = string.Equals(__originalMethod.Name, "LoadPreviewClip", StringComparison.Ordinal)
                            ? album.PreviewPath
                            : album.MusicPath;

                        if (path is not null && File.Exists(path))
                        {
                            if (TryGetCachedCustomBgm(path, out AudioClip? clip) && clip is not null)
                            {
                                MelonLogger.Msg($"[CustomBgm] Serving cached {__originalMethod.Name} for '{album.DisplayName}': {path}");
                                InvokeActionOfAudioClip(__args[0], clip);
                                return false;
                            }

                            QueueCustomBgmCallback(path, __args[0]);
                            return false;
                        }
                        else
                        {
                            MelonLogger.Warning($"[CustomBgm] 음원을 찾지 못했습니다({album.Name}): {path ?? "<none>"}. 기본값으로 되돌립니다.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[CustomBgm] Error in CustomBgmLoaderPatch.Prefix: {ex.Message}");
                }
                return true;
            }
        }
    }
}
