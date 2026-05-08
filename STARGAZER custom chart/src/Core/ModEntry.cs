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

                // 게임 디렉터리에 'hwa' 폴더를 자동으로 생성합니다.
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

                // 여기서 한 번 읽어 두면 savecustomkey 폴더와 기본 config.txt가 없을 때 만들어진다.
                LoggerInstance.Msg($"[Mod] autoplay={CustomConfig.AutoPlay} (설정: {CustomConfig.FilePath})");
                LoggerInstance.Msg($"[Mod] NoteSway={CustomConfig.NoteSway} NoteSpeedChaos={CustomConfig.NoteSpeedChaos}");

                LogBgmDebugFileInfo();
                StartCustomBgmPreload();
                TryApplyFocusedTrackViewerPatches();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[Mod] initialization failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
