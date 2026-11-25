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
    public sealed partial class StargazerCustomChartMod
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

        private sealed class BgmCacheEntry
        {
            public BgmCacheEntry(AudioClip clip, DateTime lastWriteUtc, long lastAccessSeq)
            {
                Clip = clip;
                LastWriteUtc = lastWriteUtc;
                LastAccessSeq = lastAccessSeq;
            }

            public AudioClip Clip { get; }
            public DateTime LastWriteUtc { get; }
            public long LastAccessSeq { get; set; }
        }

        // 메인 스레드 전용: Unity 코루틴(MelonCoroutines)과 Harmony 훅 모두 메인 스레드에서 실행되므로 락 없이 접근한다.
        private static readonly Dictionary<string, BgmCacheEntry> CustomBgmClipCache =
            new Dictionary<string, BgmCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DownloadHandlerAudioClip> CustomBgmDownloadHandlerCache =
            new Dictionary<string, DownloadHandlerAudioClip>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> CustomBgmFailedCache =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> CustomBgmLoadsInProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<object>> PendingCustomBgmCallbacks = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
        private static long _bgmAccessSeq;

        private static void EvictBgmCacheEntry(string fullPath)
        {
            if (CustomBgmClipCache.Remove(fullPath, out BgmCacheEntry? entry) && entry.Clip != null)
            {
                UnityEngine.Object.Destroy(entry.Clip);
            }

            if (CustomBgmDownloadHandlerCache.Remove(fullPath, out DownloadHandlerAudioClip? handler))
            {
                handler.Dispose();
            }
        }

        private static void EnforceBgmCacheLimit(string keepPath)
        {
            int maxEntries = CustomConfig.BgmCacheMaxEntries;
            if (maxEntries <= 0)
            {
                return;
            }

            while (CustomBgmClipCache.Count >= maxEntries)
            {
                string? oldestKey = null;
                long oldestSeq = long.MaxValue;
                foreach (var kvp in CustomBgmClipCache)
                {
                    if (string.Equals(kvp.Key, keepPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (kvp.Value.LastAccessSeq < oldestSeq)
                    {
                        oldestSeq = kvp.Value.LastAccessSeq;
                        oldestKey = kvp.Key;
                    }
                }

                if (oldestKey is null)
                {
                    break;
                }

                EvictBgmCacheEntry(oldestKey);
            }
        }

        // 앨범마다 전용 프리뷰 파일만 미리 디코딩해 둔다. 본 음원은 플레이 진입 시 지연 로딩하여
        // 커스텀 곡이 많아져도 시작 시간과 메모리 사용량이 급증하지 않게 한다.
        private static void StartCustomBgmPreload()
        {
            foreach (CustomAlbum album in CustomAlbumRegistry.GetAlbums())
            {
                if (album.PreviewPath is not null
                    && !string.Equals(album.PreviewPath, album.MusicPath, StringComparison.OrdinalIgnoreCase))
                {
                    StartCustomAudioClipLoad(album.PreviewPath);
                }
            }
        }

        private static void ResetPlaySessionState()
        {
            IsCustomChartPlayActive = false;
            CurrentPlayAlbum = null;
            CustomChartPlayLevelKey = null;
            LastProbedPatternPtr = IntPtr.Zero;
            WrappedPatternLoaders.Clear();
        }

        private static void UpdateCustomChartPlayState(object? travelArgs)
        {
            ResetPlaySessionState();
            if (travelArgs is null)
            {
                MelonLogger.Msg("[BgmDebug] play state travelArgs=<null>");
                return;
            }

            // 플레이 화면 난이도 표시에 쓰려고 이 시점에 재생 난이도를 기억해 둔다.
            // 업데이트로 TravelArgs 프로퍼티 이름이 바뀌어도 ELevels/ITrackData 유일 멤버 폴백으로 살아남도록 한다.
            CustomChartPlayLevelKey = TryGetMemberByType(travelArgs, "ELevels", "PlayLevel", "playLevel", "Level", "level")?.ToString();

            object? track = TryGetMemberByType(travelArgs, "ITrackData", "PlayTrack", "playTrack", "Track", "track");
            if (track is null)
            {
                MelonLogger.Msg("[BgmDebug] play state track=<null>");
                return;
            }

            string title = TryGetMemberValue(track, "TrackDisplayName", "TrackDisplayNameEN", "DisplayName", "displayName")?.ToString()
                ?? string.Empty;
            // 표시명은 사용자가 바꿀 수 있고, TrackID는 복제 원본인 공식 트랙과 동일하다.
            // 우리가 주입한 객체인지(객체 동일성)로만 확실하게 판별할 수 있다.
            // 재생 중인 앨범은 BMS 차트 주입과 플레이 화면 난이도 표시가 쓴다.
            CurrentPlayAlbum = TryGetAlbumForTrack(track);
            IsCustomChartPlayActive = CurrentPlayAlbum is not null;
            MelonLogger.Msg($"[BgmDebug] play state custom={IsCustomChartPlayActive} album={CurrentPlayAlbum?.Name ?? "<none>"} title={title}");
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

        private static string DescribePointer(object value)
        {
            IntPtr ptr = TryGetIl2CppPointer(value);
            return ptr != IntPtr.Zero ? $"0x{ptr.ToInt64():X}" : "<unknown>";
        }

        private enum CustomAudioLoadState
        {
            // 파일 없음·같은 파일로 이미 디코딩 실패·코루틴 시작 실패. 콜백을 걸어 두면 영원히 불리지 않는다.
            Unavailable,
            // 디코딩이 진행 중(방금 시작했거나 이미 돌고 있음). 끝나면 CompletePendingCustomBgmCallbacks가 콜백을 부른다.
            Loading,
            // 캐시에 최신 클립이 있다(readyClip).
            Ready,
        }

        private static CustomAudioLoadState StartCustomAudioClipLoad(string filePath) => StartCustomAudioClipLoad(filePath, out _);

        private static CustomAudioLoadState StartCustomAudioClipLoad(string filePath, out AudioClip? readyClip)
        {
            readyClip = null;
            string fullPath = ToServePath(filePath);
            DateTime lastWriteUtc = GetLastWriteTimeUtcFresh(fullPath);
            if (lastWriteUtc == MissingFileStamp)
            {
                EvictBgmCacheEntry(fullPath);
                CustomBgmFailedCache.Remove(fullPath);
                return CustomAudioLoadState.Unavailable;
            }

            if (CustomBgmFailedCache.TryGetValue(fullPath, out DateTime failedWriteUtc))
            {
                if (failedWriteUtc == lastWriteUtc)
                {
                    return CustomAudioLoadState.Unavailable;
                }

                CustomBgmFailedCache.Remove(fullPath);
            }

            if (CustomBgmClipCache.TryGetValue(fullPath, out BgmCacheEntry? cached))
            {
                if (cached.Clip != null && cached.LastWriteUtc == lastWriteUtc)
                {
                    cached.LastAccessSeq = ++_bgmAccessSeq;
                    readyClip = cached.Clip;
                    return CustomAudioLoadState.Ready;
                }

                EvictBgmCacheEntry(fullPath);
            }

            if (!CustomBgmLoadsInProgress.Add(fullPath))
            {
                return CustomAudioLoadState.Loading;
            }

            try
            {
                MelonCoroutines.Start(LoadCustomAudioClipCoroutine(fullPath, lastWriteUtc));
                return CustomAudioLoadState.Loading;
            }
            catch (Exception ex)
            {
                // 진행 중 표시를 남겨 두면 이후 요청이 전부 "Loading"으로 보여 콜백이 영원히 대기한다.
                CustomBgmLoadsInProgress.Remove(fullPath);
                MelonLogger.Warning($"[CustomBgm] 디코딩 코루틴 시작 실패: {fullPath}: {ex.GetType().Name}: {ex.Message}");
                return CustomAudioLoadState.Unavailable;
            }
        }

        private static AudioType ResolveAudioType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".ogg" => AudioType.OGGVORBIS,
                ".mp3" => AudioType.MPEG,
                ".wav" => AudioType.WAV,
                ".aif" or ".aiff" => AudioType.AIFF,
                _ => AudioType.UNKNOWN,
            };
        }

        private static IEnumerator LoadCustomAudioClipCoroutine(string filePath, DateTime lastWriteUtc)
        {
            string uri = new Uri(filePath).AbsoluteUri;
            AudioType audioType = ResolveAudioType(filePath);
            MelonLogger.Msg($"[CustomBgm] Starting load from uri: {uri} (audioType={audioType})");
            UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType);
            www.disposeDownloadHandlerOnDispose = false;
            DownloadHandlerAudioClip? downloadHandler = www.downloadHandler as DownloadHandlerAudioClip;
            bool cachedHandler = false;
            AudioClip? completedClip = null;
            try
            {
                if (downloadHandler != null)
                {
                    downloadHandler.streamAudio = false;
                }

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    CustomBgmFailedCache[filePath] = lastWriteUtc;
                    MelonLogger.Error($"[CustomBgm] 커스텀 BGM 로드에 실패했습니다: {www.error}");
                }
                else
                {
                    AudioClip? clip = DownloadHandlerAudioClip.GetContent(www);
                    if (clip is not null)
                    {
                        clip.name = "CustomBGM_" + Path.GetFileNameWithoutExtension(filePath);
                        clip.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                        EvictBgmCacheEntry(filePath);
                        EnforceBgmCacheLimit(filePath);
                        CustomBgmFailedCache.Remove(filePath);
                        CustomBgmClipCache[filePath] = new BgmCacheEntry(clip, lastWriteUtc, ++_bgmAccessSeq);
                        if (downloadHandler is not null)
                        {
                            CustomBgmDownloadHandlerCache[filePath] = downloadHandler;
                            cachedHandler = true;
                        }
                        completedClip = clip;
                        MelonLogger.Msg($"[CustomBgm] Cached custom BGM: {filePath} clip={DescribeAudioClip(clip)}");
                    }
                    else
                    {
                        CustomBgmFailedCache[filePath] = lastWriteUtc;
                        MelonLogger.Error("[CustomBgm] 로드된 클립이 null입니다!");
                    }
                }
            }
            finally
            {
                CustomBgmLoadsInProgress.Remove(filePath);
                CompletePendingCustomBgmCallbacks(filePath, completedClip);
                if (!cachedHandler)
                {
                    downloadHandler?.Dispose();
                }
                www.Dispose();
            }
        }

        // 디코딩을 시작(또는 진행 중인 디코딩에 합류)하고 콜백을 대기열에 건다. 반환값:
        //  - Loading: 콜백을 걸었다(원본 로더는 건너뛴다).
        //  - Ready: 그 사이 캐시가 준비됐다(readyClip으로 바로 서빙).
        //  - Unavailable: 콜백을 걸지 않았다(원본 로더로 되돌린다).
        // 예전엔 콜백을 먼저 걸고 로드를 시작했는데, 스로틀된 존재 확인(최대 2초 묵은 값)과 달리 실제로는 파일이
        // 지워졌거나 같은 파일로 이미 실패한 상태면 로드가 시작되지 않아 콜백이 영원히 불리지 않았다(곡 로딩 무한 대기).
        private static CustomAudioLoadState TryQueueCustomBgmCallback(string fullPath, object callback, out AudioClip? readyClip)
        {
            CustomAudioLoadState state = StartCustomAudioClipLoad(fullPath, out readyClip);
            if (state != CustomAudioLoadState.Loading)
            {
                return state;
            }

            if (!PendingCustomBgmCallbacks.TryGetValue(fullPath, out List<object>? callbacks))
            {
                callbacks = new List<object>();
                PendingCustomBgmCallbacks[fullPath] = callbacks;
            }

            callbacks.Add(callback);
            MelonLogger.Msg($"[CustomBgm] Queued callback while cache loads: {fullPath} pending={callbacks.Count}");
            return CustomAudioLoadState.Loading;
        }

        private static void CompletePendingCustomBgmCallbacks(string path, AudioClip? clip)
        {
            if (!PendingCustomBgmCallbacks.Remove(path, out List<object>? callbacks))
            {
                return;
            }

            foreach (object callback in callbacks)
            {
                TryInvokeSingleArgCallback(callback, clip, "CustomBgm");
            }

            MelonLogger.Msg($"[CustomBgm] Completed pending callbacks: {path} count={callbacks.Count} clipReady={clip != null}");
        }

        // 캐시 조회 전용(fullPath는 Path.GetFullPath로 정규화된 값). 파일 수정 시각이 바뀌었으면 캐시를 무효화해 핫리로드를 지원한다.
        private static bool TryGetCachedCustomBgm(string fullPath, out AudioClip? clip)
        {
            if (CustomBgmClipCache.TryGetValue(fullPath, out BgmCacheEntry? cached))
            {
                if (cached.Clip != null && GetLastWriteTimeUtcThrottled(fullPath) == cached.LastWriteUtc)
                {
                    cached.LastAccessSeq = ++_bgmAccessSeq;
                    clip = cached.Clip;
                    return true;
                }

                // Unity 쪽에서 파괴됐거나 디스크 파일이 수정된 경우. 기존 리소스를 즉시 해제한다.
                EvictBgmCacheEntry(fullPath);
            }

            clip = null;
            return false;
        }

        private static bool IsKnownFailedAudio(string fullPath)
        {
            if (!CustomBgmFailedCache.TryGetValue(fullPath, out DateTime failedWriteUtc))
            {
                return false;
            }

            if (GetLastWriteTimeUtcThrottled(fullPath) == failedWriteUtc)
            {
                return true;
            }

            CustomBgmFailedCache.Remove(fullPath);
            return false;
        }

        [HarmonyPatch]
        private static class CustomBgmLoaderPatch
        {
            private static readonly List<MethodBase> Targets = new List<MethodBase>();

            private static bool Prepare() => PrepareTargets(Targets, new[]
            {
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadBGMClip", 1, "Action")
                    .WithAlternates("LoadBgmClip", "LoadBGM", "LoadAudioClip"),
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadPreviewClip", 1, "Action")
                    .WithAlternates("LoadPreviewBGM", "LoadPreviewBgm", "LoadPreviewAudioClip"),
            }, "커스텀 BGM/프리뷰");

            private static IEnumerable<MethodBase> TargetMethods() => Targets;

            private static bool Prefix(MethodBase __originalMethod, object __instance, object[] __args)
            {
                try
                {
                    if (__args.Length == 0 || __args[0] is null) return true;

                    // 공식 트랙을 건드리지 않도록 우리가 주입한 객체인지로 판별한다(TrackID는 원본과 동일해 못 씀).
                    // 어느 앨범 폴더의 음원을 쓸지도 이 조회 결과로 정해진다.
                    CustomAlbum? album = TryGetAlbumForTrack(__instance);
                    if (album is null)
                    {
                        return true;
                    }

                    // 대체 이름(예: LoadPreviewBGM)으로 연결된 경우에도 프리뷰 역할을 그대로 유지하도록 논리 이름을 먼저 본다.
                    string? logicalName = TryGetResolvedTargetLogicalName(__originalMethod);
                    bool isPreview = logicalName is not null
                        ? logicalName.EndsWith(".LoadPreviewClip", StringComparison.Ordinal)
                        : __originalMethod.Name.IndexOf("Preview", StringComparison.OrdinalIgnoreCase) >= 0;
                    string? path = isPreview ? album.PreviewPath : album.MusicPath;
                    // 앨범 경로는 로드 시 이미 정규화돼 있다(CustomAlbum.TryLoad) — 커서 이동마다 GetFullPath 생략.
                    string? fullPath = path is null ? null : ToServePath(path);

                    // 캐시 적중 시에는 수정 시각(핫리로드)만 확인하고 즉시 서빙한다.
                    if (fullPath is not null && TryGetCachedCustomBgm(fullPath, out AudioClip? clip) && clip is not null)
                    {
                        if (LogOnce($"CustomBgm.Serve.{__originalMethod.Name}.{fullPath}"))
                        {
                            MelonLogger.Msg($"[CustomBgm] Serving cached {__originalMethod.Name} for '{album.DisplayName}': {fullPath} (이후 같은 요청은 로그 생략)");
                        }

                        TryInvokeSingleArgCallback(__args[0], clip, "CustomBgm");
                        return false;
                    }

                    bool fileExists = fullPath is not null && FileExistsThrottled(fullPath);
                    bool startFailed = false;
                    if (fullPath is not null && fileExists && !IsKnownFailedAudio(fullPath))
                    {
                        switch (TryQueueCustomBgmCallback(fullPath, __args[0], out AudioClip? readyClip))
                        {
                            case CustomAudioLoadState.Loading:
                                return false;
                            case CustomAudioLoadState.Ready when readyClip is not null:
                                TryInvokeSingleArgCallback(__args[0], readyClip, "CustomBgm");
                                return false;
                            default:
                                // 스로틀된 존재 확인과 달리 지금 보니 파일이 없거나 이미 실패한 파일이다 → 원본으로.
                                startFailed = true;
                                break;
                        }
                    }

                    // 곡 선택 커서가 움직일 때마다(프리뷰) 불리는 경로라 같은 원인은 1회만 남긴다(로그 파일 쓰기 폭주 방지).
                    // 파일을 고치면 IsKnownFailedAudio가 수정 시각으로 실패 캐시를 풀어 다시 시도한다.
                    string reason = path is null ? "음원 경로 없음"
                        : startFailed ? "로드 시작 불가(파일 없음/이전 디코딩 실패)"
                        : (fileExists ? "이전 디코딩 실패" : "파일 없음");
                    if (LogOnce($"CustomBgm.Missing.{album.Name}.{(isPreview ? "preview" : "bgm")}.{reason}"))
                    {
                        MelonLogger.Warning($"[CustomBgm] 커스텀 음원을 쓸 수 없어 원본으로 되돌립니다({album.Name}, {reason}): {path ?? "<none>"}");
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
