using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    // 훅 적용 상태를 한곳에 모아 로그 한 덩어리로 보여 주고, 게임 바이너리 변경(=업데이트)을 감지한다.
    //
    // 왜 필요한가: 훅은 PrepareTargets / 수동 Patch / 지연 재시도로 여러 곳에서 따로 걸리고, 실패 로그도 각자 흩어져 있다.
    // 게임 업데이트 직후 "무엇이 살아 있고 무엇이 죽었는지"를 로그 여러 군데를 뒤져야 알 수 있었다.
    // 이제 각 기능이 Report로 (적용 수 / 기대 수)를 남기고, LogSummary가 [Health] 한 블록으로 요약한다.
    //
    // 변경 감지: GameAssembly.dll(Il2Cpp 네이티브 본체)의 크기+수정시각을 savecustomkey/.game-fingerprint 에 기록해 두고,
    // 다음 실행에서 다르면 "게임이 업데이트된 것 같다"는 경고와 함께 확인 순서를 안내한다. 판단용 힌트일 뿐 동작은 바꾸지 않는다.
    internal static class HookHealth
    {
        private sealed class Entry
        {
            public int Applied;
            public int Expected;
            public string? Note;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static string _lastSummary = string.Empty;
        private static bool _startupComplete;

        // 기능 이름은 Report 키와 같아야 한다(PrepareTargets의 feature 인자 등).
        public const string SaveGuardFeature = "기록 보호(SaveGuard)";

        // "실패하면 데이터가 망가지는" 기능. 일반 경고와 섞여 묻히지 않도록 Error 레벨로 따로 알린다.
        // 시작이 끝났는데도(OnLateInitializeMelon 이후) 한 번도 보고되지 않으면 패치 클래스 자체가 돌지 않은 것으로 본다
        // (예: 다른 [HarmonyPatch] 클래스 예외로 PatchAll이 중단된 경우 — Dead End 12).
        private static readonly Dictionary<string, string> CriticalFeatures = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SaveGuardFeature] = "커스텀 곡 결과가 공식 \"Starting Point\" 기록을 덮어쓸 수 있습니다. 고치기 전까지 커스텀 곡을 플레이하지 마세요.",
        };

        // OnLateInitializeMelon에서 부른다. 이후 LogSummary부터 미보고 핵심 기능을 실패로 센다.
        public static void MarkStartupComplete()
        {
            lock (Gate)
            {
                _startupComplete = true;
            }
        }

        // 같은 기능이 지연 재시도로 여러 번 불려도 마지막 값이 이긴다(적용 수는 늘어나기만 한다).
        public static void Report(string feature, int applied, int expected, string? note = null)
        {
            lock (Gate)
            {
                if (!Entries.TryGetValue(feature, out Entry? entry))
                {
                    entry = new Entry();
                    Entries[feature] = entry;
                }

                entry.Applied = Math.Max(entry.Applied, applied);
                entry.Expected = expected;
                entry.Note = note;
            }
        }

        // 설정으로 일부러 끈 기능(AllPerfect=false 등)은 실패가 아니라 "꺼짐"으로 기록한다.
        public static void ReportDisabled(string feature, string reason) => Report(feature, 0, 0, reason);

        // 요약이 지난번과 같으면 다시 찍지 않는다(씬 로드마다 호출돼도 로그가 늘지 않는다).
        public static void LogSummary()
        {
            string summary;
            bool anyProblem;
            var criticalProblems = new List<string>();
            lock (Gate)
            {
                if (_startupComplete)
                {
                    foreach (string feature in CriticalFeatures.Keys)
                    {
                        if (!Entries.ContainsKey(feature))
                        {
                            Entries[feature] = new Entry { Applied = 0, Expected = 1, Note = "패치 클래스가 실행되지 않음(PatchAll 중단 의심)" };
                        }
                    }
                }

                if (Entries.Count == 0)
                {
                    return;
                }

                var builder = new StringBuilder();
                anyProblem = false;
                foreach (KeyValuePair<string, Entry> pair in Entries.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    Entry entry = pair.Value;
                    string state;
                    if (entry.Expected == 0)
                    {
                        state = "꺼짐";
                    }
                    else if (entry.Applied >= entry.Expected)
                    {
                        state = "정상";
                    }
                    else if (entry.Applied == 0)
                    {
                        state = "실패";
                        anyProblem = true;
                    }
                    else
                    {
                        state = "일부";
                        anyProblem = true;
                    }

                    if (entry.Expected > 0 && entry.Applied < entry.Expected
                        && CriticalFeatures.TryGetValue(pair.Key, out string? consequence))
                    {
                        criticalProblems.Add($"{pair.Key} {state} ({entry.Applied}/{entry.Expected}) — {consequence}");
                    }

                    builder.Append("  - ").Append(pair.Key).Append(": ").Append(state);
                    if (entry.Expected > 0)
                    {
                        builder.Append(" (").Append(entry.Applied).Append('/').Append(entry.Expected).Append(')');
                    }

                    if (!string.IsNullOrEmpty(entry.Note))
                    {
                        builder.Append(" — ").Append(entry.Note);
                    }

                    builder.Append('\n');
                }

                summary = builder.ToString().TrimEnd('\n');
            }

            if (string.Equals(summary, _lastSummary, StringComparison.Ordinal))
            {
                return;
            }

            _lastSummary = summary;
            if (anyProblem)
            {
                MelonLogger.Warning("[Health] 일부 기능이 완전히 적용되지 않았습니다. 게임 업데이트 직후라면 위쪽 [HookPatch][attr] / [HookPatch][resolve] 로그부터 확인하세요.\n" + summary);
            }
            else
            {
                MelonLogger.Msg("[Health] 훅 상태 요약\n" + summary);
            }
        }

        // 게임 바이너리 지문(GameAssembly.dll + global-metadata.dat)을 이전 실행과 비교하고, Steam ACF의 buildid가
        // 고정 빌드(GameVersionLogic.PinnedSteamBuildId)와 같은지, 자동 업데이트가 막혀 있는지 확인한다.
        // 파일이 없거나 읽을 수 없으면 조용히 넘어간다(보조 기능 — 동작은 바꾸지 않는다).
        public static void CheckGameFingerprint()
        {
            string gameDir = ResolveGameDirectory();
            CheckBinaryFingerprint(gameDir);
            CheckSteamBuild(gameDir);
        }

        // MelonLoader 0.7(net6 호스트)에서는 AppDomain.BaseDirectory 가 빈 문자열이라(2026-11-19 실측)
        // 상대 경로는 작업 디렉터리 덕에 우연히 맞지만 Directory.GetParent("") 등은 예외를 던진다.
        // MelonEnvironment.GameRootDirectory → BaseDirectory → 현재 디렉터리 순으로 절대 경로를 만든다.
        private static string ResolveGameDirectory()
        {
            string? dir = null;
            try
            {
                dir = MelonLoader.Utils.MelonEnvironment.GameRootDirectory;
            }
            catch
            {
                // 구버전 MelonLoader 대비.
            }

            if (string.IsNullOrWhiteSpace(dir))
            {
                dir = AppDomain.CurrentDomain.BaseDirectory;
            }

            if (string.IsNullOrWhiteSpace(dir))
            {
                dir = Environment.CurrentDirectory;
            }

            return Path.GetFullPath(dir);
        }

        private static void CheckBinaryFingerprint(string gameDir)
        {
            try
            {
                string gameAssembly = Path.Combine(gameDir, "GameAssembly.dll");
                if (!File.Exists(gameAssembly))
                {
                    return;
                }

                var info = new FileInfo(gameAssembly);
                FileInfo? metadata = TryFindGlobalMetadata(gameDir);
                string current = GameVersionLogic.BuildFingerprint(
                    info.Length,
                    info.LastWriteTimeUtc.Ticks,
                    metadata?.Length,
                    metadata?.LastWriteTimeUtc.Ticks);
                string stampPath = Path.Combine(CustomConfig.FolderPath, ".game-fingerprint");

                string? previous = File.Exists(stampPath) ? File.ReadAllText(stampPath).Trim() : null;
                switch (GameVersionLogic.Compare(previous, current))
                {
                    case FingerprintComparison.FirstRun:
                        MelonLogger.Msg($"[Health] 게임 바이너리 지문을 처음 기록합니다 (GameAssembly.dll {info.Length:N0} bytes"
                            + (metadata is null ? ", global-metadata.dat 미발견" : $", global-metadata.dat {metadata.Length:N0} bytes") + ").");
                        break;
                    case FingerprintComparison.Changed:
                        var changed = new List<string>(2);
                        if (GameVersionLogic.HasPartChanged(previous, current, "ga"))
                        {
                            changed.Add("GameAssembly.dll");
                        }

                        if (GameVersionLogic.HasPartChanged(previous, current, "meta"))
                        {
                            changed.Add("global-metadata.dat");
                        }

                        string which = changed.Count > 0 ? string.Join(" + ", changed) : "게임 바이너리";
                        MelonLogger.Warning($"[Health] {which} 이(가) 지난 실행과 다릅니다 — 게임이 업데이트된 것으로 보입니다. "
                            + "확인 순서: [HookPatch][attr] target not found → [HookPatch][resolve] → [DualWrapper] → [FocusedTrackViewerProbe] → [Health] 요약. "
                            + "그리고 커스텀 곡 실제 플레이(BMS 주입·Save Guard)를 한 번 확인하세요.");
                        break;
                }

                if (!string.Equals(previous, current, StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(CustomConfig.FolderPath);
                    File.WriteAllText(stampPath, current);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Health] 게임 바이너리 지문 확인 실패(무시): {ex.GetType().Name}: {ex.Message}");
            }
        }

        // <게임>_Data/il2cpp_data/Metadata/global-metadata.dat. _Data 폴더 이름은 실행 파일 이름을 따르므로 패턴으로 찾는다.
        private static FileInfo? TryFindGlobalMetadata(string gameDir)
        {
            try
            {
                foreach (string dataDir in Directory.GetDirectories(gameDir, "*_Data"))
                {
                    string path = Path.Combine(dataDir, "il2cpp_data", "Metadata", "global-metadata.dat");
                    if (File.Exists(path))
                    {
                        return new FileInfo(path);
                    }
                }
            }
            catch
            {
                // 보조 정보라 무시한다.
            }

            return null;
        }

        // steamapps/common/<게임>/ → steamapps/appmanifest_<AppID>.acf
        private static void CheckSteamBuild(string gameDir)
        {
            try
            {
                string trimmed = gameDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                DirectoryInfo? steamApps = Directory.GetParent(trimmed)?.Parent;
                if (steamApps is null)
                {
                    return;
                }

                string acfPath = Path.Combine(steamApps.FullName, $"appmanifest_{GameVersionLogic.SteamAppId}.acf");
                if (!File.Exists(acfPath))
                {
                    // Steam 밖으로 옮긴 보존 사본(Goldberg 단독 실행 등)일 수 있다 — 경고하지 않는다.
                    return;
                }

                string acf = File.ReadAllText(acfPath);
                string? buildId = GameVersionLogic.TryReadAcfValue(acf, "buildid");
                string? targetBuildId = GameVersionLogic.TryReadAcfValue(acf, "TargetBuildID");
                string? autoUpdate = GameVersionLogic.TryReadAcfValue(acf, "AutoUpdateBehavior");
                bool readOnly = (File.GetAttributes(acfPath) & FileAttributes.ReadOnly) != 0;

                switch (GameVersionLogic.ClassifyBuild(buildId))
                {
                    case SteamBuildState.Pinned:
                        MelonLogger.Msg($"[Health] Steam BuildID {buildId} — 고정 빌드와 일치합니다.");
                        break;
                    case SteamBuildState.Different:
                        MelonLogger.Warning($"[Health] Steam BuildID {buildId} 가 고정 빌드 {GameVersionLogic.PinnedSteamBuildId} 와 다릅니다. "
                            + "훅 대상이 바뀌었을 수 있으니 [Health] 요약과 AGENTS.md 의 '게임 업데이트 대응 체크리스트'를 확인하세요.");
                        break;
                }

                if (!string.IsNullOrEmpty(targetBuildId) && !string.Equals(targetBuildId, buildId, StringComparison.Ordinal))
                {
                    MelonLogger.Warning($"[Health] Steam 이 BuildID {targetBuildId} 로 업데이트를 대기 중입니다(현재 {buildId}). 다음 실행 전에 업데이트가 적용될 수 있습니다.");
                }

                if (!GameVersionLogic.IsAutoUpdateGuarded(autoUpdate, readOnly))
                {
                    MelonLogger.Warning($"[Health] Steam 자동 업데이트가 막혀 있지 않습니다 (AutoUpdateBehavior={autoUpdate ?? "?"}, 읽기 전용 아님). "
                        + $"고정 빌드를 유지하려면 {acfPath} 의 AutoUpdateBehavior 를 1 로 바꾸고 읽기 전용으로 두세요.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Health] Steam 빌드 확인 실패(무시): {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
