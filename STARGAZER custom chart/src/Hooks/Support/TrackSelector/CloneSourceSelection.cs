using System;
using System.Collections.Generic;

namespace STARGAZER_custom_chart
{
    // 곡 목록에서 "복제 원본(공식 Starting Point)"을 고르는 순수 로직. 게임/리플렉션 의존성이 없어 단독 검증된다.
    //
    // 왜 정확 일치를 먼저 보는가: 공식 "Starting Point (yomoha Jazz Arrange)"의 TrackID도 "startingpoint"로
    // 시작하므로 StartsWith만 쓰면 목록 순서에 따라 Jazz Arrange가 먼저 걸려 그 곡(음원·패턴·자켓)이
    // 복제될 수 있다. ID가 정확히 "startingpoint"인 항목을 우선하고, 없을 때만 접두 일치로 내려간다.
    // (커스텀 곡 "식별"은 여전히 IntPtr 동일성으로만 한다 — 이 함수는 복제 원본 선택 전용이다.)
    internal static class CloneSourceSelection
    {
        public const string StartingPointId = "startingpoint";

        public static T? Pick<T>(IEnumerable<T> items, Func<T, string?> idOf, out bool exactMatch)
        {
            exactMatch = false;
            T? prefixMatch = default;
            bool hasPrefixMatch = false;

            foreach (T item in items)
            {
                string? id = idOf(item);
                if (id is null)
                {
                    continue;
                }

                string trimmed = id.Trim();
                if (string.Equals(trimmed, StartingPointId, StringComparison.OrdinalIgnoreCase))
                {
                    exactMatch = true;
                    return item;
                }

                if (!hasPrefixMatch && trimmed.StartsWith(StartingPointId, StringComparison.OrdinalIgnoreCase))
                {
                    prefixMatch = item;
                    hasPrefixMatch = true;
                }
            }

            return hasPrefixMatch ? prefixMatch : default;
        }
    }
}
