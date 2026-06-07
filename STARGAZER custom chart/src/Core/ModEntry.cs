using System;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod : MelonMod
    {
        private static readonly HarmonyLib.Harmony RuntimeHarmonyInstance = new HarmonyLib.Harmony("com.example.stargazer.customchart");
        private static bool _hooksPatchAttempted;
        private static bool _hooksApplied;

        public override void OnInitializeMelon()
        {
            try
            {
                LoggerInstance.Msg("[Mod] OnInitialize called.");

                // Create the 'hwa' directory automatically in the game directory
                string hwaPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "hwa");
                if (!System.IO.Directory.Exists(hwaPath))
                {
                    System.IO.Directory.CreateDirectory(hwaPath);
                    LoggerInstance.Msg($"[Mod] Created directory: {hwaPath}");
                }
                else
                {
                    LoggerInstance.Msg($"[Mod] Directory already exists: {hwaPath}");
                }

                LogBgmDebugFileInfo(hwaPath);
                StartCustomBgmPreload(hwaPath);
                TryApplyFocusedTrackViewerPatches();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[Mod] initialization failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
