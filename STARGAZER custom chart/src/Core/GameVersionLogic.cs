using System;
using System.Text.RegularExpressions;

namespace STARGAZER_custom_chart
{
    internal enum FingerprintComparison
    {
        FirstRun,
        Same,
        Changed,
    }

    internal enum SteamBuildState
    {
        Unknown,
        Pinned,
        Different,
    }

    // 게임 버전 판정용 순수 로직(Unity/MelonLoader 의존 없음 → xUnit 테스트 대상).
    //
    // 지문(fingerprint) 형식 v2: "v2|ga=<크기>:<수정Ticks>|meta=<크기>:<수정Ticks>"
    //  - ga   = GameAssembly.dll (Il2Cpp 네이티브 본체)
    //  - meta = il2cpp_data/Metadata/global-metadata.dat (타입·메서드 이름 테이블. 없으면 "none")
    // v1(구버전, 2026-11-16~)은 GameAssembly.dll 만 "<크기>:<수정Ticks>"로 기록했다. v1 파일을 만나면
    // ga 부분만 비교해 같으면 Same으로 보고(형식만 조용히 갱신), 다르면 Changed로 본다 — 형식 변경 때문에
    // 업데이트 경고가 거짓으로 한 번 뜨는 일을 막기 위해서다. 같은 이유로 meta=none 은 비교에서 뺀다.
    internal static class GameVersionLogic
    {
        // AGENTS.md §1과 같은 값. 고정 빌드가 바뀌면 문서와 함께 갱신할 것.
        internal const string SteamAppId = "3446490";
        internal const string PinnedSteamBuildId = "21205351";

        private const string V2Prefix = "v2|";
        private const string MissingPart = "none";

        internal static string BuildFingerprint(long gameAssemblyLength, long gameAssemblyWriteTicks, long? metadataLength, long? metadataWriteTicks)
        {
            string meta = metadataLength is long length && metadataWriteTicks is long ticks
                ? $"{length}:{ticks}"
                : MissingPart;
            return $"{V2Prefix}ga={gameAssemblyLength}:{gameAssemblyWriteTicks}|meta={meta}";
        }

        internal static FingerprintComparison Compare(string? previous, string current)
        {
            previous = previous?.Trim();
            if (string.IsNullOrEmpty(previous))
            {
                return FingerprintComparison.FirstRun;
            }

            if (string.Equals(previous, current, StringComparison.Ordinal))
            {
                return FingerprintComparison.Same;
            }

            if (!previous.StartsWith(V2Prefix, StringComparison.Ordinal))
            {
                // v1: GameAssembly.dll 부분만 기록돼 있다.
                string? currentGameAssembly = TryGetPart(current, "ga");
                return string.Equals(previous, currentGameAssembly, StringComparison.Ordinal)
                    ? FingerprintComparison.Same
                    : FingerprintComparison.Changed;
            }

            // v2끼리: 한쪽이라도 meta=none(파일 못 찾음)이면 meta는 "알 수 없음"으로 보고 ga만 비교한다.
            // (경로 해석 실패로 none이 기록된 다음 실행에서 거짓 업데이트 경고가 뜨는 것을 막는다.)
            return HasPartChanged(previous, current, "ga") || HasPartChanged(previous, current, "meta")
                ? FingerprintComparison.Changed
                : FingerprintComparison.Same;
        }

        // 어느 파일이 바뀌었는지 로그에 남기기 위한 보조 함수. v1/v2 모두 처리한다.
        internal static bool HasPartChanged(string? previous, string current, string part)
        {
            previous = previous?.Trim();
            if (string.IsNullOrEmpty(previous))
            {
                return false;
            }

            string? before = previous.StartsWith(V2Prefix, StringComparison.Ordinal)
                ? TryGetPart(previous, part)
                : (part == "ga" ? previous : null);
            string? after = TryGetPart(current, part);
            if (before is null || after is null || before == MissingPart || after == MissingPart)
            {
                // v1에는 meta 정보가 없고, none은 "못 찾음"일 뿐이므로 "바뀌었다"고 단정하지 않는다.
                return false;
            }

            return !string.Equals(before, after, StringComparison.Ordinal);
        }

        internal static string? TryGetPart(string fingerprint, string part)
        {
            if (string.IsNullOrEmpty(fingerprint) || !fingerprint.StartsWith(V2Prefix, StringComparison.Ordinal))
            {
                return null;
            }

            string key = part + "=";
            foreach (string segment in fingerprint.Substring(V2Prefix.Length).Split('|'))
            {
                if (segment.StartsWith(key, StringComparison.Ordinal))
                {
                    return segment.Substring(key.Length);
                }
            }

            return null;
        }

        // Steam appmanifest_*.acf(Valve KeyValues 텍스트)에서 "key" "value" 첫 항목을 읽는다.
        // 따옴표로 키 경계를 고정하므로 "buildid"가 "TargetBuildID"에 걸리지 않는다(대소문자 무시).
        internal static string? TryReadAcfValue(string? acfText, string key)
        {
            if (string.IsNullOrEmpty(acfText) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            Match match = Regex.Match(
                acfText,
                "\"" + Regex.Escape(key) + "\"[ \\t]+\"([^\"\\r\\n]*)\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : null;
        }

        internal static SteamBuildState ClassifyBuild(string? buildId)
        {
            if (string.IsNullOrWhiteSpace(buildId))
            {
                return SteamBuildState.Unknown;
            }

            return string.Equals(buildId.Trim(), PinnedSteamBuildId, StringComparison.Ordinal)
                ? SteamBuildState.Pinned
                : SteamBuildState.Different;
        }

        // Steam StateFlags 비트 2(UpdateRequired): 업데이트가 내려와 적용을 기다리는 상태(4 = 설치 완료, 6 = 설치 완료 + 업데이트 필요).
        private const int SteamStateFlagUpdateRequired = 2;

        // Steam이 업데이트를 대기 중인지(다음 실행 전에 고정 빌드가 깨질 수 있는지) 판정한다.
        //  - TargetBuildID가 비었거나 "0"이면 "대기 없음"이다. Steam은 대기 중인 업데이트가 없을 때 0을 쓰기도 해서,
        //    예전처럼 buildid와 다르기만 하면 경고하면 "BuildID 0 으로 업데이트 대기 중"이라는 거짓 경고가 뜰 수 있었다.
        //  - StateFlags에 UpdateRequired 비트가 있으면 TargetBuildID와 무관하게 대기 중이다.
        internal static bool IsUpdatePending(string? buildId, string? targetBuildId, string? stateFlags)
        {
            string? target = targetBuildId?.Trim();
            bool targetPending = !string.IsNullOrEmpty(target)
                && !string.Equals(target, "0", StringComparison.Ordinal)
                && !string.Equals(target, buildId?.Trim(), StringComparison.Ordinal);

            bool flagPending = int.TryParse(stateFlags?.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int flags)
                && (flags & SteamStateFlagUpdateRequired) != 0;

            return targetPending || flagPending;
        }

        // Steam AutoUpdateBehavior: 0 = 항상 최신 유지, 1 = 실행할 때만 업데이트, 2 = 높은 우선순위.
        // 고정 빌드를 지키려면 "1"이거나 ACF가 읽기 전용이어야 한다(AGENTS.md §1).
        internal static bool IsAutoUpdateGuarded(string? autoUpdateBehavior, bool acfReadOnly)
        {
            return acfReadOnly || string.Equals(autoUpdateBehavior?.Trim(), "1", StringComparison.Ordinal);
        }
    }
}
