using System;
using System.IO;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class StargazerCustomChartMod : MelonMod
    {
        private static readonly HarmonyLib.Harmony RuntimeHarmonyInstance = new HarmonyLib.Harmony("com.hwayoungwang.stargazer.customchart");

        public override void OnInitializeMelon()
        {
            try
            {
                LoggerInstance.Msg("[Mod] OnInitialize called.");

                SafeTextReader.EnsureInitialized();
                CustomConfig.LogInfo = MelonLogger.Msg;
                CustomConfig.LogWarning = MelonLogger.Warning;
                EnsureAlbumRootExists();

                // 여기서 한 번 읽어 두면 savecustomkey 폴더와 기본 config.txt가 없을 때 만들어진다.
                LoggerInstance.Msg($"[Mod] autoplay={CustomConfig.AutoPlay} (설정: {CustomConfig.FilePath})");
                LoggerInstance.Msg($"[Mod] NoteSway={CustomConfig.NoteSway} NoteSpeedChaos={CustomConfig.NoteSpeedChaos}");

                HookHealth.CheckGameFingerprint();
                LogBgmDebugFileInfo();
                StartCustomBgmPreload();
                ApplyDeferredPatches();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[Mod] initialization failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public override void OnLateInitializeMelon()
        {
            try
            {
                ApplyDeferredPatches();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[Mod] OnLateInitializeMelon failed: {ex.Message}");
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            try
            {
                if (string.IsNullOrEmpty(sceneName) || sceneName.IndexOf("Play", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    ResetPlaySessionState();
                    if (!string.IsNullOrEmpty(sceneName)
                        && (sceneName.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0
                            || sceneName.IndexOf("Main", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        LevelSelectorAlbum = null;
                    }
                }

                ApplyDeferredPatches();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[Mod] OnSceneWasLoaded failed: {ex.Message}");
            }
        }

        // 수동 Patch 계열은 대상 타입이 늦게 로드될 수 있어 초기화/후기 초기화/씬 로드마다 다시 시도한다.
        // 셋 다 이미 다 걸렸거나 설정으로 꺼져 있으면 즉시 반환하므로 반복 호출 비용은 없다.
        private void ApplyDeferredPatches()
        {
            TryApplyFocusedTrackViewerPatches();
            JudgeAllPerfectPatch.Initialize(RuntimeHarmonyInstance);
            TrackUnlockAllPatch.Initialize(RuntimeHarmonyInstance);

            // 상태가 바뀐 경우에만 [Health] 요약이 다시 찍힌다(HookHealth.LogSummary 참고).
            HookHealth.LogSummary();
        }

        // 게임 디렉터리에 'hwa' 폴더(커스텀 곡 루트)를 자동으로 만든다.
        private void EnsureAlbumRootExists()
        {
            string hwaPath = CustomAlbumRegistry.RootPath;
            if (Directory.Exists(hwaPath))
            {
                LoggerInstance.Msg($"[Mod] Directory already exists: {hwaPath}");
                return;
            }

            Directory.CreateDirectory(hwaPath);
            LoggerInstance.Msg($"[Mod] Created directory: {hwaPath}");
        }
    }
}
