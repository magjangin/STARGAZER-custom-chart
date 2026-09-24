using System;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod : MelonMod
    {
        // [HarmonyPatch] 클래스들은 MelonLoader가 모드 어셈블리에 PatchAll을 돌려 자동으로 건다.
        // 이 인스턴스는 런타임에 동적으로 거는 패치(FocusedTrackViewer 메서드, TrackLoader 콜백)용이다.
        private static readonly HarmonyLib.Harmony RuntimeHarmonyInstance = new HarmonyLib.Harmony("com.magjangin.stargazer.customchart");

        public override void OnInitializeMelon()
        {
            try
            {
                LoggerInstance.Msg("[Mod] OnInitialize called.");

                // 게임 디렉터리에 'hwa' 폴더를 자동으로 생성합니다.
                string hwaPath = CustomAlbumRegistry.RootPath;
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
