using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace STARGAZER_custom_chart
{
    public sealed partial class StargazerCustomChartMod
    {
        // INNER_TrackData.LoadJacketSprite(Action<Sprite>)는 LoadBGMClip/LoadPreviewClip과
        // 시그니처가 동일한 트랙별 콜백 로더다(decompiled/Assembly-CSharp/Il2CppStargazer/TrackLoader.cs).
        private sealed class JacketCacheEntry
        {
            public JacketCacheEntry(Sprite sprite, DateTime lastWriteUtc, long lastAccessSeq)
            {
                Sprite = sprite;
                LastWriteUtc = lastWriteUtc;
                LastAccessSeq = lastAccessSeq;
            }

            public Sprite Sprite { get; }
            public DateTime LastWriteUtc { get; }
            public long LastAccessSeq { get; set; }
        }

        // TryGetAlbumForTrack(객체 동일성 기준)으로 그 트랙이 어느 앨범(hwa 하위 폴더)인지 찾아
        // 그 앨범의 자켓 이미지를 서빙한다 — BGM 서빙과 같은 방식.
        // 메인 스레드 전용
        private static readonly Dictionary<string, JacketCacheEntry> CustomJacketCache =
            new Dictionary<string, JacketCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> CustomJacketFailedCache =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static long _jacketAccessSeq;

        private static void EvictJacketCacheEntry(string fullPath)
        {
            if (CustomJacketCache.Remove(fullPath, out JacketCacheEntry? cached) && cached.Sprite != null)
            {
                Texture2D? tex = cached.Sprite.texture;
                UnityEngine.Object.Destroy(cached.Sprite);
                if (tex != null)
                {
                    UnityEngine.Object.Destroy(tex);
                }
            }
        }

        private static void EnforceJacketCacheLimit(string keepPath)
        {
            int maxEntries = CustomConfig.JacketCacheMaxEntries;
            if (maxEntries <= 0)
            {
                return;
            }

            while (CustomJacketCache.Count >= maxEntries)
            {
                string? oldestKey = null;
                long oldestSeq = long.MaxValue;
                foreach (var kvp in CustomJacketCache)
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

                EvictJacketCacheEntry(oldestKey);
            }
        }

        private static Sprite? LoadCustomJacketSprite(string filePath)
        {
            // CustomAlbum.TryLoad가 경로를 이미 절대 경로로 정규화해 둔다(호출마다 GetFullPath 생략).
            string fullPath = ToServePath(filePath);

            // 곡 선택 커서 이동마다 불리는 경로라 수정 시각은 경로별로 잠깐 캐시한 값을 쓴다(FileStampSupport).
            DateTime lastWriteUtc = GetLastWriteTimeUtcThrottled(fullPath);
            if (lastWriteUtc == MissingFileStamp)
            {
                EvictJacketCacheEntry(fullPath);
                CustomJacketFailedCache.Remove(fullPath);
                return null;
            }
            if (CustomJacketFailedCache.TryGetValue(fullPath, out DateTime failedWriteUtc))
            {
                if (failedWriteUtc == lastWriteUtc)
                {
                    return null;
                }

                CustomJacketFailedCache.Remove(fullPath);
            }

            if (CustomJacketCache.TryGetValue(fullPath, out JacketCacheEntry? cached))
            {
                if (cached.Sprite != null && cached.LastWriteUtc == lastWriteUtc)
                {
                    cached.LastAccessSeq = ++_jacketAccessSeq;
                    return cached.Sprite;
                }

                EvictJacketCacheEntry(fullPath);
            }

            try
            {
                // 실제로 읽을 때는 지금 값을 다시 본다(캐시에 저장되는 수정 시각과 읽은 내용이 어긋나지 않게).
                lastWriteUtc = GetLastWriteTimeUtcFresh(fullPath);
                byte[] bytes = File.ReadAllBytes(fullPath);
                var texture = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(texture, bytes))
                {
                    CustomJacketFailedCache[fullPath] = lastWriteUtc;
                    MelonLogger.Warning($"[CustomJacket] 이미지 디코딩 실패: {fullPath}");
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
                texture.hideFlags |= HideFlags.DontUnloadUnusedAsset;

                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                sprite.name = "CustomJacket_" + Path.GetFileNameWithoutExtension(fullPath);
                sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                EvictJacketCacheEntry(fullPath);
                EnforceJacketCacheLimit(fullPath);
                CustomJacketFailedCache.Remove(fullPath);
                CustomJacketCache[fullPath] = new JacketCacheEntry(sprite, lastWriteUtc, ++_jacketAccessSeq);
                MelonLogger.Msg($"[CustomJacket] Cached custom jacket: {fullPath} ({texture.width}x{texture.height})");
                return sprite;
            }
            catch (Exception ex)
            {
                CustomJacketFailedCache[fullPath] = lastWriteUtc;
                MelonLogger.Warning($"[CustomJacket] 로드 실패: {fullPath}: {ex.Message}");
                return null;
            }
        }

        [HarmonyPatch]
        private static class CustomJacketLoaderPatch
        {
            private static readonly List<MethodBase> Targets = new List<MethodBase>();

            private static bool Prepare() => PrepareTargets(Targets, new[]
            {
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadJacketSprite", 1, "Action")
                    .WithAlternates("LoadJacketImage", "LoadJacket", "LoadCoverSprite"),
            }, "커스텀 자켓");

            private static IEnumerable<MethodBase> TargetMethods() => Targets;

            private static bool Prefix(object __instance, object[] __args)
            {
                try
                {
                    if (__args.Length == 0 || __args[0] is null)
                    {
                        return true;
                    }

                    // 공식 트랙에 커스텀 커버가 들어가지 않도록, 우리가 주입한 객체인지로만 판별한다.
                    // (TrackID는 복제 원본인 공식 "Starting Point"와 동일해서 구분 기준이 될 수 없다.)
                    // 어느 앨범 폴더의 이미지를 쓸지도 이 조회 결과로 정해진다.
                    CustomAlbum? album = TryGetAlbumForTrack(__instance);
                    if (album?.JacketPath is null)
                    {
                        return true;
                    }

                    string path = album.JacketPath;
                    Sprite? sprite = LoadCustomJacketSprite(path);
                    if (sprite is null)
                    {
                        return true;
                    }

                    if (LogOnce($"CustomJacket.Serve.{path}"))
                    {
                        MelonLogger.Msg($"[CustomJacket] Serving custom jacket for '{album.DisplayName}': {path} (이후 같은 요청은 로그 생략)");
                    }

                    TryInvokeSingleArgCallback(__args[0], sprite, "CustomJacket");
                    return false;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[CustomJacket] Error in CustomJacketLoaderPatch.Prefix: {ex.Message}");
                }

                return true;
            }
        }
    }
}
