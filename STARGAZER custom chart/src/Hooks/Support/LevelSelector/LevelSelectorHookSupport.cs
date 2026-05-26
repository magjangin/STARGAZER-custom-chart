using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static void EnumerateLevelSelectorLevels(object instance)
        {
            try
            {
                object? levels = TryGetMemberValue(instance, instance.GetType(), "levels");
                if (levels is null)
                {
                    MelonLogger.Warning("[LevelSelector][Enumerate] 'levels' collection is null.");
                    return;
                }

                var items = EnumerateCollectionItems(levels, 12).ToList();
                if (items.Count == 0)
                {
                    return;
                }

                var levelSummary = new List<string>();
                foreach (object? item in items)
                {
                    if (item is null)
                    {
                        levelSummary.Add("null");
                        continue;
                    }

                    string levelName = "?";
                    string levelText = "?";
                    try
                    {
                        object? levelItem = TryGetMemberValue(item, item.GetType(), "item");
                        if (levelItem is not null)
                        {
                            levelName = TryGetMemberValue(levelItem, levelItem.GetType(), "name")?.ToString() ?? "?";
                            object? tp = TryGetMemberValue(levelItem, levelItem.GetType(), "levelText");
                            if (tp is not null && TryGetExactPropertyValue(tp, "Text", out object? textValue) && textValue is not null)
                            {
                                levelText = textValue.ToString() ?? "?";
                            }
                        }
                    }
                    catch
                    {
                    }

                    levelSummary.Add($"{levelName}={levelText}");
                }

                MelonLogger.Msg($"[LevelSelector] levels: [{string.Join(", ", levelSummary)}]");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[LevelSelector][Enumerate] Failed: {ex.Message}");
            }
        }
    }
}