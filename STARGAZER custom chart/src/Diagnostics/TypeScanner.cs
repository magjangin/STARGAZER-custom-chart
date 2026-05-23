using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static void DumpAssemblyCSharpTypes()
        {
            try
            {
                Assembly? asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name?.IndexOf("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) >= 0);
                if (asm is null)
                {
                    MelonLogger.Msg("[TypeScan] Assembly-CSharp not loaded yet.");
                    return;
                }

                var types = asm.GetTypes()
                    .Where(t => t.IsClass)
                    .OrderBy(t => t.FullName)
                    .ToArray();

                MelonLogger.Msg($"[TypeScan] total types={types.Length}");
                int shown = 0;
                foreach (Type t in types)
                {
                    if (shown++ >= 200)
                    {
                        break;
                    }

                    MelonLogger.Msg($"[TypeScan] {t.FullName}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TypeScan] failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
