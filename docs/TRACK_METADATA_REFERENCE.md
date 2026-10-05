# Il2CppStargazer Track Metadata Reference

이 문서는 `Sixtar Gate STARGAZER` 게임 내의 트랙 메타데이터 구조 및 디버그 분석 로그 참조 가이드입니다. 

---

## 1. 수집된 실제 디버그 로그 (Reference Log)

아래 로그는 곡 선택 화면에서 `startingpoint` (오리지널 및 복제 주입본) 트랙의 내부 데이터에 접근할 때 감지되어 파싱된 실제 런타임 덤프 로그입니다.

> 이 로그를 찍은 `[Accessor]` / `[TrackMetaDump]` 훅(표시명 게터 후킹)은 현재 코드에 없습니다.
> 지금은 표시명을 게터에서 바꾸지 않고 복제본 메타데이터에 직접 씁니다. 멤버 구조를 보여 주는 과거 기록으로만 보세요.

```log
[22:17:29.411] [STARGAZER custom chart] [Accessor][TrackDisplayName] trackId=startingpoint result=Starting Point
[22:17:29.412] [STARGAZER custom chart] [TrackMetaDump] caller=Accessor.TrackDisplayNamePostfix metaType=Il2CppStargazer.TrackLoader+INNER_TrackMetaData id=startingpoint display=Starting Point displayEN=? bundle=<none> episode=<none>
[22:17:29.417] [STARGAZER custom chart] [TrackMetaDump] members (20): P:trackid(W)=startingpoint, P:displayName(W)=Starting Point, P:displayNameEN(W)=<null>, P:order(W)=20241015, P:composer(W)=LucaProject, P:licenseCopy(W)=<null>, P:artistInfos(W)=Il2CppSystem.Collections.Generic.Dictionary`2[System.String,System.String], P:category(W)=Original, P:bgaOFfset(W)=0, P:bgaExist(W)=True, P:lockType(W)=<null>, P:unlockValue(W)=<null>, P:levelString(W)=Il2CppSystem.Collections.Generic.Dictionary`2[Il2CppStargazer.Travel.ELevels,System.String], P:_extraLevels(W)=Il2CppSystem.Collections.Generic.List`1[Il2CppStargazer.TrackLoader+INNER_ExtraLevelData], P:ExtraLevelList(R)=Il2CppSystem.Collections.Generic.List`1[System.String], P:ObjectClass(R)=2217587472464, P:Pointer(R)=2226826956288, P:WasCollected(R)=False, F:isWrapped(RW)=False, F:pooledPtr(RW)=2226826956288
```

---

## 2. `INNER_TrackMetaData` 멤버 분석 (Member Specifications)

`INNER_TrackMetaData` 클래스(`Il2CppSystem.Object` 상속)가 보유한 런타임 프로퍼티(`P:`) 및 필드(`F:`) 목록 분석입니다. 
*(W = 쓰기 가능, R = 읽기 전용, RW = 읽고 쓰기 가능)*
타입은 `decompiled/Assembly-CSharp/Il2CppStargazer/TrackLoader.cs`의 시그니처 기준입니다.

| 멤버 이름 | 타입 / 특징 | 런타임 실제 예시 값 | 용도 및 설명 |
| :--- | :--- | :--- | :--- |
| **`P:trackid`** (W) | `System.String` | `"startingpoint"` | 곡을 식별하는 고유 ID (알파벳 소문자) |
| **`P:displayName`** (W) | `System.String` | `"Starting Point"` | 게임 내 노출되는 기본 곡 이름 |
| **`P:displayNameEN`** (W) | `System.String` | `<null>` | 영문 모드에서 표시될 곡 이름 |
| **`P:composer`** (W) | `System.String` | `"LucaProject"` | 작곡가 이름 |
| **`P:artistInfos`** (W) | `Il2CppSystem.Collections...` | `Dictionary<string, string>` | 일러스트레이터, BGA 제작자 등 아티스트 크레딧 딕셔너리 |
| **`P:category`** (W) | `System.String` | `"Original"` | 곡 카테고리 (예: Original, Licensing 등) |
| **`P:order`** (W) | `System.Int32` | `20241015` | 정렬 순서값 (주로 출시일 형태) |
| **`P:bgaOFfset`** (W) | `System.Single` | `0` | 백그라운드 애니메이션(BGA)의 오프셋 시간 싱크 조절용 |
| **`P:bgaExist`** (W) | `System.Boolean` | `True` | BGA 영상 존재 여부 |
| **`P:licenseCopy`** (W) | `System.String` | `<null>` | 라이선스 표기문구 |
| **`P:lockType`** (W) | `System.String` | `<null>` | 해금 타입 제어 속성 |
| **`P:unlockValue`** (W) | `System.String` | `<null>` | 해금에 필요한 요구 조건값 |
| **`P:levelString`** (W) | `Il2CppSystem.Collections...` | `Dictionary<ELevels, string>` | 난이도 등급 표기 정보 |
| **`P:_extraLevels`** (W) | `Il2CppSystem.Collections...` | `List<INNER_ExtraLevelData>` | 추가 특수 패턴 난이도 데이터 |
| **`P:ExtraLevelList`** (R) | `Il2CppSystem.Collections...` | `List<string>` | 읽기 전용 엑스트라 레벨 리스트 |
| **`P:Pointer`** (R) | `System.IntPtr` | `2226826956288` | 메모리 내 IL2CPP 객체 실제 주소 포인터 |
| **`F:pooledPtr`** (RW) | `System.IntPtr` | `2226826956288` | 풀링된 메모리 포인터 |
| **`F:isWrapped`** (RW) | `System.Boolean` | `False` | 래핑 여부 플래그 |

---

## 3. 커스텀 곡 제작 및 메타데이터 주입 시 힌트

* **포인터는 식별용**: 
  `Pointer`는 IL2CPP 래퍼가 가리키는 네이티브 객체 주소입니다. 이 모드는 메모리를 직접 고치지 않고,
  주입한 복제 트랙을 공식 트랙과 구분하는 데(`Dictionary<IntPtr, CustomAlbum>`)만 씁니다
  ([REFERENCE_CUSTOM_TRACK_PIPELINE.md](REFERENCE_CUSTOM_TRACK_PIPELINE.md#커스텀-트랙-식별--객체-동일성)).
* **딕셔너리 구조 필드**:
  `artistInfos` 및 `levelString` 같은 구조는 복제 주입 시 단순히 값을 할당하는 것뿐만 아니라 `Il2CppSystem.Collections` 클래스군과의 호환 처리가 필요함을 시사합니다.
* **`levelString`은 바꾸면 안 됩니다**: 
  값을 바꾸면 곡 진입 후 로딩이 멈춥니다(2026-08-08 실측). 난이도는 UI 표시만 덮어씁니다
  ([REFERENCE_CUSTOM_TRACK_PIPELINE.md](REFERENCE_CUSTOM_TRACK_PIPELINE.md#난이도-표시)).
