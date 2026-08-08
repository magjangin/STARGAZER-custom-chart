using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // 난이도 표시는 메타데이터(levelString)가 아니라 UI에만 덮어쓴다.
        // levelString을 바꾸면 패턴 에셋 로딩이 멈추기 때문이다
        // (ExperimentChartSettings.EnableTrackLevelOverride 주석의 실측 근거 참고).
        // 게임 자체의 세터인 LevelItem.FetchLevelText(string)를 호출해 원본 서식을 그대로 유지한다.
        // ELevels 선언 순서. 이름으로 난이도를 못 알아낼 때의 폴백.
        private static readonly string[] LevelOrderFallback = { "Cosmic", "Stellar", "Void" };

        // 지금 난이도 선택 화면에 떠 있는 앨범(없으면 공식 곡). FocusedTrackViewer 판정 결과를 재사용한다.
        private static CustomAlbum? LevelSelectorAlbum;

        // 플레이 중인 앨범과 난이도("Cosmic"/"Stellar"/"Void"). PlayerBase.Play 훅에서 TravelArgs로 채운다.
        private static CustomAlbum? CurrentPlayAlbum;
        private static string? CustomChartPlayLevelKey;
        private static void HandleLevelSelectorSetTrack(object? instance, object[] args)
        {
            object? track = args.Length > 0 ? args[0] : null;
            LevelSelectorAlbum = TryGetAlbumForTrack(track);
            if (LevelSelectorAlbum is not null)
            {
                ApplyCustomLevelDisplay(instance);
            }
        }

        private static void HandleLevelSelectorRefresh(object? instance)
        {
            // 게임이 표시를 다시 채운 뒤 불리는 훅에서 매번 재적용한다.
            if (LevelSelectorAlbum is not null)
            {
                ApplyCustomLevelDisplay(instance);
            }
        }

        private static void ApplyCustomLevelDisplay(object? levelSelector)
        {
            try
            {
                if (levelSelector is null)
                {
                    return;
                }

                CustomTrackInfo? info = LevelSelectorAlbum?.Info;
                if (info is null || info.Levels.Count == 0)
                {
                    return;
                }

                object? levels = TryGetMemberValue(levelSelector, levelSelector.GetType(), "levels");
                if (levels is null)
                {
                    if (LogOnce("CustomLevelDisplay.noLevels"))
                    {
                        MelonLogger.Warning("[CustomLevel][LevelSelector] levels 목록을 찾지 못했습니다.");
                    }
                    return;
                }

                int applied = 0;
                int seen = 0;
                foreach (object? selectionLevel in EnumerateCollectionItems(levels, 12))
                {
                    if (selectionLevel is null)
                    {
                        continue;
                    }

                    object? levelItem = TryGetMemberValue(selectionLevel, selectionLevel.GetType(), "item");
                    if (levelItem is null)
                    {
                        seen++;
                        continue;
                    }

                    // SelectionItem<ELevels>.selectedValue는 제네릭이라 리플렉션으로 읽으면 enum이 아닌
                    // 포인터성 정수가 나온다(실측: 847634592). LevelItem의 오브젝트 이름이
                    // "Cosmic"/"Stellar"/"Void"로 정확히 들어있어 그걸 키로 쓰고, 실패 시 순서로 폴백한다.
                    string? levelKey = TryGetMemberValue(levelItem, levelItem.GetType(), "name")?.ToString()?.Trim();
                    if (levelKey is null || !info.Levels.ContainsKey(levelKey))
                    {
                        levelKey = seen < LevelOrderFallback.Length ? LevelOrderFallback[seen] : null;
                    }

                    seen++;

                    if (levelKey is null || !info.Levels.TryGetValue(levelKey, out string? levelValue))
                    {
                        if (LogOnce("CustomLevelDisplay.miss"))
                        {
                            MelonLogger.Warning($"[CustomLevel][LevelSelector] 항목 해석 실패: name={levelKey ?? "<null>"}");
                        }
                        continue;
                    }

                    if (TryInvokeFetchLevelText(levelItem, levelValue))
                    {
                        applied++;
                    }
                }

                if (LogOnce($"CustomLevelDisplay.{applied}/{seen}"))
                {
                    string summary = string.Join(", ", info.Levels.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                    MelonLogger.Msg($"[CustomLevel][LevelSelector] 난이도 표시 덮어쓰기: 적용={applied}/{seen} ({summary})");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CustomLevel] 난이도 표시 덮어쓰기 실패: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // 곡 선택 화면(SELECT MUSIC)의 COSMIC/STELLAR/VOID 배지는 LevelSelector가 아니라
        // FocusedTrackViewer가 그린다. LevelUnit.Set(string)이 게임 자체 세터라 그대로 호출한다.
        // 호출 지점은 FocusedTrackViewerProbe의 Postfix(이미 SetTrack/Refresh 전부 후킹 중이고 안정적).
        private static void ApplyCustomLevelDisplayToFocusedViewer(object viewer)
        {
            try
            {
                object? currentTrack = TryGetMemberValue(viewer, viewer.GetType(), "currentTrack");
                CustomAlbum? album = TryGetAlbumForTrack(currentTrack);

                // 난이도 선택 화면(LevelSelector)은 지금 포커스된 곡으로만 열리므로, 여기서 확정된 판정을
                // 그대로 재사용한다. LevelSelector.SetTrack은 실제로 호출되지 않아 신뢰할 수 없었다.
                LevelSelectorAlbum = album;

                CustomTrackInfo? info = album?.Info;
                if (info is null || info.Levels.Count == 0)
                {
                    return;
                }

                object? levelUnits = TryGetMemberValue(viewer, viewer.GetType(), "levelItems");
                if (levelUnits is null)
                {
                    return;
                }

                int applied = 0;
                foreach (object? levelUnit in EnumerateCollectionItems(levelUnits, 12))
                {
                    if (levelUnit is null)
                    {
                        continue;
                    }

                    string? levelKey = TryGetMemberValue(levelUnit, levelUnit.GetType(), "targetLevel")?.ToString();
                    if (levelKey is null || !info.Levels.TryGetValue(levelKey, out string? levelValue))
                    {
                        continue;
                    }

                    if (TryInvokeLevelUnitSet(levelUnit, levelValue))
                    {
                        applied++;
                    }
                }

                if (applied > 0 && LogOnce($"CustomLevelDisplay.Focused.{applied}"))
                {
                    string summary = string.Join(", ", info.Levels.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                    MelonLogger.Msg($"[CustomLevel] 곡 선택 화면 난이도 표시를 덮어썼습니다(표시 전용): {summary}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CustomLevel] 곡 선택 화면 난이도 덮어쓰기 실패: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool TryInvokeLevelUnitSet(object levelUnit, string levelValue)
        {
            try
            {
                MethodInfo? set = levelUnit.GetType().GetMethod(
                    "Set",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string) },
                    null);
                if (set is null)
                {
                    return false;
                }

                set.Invoke(levelUnit, new object[] { levelValue });
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CustomLevel] LevelUnit.Set 호출 실패: {ex.Message}");
                return false;
            }
        }

        // 플레이 화면(Play.Widgets.CurrentTrackViewer). 여기 levelText는 LevelItem이 아니라
        // TextProvider라서 DoTextSetter(string)로 직접 쓴다.
        private static void ApplyCustomLevelDisplayToPlayScene(object? viewer)
        {
            try
            {
                if (viewer is null || CurrentPlayAlbum is null || CustomChartPlayLevelKey is null)
                {
                    return;
                }

                CustomTrackInfo? info = CurrentPlayAlbum.Info;
                if (info is null || !info.Levels.TryGetValue(CustomChartPlayLevelKey, out string? levelValue))
                {
                    return;
                }

                object? levelText = TryGetMemberValue(viewer, viewer.GetType(), "levelText");
                if (levelText is null)
                {
                    return;
                }

                if (TrySetTextProviderText(levelText, levelValue)
                    && LogOnce($"CustomLevelDisplay.Play.{CustomChartPlayLevelKey}.{levelValue}"))
                {
                    MelonLogger.Msg($"[CustomLevel][Play] 플레이 화면 난이도 표시를 덮어썼습니다: {CustomChartPlayLevelKey}={levelValue}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CustomLevel][Play] 난이도 표시 덮어쓰기 실패: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool TrySetTextProviderText(object textProvider, string value)
        {
            Type type = textProvider.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // textChain까지 전파하는 게임 자체 세터를 우선 사용한다.
            try
            {
                MethodInfo? setter = type.GetMethod("DoTextSetter", flags, null, new[] { typeof(string) }, null);
                if (setter is not null)
                {
                    setter.Invoke(textProvider, new object[] { value });
                    return true;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CustomLevel][Play] DoTextSetter 실패: {ex.Message}");
            }

            try
            {
                PropertyInfo? textProp = type.GetProperty("Text", flags);
                if (textProp is not null && textProp.CanWrite)
                {
                    textProp.SetValue(textProvider, value);
                    return true;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CustomLevel][Play] Text 프로퍼티 쓰기 실패: {ex.Message}");
            }

            return false;
        }

        // 결과 씬(PlayInfoViewer.ShowPlayInfo). 여기 levelText는 리스트가 아니라 LevelItem 하나이고,
        // 어떤 난이도로 플레이했는지는 ITravelResultData.PlayData(TravelArgs)의 PlayLevel/PlayTrack에서 얻는다.
        private static void HandleResultPlayInfoLevelDisplay(object? instance, object[] args)
        {
            try
            {
                object? resultData = args.Length > 0 ? args[0] : null;
                if (instance is null || resultData is null)
                {
                    return;
                }

                object? playData = TryGetMemberValue(resultData, resultData.GetType(), "PlayData");
                if (playData is null)
                {
                    return;
                }

                object? playTrack = TryGetMemberValue(playData, playData.GetType(), "PlayTrack");
                CustomTrackInfo? info = TryGetAlbumForTrack(playTrack)?.Info;
                if (info is null || info.Levels.Count == 0)
                {
                    return;
                }

                string? levelKey = TryGetMemberValue(playData, playData.GetType(), "PlayLevel")?.ToString();
                if (levelKey is null || !info.Levels.TryGetValue(levelKey, out string? levelValue))
                {
                    return;
                }

                object? levelItem = TryGetMemberValue(instance, instance.GetType(), "levelText");
                if (levelItem is null)
                {
                    MelonLogger.Warning("[CustomLevel][Result] PlayInfoViewer.levelText를 찾지 못했습니다.");
                    return;
                }

                if (TryInvokeFetchLevelText(levelItem, levelValue))
                {
                    MelonLogger.Msg($"[CustomLevel][Result] 결과 씬 난이도 표시를 덮어썼습니다: {levelKey}={levelValue}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CustomLevel][Result] 난이도 표시 덮어쓰기 실패: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool TryInvokeFetchLevelText(object levelItem, string levelValue)
        {
            try
            {
                MethodInfo? fetch = levelItem.GetType().GetMethod(
                    "FetchLevelText",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string) },
                    null);
                if (fetch is null)
                {
                    return false;
                }

                fetch.Invoke(levelItem, new object[] { levelValue });
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CustomLevel] FetchLevelText 호출 실패: {ex.Message}");
                return false;
            }
        }
    }
}
