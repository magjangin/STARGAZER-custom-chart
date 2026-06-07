using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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

        private static string TryScanForNumericDifficulty(object? obj)
        {
            if (obj is null) return "?";
            try
            {
                Type type = obj.GetType();
                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                
                // 1. Try to search for fields or properties with exact/partial names containing level/difficulty
                string[] targetCandidates = { "level", "difficulty", "lv", "val", "value" };
                
                // Try properties first
                foreach (var prop in type.GetProperties(Flags))
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                    string name = prop.Name.ToLowerInvariant();
                    bool matches = false;
                    foreach (string candidate in targetCandidates)
                    {
                        if (name.Contains(candidate, StringComparison.Ordinal))
                        {
                            matches = true;
                            break;
                        }
                    }
                    if (!matches) continue;
                    
                    try
                    {
                        object? val = prop.GetValue(obj);
                        if (val is not null && IsNumericAndPositive(val, out string strVal))
                        {
                            return strVal;
                        }
                    }
                    catch {}
                }
                
                // Try fields
                foreach (var field in type.GetFields(Flags))
                {
                    string name = field.Name.ToLowerInvariant();
                    bool matches = false;
                    foreach (string candidate in targetCandidates)
                    {
                        if (name.Contains(candidate, StringComparison.Ordinal))
                        {
                            matches = true;
                            break;
                        }
                    }
                    if (!matches) continue;
                    
                    try
                    {
                        object? val = field.GetValue(obj);
                        if (val is not null && IsNumericAndPositive(val, out string strVal))
                        {
                            return strVal;
                        }
                    }
                    catch {}
                }
                
                // 2. If it's a TextProvider or similar, try to extract its text value
                object? textVal = TryGetMemberValue(obj, type, "text")
                                  ?? TryGetMemberValue(obj, type, "Text")
                                  ?? TryGetMemberValue(obj, type, "string");
                if (textVal is not null)
                {
                    string textStr = textVal.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(textStr) && textStr != "?" && int.TryParse(textStr, out int num) && num > 0)
                    {
                        return textStr;
                    }
                }
            }
            catch {}
            
            return "?";
        }
        
        private static bool IsNumericAndPositive(object val, out string stringValue)
        {
            stringValue = "?";
            if (val is int iVal)
            {
                if (iVal > 0 && iVal < 100) { stringValue = iVal.ToString(); return true; }
            }
            else if (val is long lVal)
            {
                if (lVal > 0 && lVal < 100) { stringValue = lVal.ToString(); return true; }
            }
            else if (val is short sVal)
            {
                if (sVal > 0 && sVal < 100) { stringValue = sVal.ToString(); return true; }
            }
            else if (val is byte bVal)
            {
                if (bVal > 0 && bVal < 100) { stringValue = bVal.ToString(); return true; }
            }
            else if (val is float fVal)
            {
                if (fVal > 0f && fVal < 100f) { stringValue = ((int)fVal).ToString(); return true; }
            }
            else if (val is double dVal)
            {
                if (dVal > 0d && dVal < 100d) { stringValue = ((int)dVal).ToString(); return true; }
            }
            else if (val is string str)
            {
                if (int.TryParse(str, out int num) && num > 0 && num < 100)
                {
                    stringValue = str;
                    return true;
                }
            }
            return false;
        }

        private static void DumpObjectValues(string label, object? obj)
        {
            if (obj is null) return;
            try
            {
                Type type = obj.GetType();
                var list = new List<string>();
                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                
                foreach (var prop in type.GetProperties(Flags))
                {
                    if (prop.CanRead && prop.GetIndexParameters().Length == 0)
                    {
                        try
                        {
                            object? val = prop.GetValue(obj);
                            list.Add($"P:{prop.Name}={val ?? "null"} ({prop.PropertyType.Name})");
                        }
                        catch (Exception ex)
                        {
                            list.Add($"P:{prop.Name}=<error: {ex.Message}>");
                        }
                    }
                }
                
                foreach (var field in type.GetFields(Flags))
                {
                    try
                    {
                        object? val = field.GetValue(obj);
                        list.Add($"F:{field.Name}={val ?? "null"} ({field.FieldType.Name})");
                    }
                    catch (Exception ex)
                    {
                        list.Add($"F:{field.Name}=<error: {ex.Message}>");
                    }
                }
                
                MelonLogger.Msg($"[DumpValues][{label}] Type={type.FullName}:\n  {string.Join("\n  ", list)}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[DumpValues][{label}] Failed: {ex.Message}");
            }
        }
    }
}
