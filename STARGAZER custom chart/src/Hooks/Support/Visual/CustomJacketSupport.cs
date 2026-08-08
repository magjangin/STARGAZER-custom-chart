using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // INNER_TrackData.LoadJacketSprite(Action<Sprite>)는 LoadBGMClip/LoadPreviewClip과
        // 시그니처가 동일한 트랙별 콜백 로더다(decompiled/Assembly-CSharp/Il2CppStargazer/TrackLoader.cs).
        // 뼈대 단계라 트랙별 개별 이미지 매핑은 아직 없고, IsCustomChartTrack(객체 동일성 기준)으로 식별된
        // 커스텀 트랙 전체에 hwa/ 폴더의 이미지 파일 하나를 공용으로 서빙한다 — BGM 서빙과 동일한 수준.
        private static readonly Dictionary<string, Sprite> CustomJacketCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        // 정확한 이름을 먼저 시도하고(Windows는 대소문자 구분 안 하니 대소문자 편차는 자동 해결됨),
        // 없으면 hwa 폴더에서 이미지 확장자를 가진 첫 파일로 폴백한다 — 사용자가 파일명을
        // "Thumbnail.png", "썸네일.png" 등 자유롭게 지어도 찾도록.
        private static readonly string[] CustomJacketFileNameCandidates =
        {
            "jacket.png", "jacket.jpg", "jacket.jpeg",
            "cover.png", "cover.jpg", "cover.jpeg",
            "thumbnail.png", "thumbnail.jpg", "thumbnail.jpeg",
            "자켓.png", "커버.png", "썸네일.png",
        };

        private static readonly string[] CustomJacketImageExtensions = { ".png", ".jpg", ".jpeg" };

        private static string? FindCustomJacketFile(string hwaPath)
        {
            foreach (string name in CustomJacketFileNameCandidates)
            {
                string candidate = Path.Combine(hwaPath, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(hwaPath))
                {
                    string ext = Path.GetExtension(file);
                    if (Array.Exists(CustomJacketImageExtensions, candidateExt => string.Equals(candidateExt, ext, StringComparison.OrdinalIgnoreCase)))
                    {
                        MelonLogger.Msg($"[CustomJacket] 이름 후보와 안 맞아 폴더 내 이미지 파일로 대체: {file}");
                        return file;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CustomJacket] hwa 폴더 스캔 실패: {ex.Message}");
            }

            return null;
        }

        private static Sprite? LoadCustomJacketSprite(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (CustomJacketCache.TryGetValue(fullPath, out Sprite? cached) && cached != null)
            {
                return cached;
            }

            if (!File.Exists(fullPath))
            {
                return null;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                var texture = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(texture, bytes))
                {
                    MelonLogger.Warning($"[CustomJacket] 이미지 디코딩 실패: {fullPath}");
                    return null;
                }

                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                sprite.name = "CustomJacket_" + Path.GetFileNameWithoutExtension(fullPath);
                CustomJacketCache[fullPath] = sprite;
                MelonLogger.Msg($"[CustomJacket] Cached custom jacket: {fullPath} ({texture.width}x{texture.height})");
                return sprite;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CustomJacket] 로드 실패: {fullPath}: {ex.Message}");
                return null;
            }
        }

        private static void InvokeActionOfSprite(object? callback, Sprite? sprite)
        {
            if (callback is null) return;
            try
            {
                MethodInfo? invoke = callback.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
                if (invoke is not null)
                {
                    invoke.Invoke(callback, new object?[] { sprite });
                }
                else
                {
                    MelonLogger.Warning("[CustomJacket] 콜백 객체에서 Invoke 메서드를 찾지 못했습니다.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CustomJacket] Failed to invoke callback: {ex.Message}");
            }
        }

        [HarmonyPatch]
        private static class CustomJacketLoaderPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                Type? trackDataType = FindType("Il2CppStargazer.TrackLoader+INNER_TrackData");
                if (trackDataType is null)
                {
                    MelonLogger.Warning("[CustomJacket] Il2CppStargazer.TrackLoader+INNER_TrackData 타입을 찾지 못했습니다.");
                    yield break;
                }

                MethodInfo? loadJacket = trackDataType.GetMethod("LoadJacketSprite", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (loadJacket is not null)
                {
                    MelonLogger.Msg($"[CustomJacket] Found target LoadJacketSprite: {loadJacket.DeclaringType?.FullName}.{loadJacket.Name}");
                    yield return loadJacket;
                }
            }

            private static bool Prefix(object __instance, object[] __args)
            {
                try
                {
                    if (__args.Length == 0 || __args[0] is null)
                    {
                        return true;
                    }

                    Type t = __instance.GetType();
                    string displayName = TryGetMemberValue(__instance, t, "TrackDisplayName")?.ToString()
                        ?? TryGetMemberValue(__instance, t, "TrackDisplayNameEN")?.ToString()
                        ?? string.Empty;

                    // 공식 트랙에 커스텀 커버가 들어가지 않도록, 우리가 주입한 객체인지로만 판별한다.
                    // (TrackID는 복제 원본인 공식 "Starting Point"와 동일해서 구분 기준이 될 수 없다.)
                    if (!IsCustomChartTrack(__instance))
                    {
                        return true;
                    }

                    // TODO: 트랙마다 다른 이미지가 필요해지면 TrackID로 파일명을 분기할 것.
                    string hwaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hwa");
                    string? path = FindCustomJacketFile(hwaPath);
                    if (path is null)
                    {
                        return true;
                    }

                    Sprite? sprite = LoadCustomJacketSprite(path);
                    if (sprite is null)
                    {
                        return true;
                    }

                    MelonLogger.Msg($"[CustomJacket] Serving custom jacket for '{displayName}': {path}");
                    InvokeActionOfSprite(__args[0], sprite);
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
