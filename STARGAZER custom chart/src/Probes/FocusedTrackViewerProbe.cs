using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static bool _focusedTrackViewerPatched;

        [ThreadStatic]
        private static bool _isProbingFocusedTrackViewer;

        private void TryApplyFocusedTrackViewerPatches()
        {
            if (_focusedTrackViewerPatched) return;
            _focusedTrackViewerPatched = true;

            try
            {
                Type? type = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Select(assembly => assembly.GetType("Il2CppStargazer.Travel.FocusedTrackViewer", false))
                    .FirstOrDefault(t => t is not null);

                if (type is null)
                {
                    LoggerInstance.Warning("[FocusedTrackViewerProbe] Il2CppStargazer.Travel.FocusedTrackViewer type not found in loaded assemblies.");
                    return;
                }

                LoggerInstance.Msg($"[FocusedTrackViewerProbe] Found type: {type.FullName}. Proceeding with dynamic hooks.");

                // Hook methods of this type, excluding property getters/setters to avoid high-frequency noise and recursion
                MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(m => !m.IsGenericMethod && !m.IsAbstract && m.DeclaringType == type)
                    .Where(m => !m.Name.StartsWith("get_", StringComparison.Ordinal) && !m.Name.StartsWith("set_", StringComparison.Ordinal))
                    .ToArray();

                int successCount = 0;
                foreach (MethodInfo method in methods)
                {
                    try
                    {
                        RuntimeHarmonyInstance.Patch(
                            method,
                            prefix: new HarmonyMethod(typeof(GameTypeEnumeratorMod).GetMethod(nameof(FocusedTrackViewerPrefix), BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(GameTypeEnumeratorMod).GetMethod(nameof(FocusedTrackViewerPostfix), BindingFlags.Static | BindingFlags.NonPublic))
                        );
                        successCount++;
                    }
                    catch
                    {
                        // Some methods might fail to patch, which is fine
                    }
                }

                LoggerInstance.Msg($"[FocusedTrackViewerProbe] Successfully patched {successCount}/{methods.Length} methods of FocusedTrackViewer (excluding getters/setters).");
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[FocusedTrackViewerProbe] Failed to apply patches: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void FocusedTrackViewerPrefix(MethodBase __originalMethod, object? __instance, object[]? __args)
        {
            if (_isProbingFocusedTrackViewer) return;
            _isProbingFocusedTrackViewer = true;

            try
            {
                string methodSig = $"{__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}";
                MelonLogger.Msg($"[FocusedTrackViewer][PRE] {methodSig} called.");
                
                if (__instance is not null)
                {
                    EnumerateFocusedTrackViewerLevelItems(__instance, "PRE");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[FocusedTrackViewer][PRE] Exception: {ex.Message}");
            }
            finally
            {
                _isProbingFocusedTrackViewer = false;
            }
        }

        private static void FocusedTrackViewerPostfix(MethodBase __originalMethod, object? __instance, object[]? __args)
        {
            if (_isProbingFocusedTrackViewer) return;
            _isProbingFocusedTrackViewer = true;

            try
            {
                string methodSig = $"{__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}";
                MelonLogger.Msg($"[FocusedTrackViewer][POST] {methodSig} finished.");

                if (__instance is not null)
                {
                    EnumerateFocusedTrackViewerLevelItems(__instance, "POST");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[FocusedTrackViewer][POST] Exception: {ex.Message}");
            }
            finally
            {
                _isProbingFocusedTrackViewer = false;
            }
        }

        private static void EnumerateFocusedTrackViewerLevelItems(object instance, string phase)
        {
            try
            {
                Type type = instance.GetType();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // Print member catalog for debugging
                string membersCatalog = BuildObjectMemberCatalog("FocusedTrackViewer", instance);
                MelonLogger.Msg($"[FocusedTrackViewer][{phase}] Member Catalog: {membersCatalog}");

                // Enumerate properties and fields looking for "level", "item", "track", "data", "button", "list", "array"
                string[] candidates = { "level", "item", "track", "data", "button", "list", "array" };

                // Properties
                foreach (PropertyInfo property in type.GetProperties(flags))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
                    if (!NameMatchesAny(property.Name, candidates)) continue;

                    object? val = null;
                    try { val = property.GetValue(instance); } catch { continue; }
                    if (val is null) continue;

                    EnumerateFocusedTrackViewerCollection(property.Name, val, phase);
                }

                // Fields
                foreach (FieldInfo field in type.GetFields(flags))
                {
                    if (!NameMatchesAny(field.Name, candidates)) continue;

                    object? val = null;
                    try { val = field.GetValue(instance); } catch { continue; }
                    if (val is null) continue;

                    EnumerateFocusedTrackViewerCollection(field.Name, val, phase);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[FocusedTrackViewer][{phase}] Enumerate failed: {ex.Message}");
            }
        }

        private static void EnumerateFocusedTrackViewerCollection(string memberName, object collection, string phase)
        {
            try
            {
                var items = EnumerateCollectionItems(collection, 100).ToList();
                if (items.Count == 0) return;

                MelonLogger.Msg($"[FocusedTrackViewer][{phase}] Found collection '{memberName}' with {items.Count} items. Enumerating elements:");
                for (int i = 0; i < items.Count; i++)
                {
                    object? item = items[i];
                    if (item is null)
                    {
                        MelonLogger.Msg($"  [{i}] null");
                        continue;
                    }

                    string itemType = item.GetType().FullName ?? item.GetType().Name;
                    
                    // Expose members catalog for the first item in the collection
                    if (i == 0)
                    {
                        string itemCatalog = BuildObjectMemberCatalog($"Item[{memberName}]", item);
                        MelonLogger.Msg($"[FocusedTrackViewer][{phase}] {itemCatalog}");
                    }
                    
                    // Extract key values using candidates
                    string details = "";
                    try
                    {
                        string[] detailNames = { "name", "id", "level", "title", "text", "value", "lv", "difficulty", "num", "score", "rate", "number", "index" };
                        List<string> foundDetails = new List<string>();
                        foreach (string detailName in detailNames)
                        {
                            if (TryGetValueByNameCandidates(item, new[] { detailName }, out object? detailVal) && detailVal is not null)
                            {
                                foundDetails.Add($"{detailName}={detailVal}");
                            }
                        }
                        if (foundDetails.Count > 0)
                        {
                            details = $" ({string.Join(", ", foundDetails)})";
                        }
                    }
                    catch { }

                    // Deeper inspection of the nested 'levelItem' object inside LevelUnit
                    try
                    {
                        object? subItem = TryGetMemberValue(item, item.GetType(), "levelItem")
                            ?? TryGetMemberValue(item, item.GetType(), "LevelItem")
                            ?? TryGetMemberValue(item, item.GetType(), "_levelItem_k__BackingField");

                        if (subItem is not null)
                        {
                            if (i == 0)
                            {
                                string subCatalog = BuildObjectMemberCatalog($"levelItem[Item[{memberName}]]", subItem);
                                MelonLogger.Msg($"[FocusedTrackViewer][{phase}] Sub-item Catalog: {subCatalog}");
                            }

                            // Retrieve primitive / string / enum details of the nested levelItem
                            List<string> subDetails = new List<string>();
                            string[] subDetailNames = { "level", "lv", "number", "index", "value", "title", "text", "name", "id", "difficulty", "score", "rate" };
                            foreach (string subName in subDetailNames)
                            {
                                if (TryGetValueByNameCandidates(subItem, new[] { subName }, out object? subVal) && subVal is not null)
                                {
                                    subDetails.Add($"{subName}={subVal}");
                                }
                            }
                            if (subDetails.Count > 0)
                            {
                                details += $" | levelItem: [{string.Join(", ", subDetails)}]";
                            }

                            // Even deeper inspection of the TextProvider inside LevelItem
                            try
                            {
                                object? textProvider = TryGetMemberValue(subItem, subItem.GetType(), "levelText")
                                    ?? TryGetMemberValue(subItem, subItem.GetType(), "LevelText")
                                    ?? TryGetMemberValue(subItem, subItem.GetType(), "_levelText_k__BackingField");

                                if (textProvider is not null)
                                {
                                    if (i == 0)
                                    {
                                        string textProviderCatalog = BuildObjectMemberCatalog($"levelText[levelItem[Item[{memberName}]]]", textProvider);
                                        MelonLogger.Msg($"[FocusedTrackViewer][{phase}] TextProvider Catalog: {textProviderCatalog}");
                                    }

                                    // Retrieve string/value/text from TextProvider
                                    List<string> textDetails = new List<string>();
                                    
                                    // Query "Text" property EXACTLY first to get the difficulty number!
                                    if (TryGetExactPropertyValue(textProvider, "Text", out object? exactTextVal) && exactTextVal is not null)
                                    {
                                        textDetails.Add($"Text=\"{exactTextVal}\"");
                                    }

                                    string[] textFields = { "value", "Value", "str", "Str", "stringValue", "StringValue", "raw", "Raw", "displayText", "DisplayText" };
                                    foreach (string textField in textFields)
                                    {
                                        if (TryGetValueByNameCandidates(textProvider, new[] { textField }, out object? textVal) && textVal is not null)
                                        {
                                            textDetails.Add($"{textField}={textVal}");
                                        }
                                    }
                                    if (textDetails.Count > 0)
                                    {
                                        details += $" | levelText: [{string.Join(", ", textDetails)}]";
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }

                    MelonLogger.Msg($"  [{i}] Type={itemType}{details}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[FocusedTrackViewer][{phase}] Failed to enumerate collection '{memberName}': {ex.Message}");
            }
        }

        private static bool TryGetExactPropertyValue(object owner, string name, out object? value)
        {
            value = null;
            if (owner is null) return false;

            Type type = owner.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            PropertyInfo? prop = type.GetProperty(name, flags)
                ?? type.GetProperties(flags).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (prop is not null && prop.CanRead && prop.GetIndexParameters().Length == 0)
            {
                try
                {
                    value = prop.GetValue(owner);
                    return value is not null;
                }
                catch {}
            }

            FieldInfo? field = type.GetField(name, flags)
                ?? type.GetFields(flags).FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (field is not null)
            {
                try
                {
                    value = field.GetValue(owner);
                    return value is not null;
                }
                catch {}
            }

            return false;
        }
    }
}
