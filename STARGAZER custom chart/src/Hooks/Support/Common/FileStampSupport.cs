using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace STARGAZER_custom_chart
{
    public sealed partial class StargazerCustomChartMod
    {
        // 자켓·BGM·프리뷰 캐시는 파일 수정 시각으로 핫리로드를 지원하는데, 서빙 훅이 곡 선택 커서가 움직일 때마다
        // (FocusedTrackViewer가 자켓·프리뷰를 다시 요청) 불려 매번 파일 시스템 조회(GetLastWriteTimeUtc/Exists)를 했다.
        // 같은 경로는 StampRecheckSeconds 안에서는 마지막으로 본 값을 그대로 쓴다. 파일을 고치면 최대 그만큼 늦게 반영된다.
        // 메인 스레드 전용(Harmony 훅·Unity 코루틴 모두 메인 스레드).
        private const double StampRecheckSeconds = 2.0;

        private static readonly Dictionary<string, (long CheckedAt, DateTime LastWriteUtc)> FileStampCache =
            new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);

        // File.GetLastWriteTimeUtc는 파일이 없으면 예외 대신 1601-01-01(UTC)을 돌려준다.
        private static readonly DateTime MissingFileStamp = DateTime.FromFileTimeUtc(0);

        private static DateTime GetLastWriteTimeUtcThrottled(string fullPath)
        {
            long now = Stopwatch.GetTimestamp();
            if (FileStampCache.TryGetValue(fullPath, out var cached)
                && (now - cached.CheckedAt) < (long)(StampRecheckSeconds * Stopwatch.Frequency))
            {
                return cached.LastWriteUtc;
            }

            return GetLastWriteTimeUtcFresh(fullPath);
        }

        // 디코딩을 시작하는 등 "지금 값"이 필요한 곳에서 쓴다. 본 값을 캐시에도 넣어, 직후 throttled 조회가
        // 낡은 값을 돌려줘 방금 만든 캐시 항목을 수정 시각 불일치로 버리는 일이 없게 한다.
        private static DateTime GetLastWriteTimeUtcFresh(string fullPath)
        {
            DateTime stamp = File.GetLastWriteTimeUtc(fullPath);
            FileStampCache[fullPath] = (Stopwatch.GetTimestamp(), stamp);
            return stamp;
        }

        // 서빙 캐시 키. 앨범 경로는 CustomAlbum.TryLoad에서 이미 정규화돼 있어 보통 그대로 돌려준다
        // (Path.GetFullPath는 문자열 정규화·할당이 있어 커서 이동마다 반복할 필요가 없다).
        private static string ToServePath(string path) =>
            Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(path);

        private static bool FileExistsThrottled(string fullPath) => GetLastWriteTimeUtcThrottled(fullPath) != MissingFileStamp;
    }
}
