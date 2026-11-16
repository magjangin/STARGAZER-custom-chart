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
            lock (Gate)
            {
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

        // GameAssembly.dll 지문을 이전 실행과 비교한다. 파일이 없거나 읽을 수 없으면 조용히 넘어간다(보조 기능).
        public static void CheckGameFingerprint()
        {
            try
            {
                string gameAssembly = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameAssembly.dll");
                if (!File.Exists(gameAssembly))
                {
                    return;
                }

                var info = new FileInfo(gameAssembly);
                string current = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
                string stampPath = Path.Combine(CustomConfig.FolderPath, ".game-fingerprint");

                string? previous = File.Exists(stampPath) ? File.ReadAllText(stampPath).Trim() : null;
                if (previous is null)
                {
                    MelonLogger.Msg($"[Health] 게임 바이너리 지문을 처음 기록합니다 (GameAssembly.dll {info.Length:N0} bytes).");
                }
                else if (!string.Equals(previous, current, StringComparison.Ordinal))
                {
                    MelonLogger.Warning("[Health] GameAssembly.dll 이 지난 실행과 다릅니다 — 게임이 업데이트된 것으로 보입니다. "
                        + "확인 순서: [HookPatch][attr] target not found → [HookPatch][resolve] → [DualWrapper] → [FocusedTrackViewerProbe] → [Health] 요약. "
                        + "그리고 커스텀 곡 실제 플레이(BMS 주입·Save Guard)를 한 번 확인하세요.");
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
    }
}
