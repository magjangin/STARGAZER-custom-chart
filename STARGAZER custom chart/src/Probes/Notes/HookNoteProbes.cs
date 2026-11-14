using System;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // 마지막으로 처리한 Pattern의 IL2CPP 포인터. PlayerBase.Play마다 비운다(UpdateCustomChartPlayState).
        private static IntPtr LastProbedPatternPtr;

        private static void ProbeNoteArrayMembers(object? instance, string source)
        {
            try
            {
                if (instance is null || !IsCustomChartPlayActive || CurrentPlayAlbum is null)
                {
                    return;
                }

                // 1차(람다 Postfix)와 2차(onCompleted 래퍼)가 둘 다 살아 있으면 같은 Pattern이 두 번 들어온다.
                // 관리 래퍼 객체는 호출마다 새로 만들어질 수 있어 ReferenceEquals가 아니라 IL2CPP 네이티브 포인터로 비교한다.
                if (source.StartsWith("PatternLoader.", StringComparison.Ordinal))
                {
                    IntPtr patternPtr = TryGetIl2CppPointer(instance);
                    if (patternPtr != IntPtr.Zero)
                    {
                        if (patternPtr == LastProbedPatternPtr)
                        {
                            MelonLogger.Msg($"[NoteProbe] 이미 처리한 패턴 인스턴스({source})이므로 중복 BMS 주입을 건너뜁니다.");
                            return;
                        }

                        LastProbedPatternPtr = patternPtr;
                    }
                }

                object? layersValue = TryGetMemberByType(instance, "List<Layer>", "Layers", "layers");
                if (layersValue is null)
                {
                    MelonLogger.Warning($"[NoteProbe] {source} Pattern.Layers를 찾지 못했습니다.");
                    return;
                }

                object? firstLayer = null;
                object? firstAreasValue = null;
                object? templateArea = null;
                object? templateNote = null;

                foreach (object? layer in EnumerateCollectionItems(layersValue, 12))
                {
                    if (layer is null)
                    {
                        continue;
                    }

                    object? areasValue = TryGetMemberByType(layer, "List<Area>", "Areas", "_Areas_k__BackingField", "areas");
                    if (areasValue is null)
                    {
                        continue;
                    }

                    foreach (object? area in EnumerateCollectionItems(areasValue, 128))
                    {
                        if (area is null)
                        {
                            continue;
                        }

                        object? notesValue = TryGetMemberByType(area, "List<Note>", "notes", "Notes");
                        if (notesValue is null)
                        {
                            continue;
                        }

                        foreach (object? note in EnumerateCollectionItems(notesValue, 8))
                        {
                            if (note is not null)
                            {
                                firstLayer = layer;
                                firstAreasValue = areasValue;
                                templateArea = area;
                                templateNote = note;
                                break;
                            }
                        }

                        if (templateNote is not null)
                        {
                            break;
                        }
                    }

                    if (templateNote is not null)
                    {
                        break;
                    }
                }

                if (firstLayer is null || firstAreasValue is null || templateArea is null || templateNote is null)
                {
                    MelonLogger.Warning($"[NoteProbe] {source} 템플릿 Layer/Area/Note를 찾지 못해 BMS 주입을 건너뜁니다.");
                    return;
                }

                TryInjectBmsChart(firstLayer, firstAreasValue, templateArea, templateNote);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[NoteProbe] {source} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
