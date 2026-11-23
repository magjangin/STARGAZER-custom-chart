using System.Collections.Generic;

namespace STARGAZER_custom_chart
{
    // TrackSelector.Set 주입 계획을 세우는 순수 로직(게임/리플렉션 의존 없음 → xUnit 대상).
    //
    // 왜 필요한가: 예전에는 Set이 새 목록으로 불릴 때마다 등록표를 비우고 앨범마다 새로 복제했다.
    // 그러면 이전 복제본(이미 화면·포커스·결과 데이터가 붙잡고 있을 수 있음)이 커스텀 식별을 잃어
    // 공식 "Starting Point"로 취급되고, 그 상태로 플레이·저장되면 공식 기록이 오염될 수 있었다.
    // 지금은 앨범마다 복제본을 한 번만 만들어 계속 재사용하고, 목록에 이미 들어 있는 복제본은 다시 넣지 않는다.
    internal static class CustomTrackInjectionLogic
    {
        /// <summary>
        /// 앨범 순서대로 "목록에 넣어야 하는" 앨범 인덱스를 돌려준다.
        /// <paramref name="clonePerAlbum"/>[i]는 i번째 앨범의 기존 복제본 식별자(없으면 null),
        /// <paramref name="itemsInList"/>는 현재 목록에 들어 있는 항목 식별자 집합이다.
        /// 복제본이 없거나(새로 복제 필요) 목록에 없는(재삽입 필요) 앨범이 결과에 들어간다.
        /// </summary>
        internal static List<int> FindAlbumsMissingFromList<T>(IReadOnlyList<T?> clonePerAlbum, ICollection<T> itemsInList)
            where T : struct
        {
            var missing = new List<int>();
            for (int i = 0; i < clonePerAlbum.Count; i++)
            {
                T? clone = clonePerAlbum[i];
                if (clone is null || !itemsInList.Contains(clone.Value))
                {
                    missing.Add(i);
                }
            }

            return missing;
        }
    }
}
