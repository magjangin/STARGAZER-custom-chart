# STARGAZER Custom Chart - 문서 4
# BMS 파서·앨범 구조·표시 오버라이드와 함정 4건 (2026-08-08)

## 목적

- [WORKLOG_03](WORKLOG_03_AREA_STRUCTURE_AND_BMS_INJECTION_2026-07-09.md)에서 "가능하다"까지 확인한 BMS 주입을 실제 파서로 구현한다.
- 커스텀 곡을 하나가 아니라 여러 개 만들 수 있게 한다.
- 자켓·곡 정보·난이도까지 커스텀으로 바꾼다.

## 결과 요약

| 항목 | 상태 |
| --- | --- |
| BMS 파서 + 차트 주입 | 완료 (73마디 552노트 반영 확인) |
| 확장 3자리 사운드 ID | 완료 |
| 롱노트 매핑 | 구현 완료, **실차트 검증 미완** |
| 앨범(폴더=곡) 구조 | 완료 |
| 커스텀 자켓 | 완료 |
| `info.txt` 제목/아티스트 | 완료 |
| 난이도 표시(4개 화면) | 완료 (UI 전용) |
| `config.txt` 오토플레이 설정 | 완료 |

---

## 함정 1 — 커스텀 트랙을 TrackID로 구분하면 공식 곡이 오염된다

처음에는 표시명(`"테스트 "` 접두)으로 커스텀 트랙을 식별했다.
표시명을 사용자 지정으로 바꾸면서 식별 기준을 `TrackID`로 옮겼는데, 이때 공식 곡이 깨졌다.

원인: 커스텀 트랙은 공식 "Starting Point"의 **복제본**이라 `TrackID`가 원본과 완전히 같다(`startingpoint`).
`StartsWith("startingpoint")` 비교라서 "Starting Point (yomoha Jazz Arrange)"까지 걸렸고,
공식 곡 두 개에 커스텀 자켓이 적용됐다.

```
[CustomJacket] Serving custom jacket for 'Starting Point': hwa\thumbnail.png
[CustomJacket] Serving custom jacket for 'Starting Point  (yomoha Jazz Arrange)': hwa\thumbnail.png
```

**해결**: 주입 시점에 복제본의 IL2CPP 네이티브 포인터를 기록하고, 그 객체만 커스텀으로 판정한다.
표시명도 TrackID도 원본과 겹칠 수 있지만 객체 동일성은 겹치지 않는다.
포인터 → 앨범 매핑으로 두어 "어느 폴더의 자원을 쓸지"까지 같은 조회로 해결했다.

## 함정 2 — 난이도를 메타데이터에 쓰면 로딩이 멈춘다

`info.txt`의 난이도를 `INNER_TrackMetaData.levelString`에 반영했더니
곡 진입 후 로딩 화면에서 영구 대기했다.

### 실험 1 (공유 Dictionary에 기록)

정상 실행과 멈춘 실행의 로그가 `AddBundle` 직전까지 **완전히 동일**했고, 다른 것은 난이도 값뿐이었다.

| | 정상 | 멈춤 |
| --- | --- | --- |
| levels | `Cosmic=1, Stellar=3, Void=6` | `Cosmic=8, Stellar=8, Void=8` |
| 이후 | 60ms 뒤 `PlayerBase.Play` 진행 | 아무것도 오지 않음 |

이때 클론의 `levelString`은 `CopyFieldsAndProperties`가 참조만 복사해서
**복제 원본(공식 트랙)과 같은 Dictionary 객체**였다. 공식 곡 데이터까지 덮어쓰고 있었던 것.
이게 원인이라고 보고 분리 처리를 넣었다.

### 실험 2 (클론 전용 Dictionary로 분리 후 기록)

```
[TrackLevel] levelString을 공식 트랙과 분리했습니다  shared=0x214FBBF1F00 new=0x214FDAED000
[TrackLevel] cosmic: '1' -> '8'
[PatternLoad] trackId=startingpoint level=Cosmic levelString=8 (dict=0x214FDAED000) custom=True
[AssetLoader][AddBundle][PRE] ...startingpoint
(끝 — PlayerBase.Play 없음)
```

공식 트랙 Dictionary(`0x214FBBF1F00`)는 그대로 두고 클론 전용(`0x214FDAED000`)에만 썼는데도 똑같이 멈췄다.

**결론**: 공유 오염은 원인이 아니었다. 두 실험의 공통 변수는 "값 변경" 하나뿐이므로,
난이도 문자열은 단순 표시용이 아니라 **패턴 에셋 로딩 경로에서 읽힌다.**
존재하지 않는 이름의 에셋을 조회해 비동기 콜백이 오지 않는 것으로 추정하지만,
인터롭 어셈블리에 메서드 본문이 없어 코드로는 확인하지 못했다.

**해결**: 메타데이터를 건드리지 않고 UI만 덮어쓴다.
분리 처리(`TryDetachLevelStringDictionary`)는 그 자체로 올바른 수정이라 코드에 남겨 두되 호출하지 않는다.

## 함정 3 — 화면마다 난이도를 그리는 컴포넌트가 다르다

`LevelSelector` 하나만 처리하면 될 줄 알았으나 화면마다 달랐다.

| 화면 | 컴포넌트 | 세터 |
| --- | --- | --- |
| 곡 선택 | `FocusedTrackViewer` | `LevelUnit.Set(string)` |
| 난이도 선택 | `LevelSelector` | `LevelItem.FetchLevelText(string)` |
| 플레이 | `CurrentTrackViewer` | `TextProvider.DoTextSetter(string)` |
| 결과 | `PlayInfoViewer` | `LevelItem.FetchLevelText(string)` |

곡 선택 화면 배지는 `LevelSelector`가 아니라 `FocusedTrackViewer`가 그린다.
`LevelSelector`는 곡을 고른 뒤 열리는 **별도 화면**이다.

여기서 추가로 걸린 것들:

- **`LevelSelector.Refresh` 패치 → NRE 폭주.** 호출 빈도가 매우 높고, 패치하면
  `LevelSelector::Refresh`에서 `NullReferenceException`이 초당 수십 번 발생했다.
  재적용은 `FetchTrackRecord` / `FetchJacektImage`로 옮겨 해결.
- **`LevelSelector.SetTrack`은 호출되지 않는다.** 패치 대상 등록은 되지만 실제로 불리지 않아
  커스텀 여부 플래그가 영영 꺼져 있었다. `FocusedTrackViewer.currentTrack` 판정을 재사용해 해결.
- **`SelectionItem<T>.selectedValue`는 리플렉션으로 enum이 안 나온다.**
  제네릭이라 포인터성 정수가 나왔다(`847634592`). `LevelItem`의 오브젝트 이름을 키로 써서 해결.

## 함정 4 — 제네릭 공유로 엉뚱한 콜백에 훅이 걸린다

`TrackLoader.LoadTracksAsync`의 `Action<List<ITrackData>>.Invoke`에 건 패치가
`Action<AudioClip>.Invoke`(프리뷰 로딩 콜백) 호출에도 걸렸다.
IL2CPP가 참조 타입 제네릭 메서드의 네이티브 구현을 공유하기 때문으로 보인다.

증상은 `LoadPreviewClip` 콜백을 호출하는 순간 `"Loaded 0 tracks successfully"`라는
잘못된 로그가 찍히는 것이었다(실제로는 89곡이 정상 로드된 상태).
기능 버그는 아니고 진단 로그 오작동이었지만, 가드가 없으면 이런 오탐이 다른 곳에서도 생길 수 있다.

**해결**: postfix에서 인자가 컬렉션이 아니거나 `UnityEngine.Object`면 무시.

## 그 밖에 확인한 사실

- 곡 재시작 시 패턴이 새로 로드되므로 차트 주입은 **매번 다시** 해야 한다.
  "한 번만 실행" 가드에 묶었더니 재시작 후 원본 차트가 나왔다.
- `INNER_TrackData._metaData`는 필드가 아니라 auto-property다.
- `SoundPlayer`의 실제 네임스페이스는 `Il2CppStarlike.Sound`이며,
  코드에 있던 `Il2CppStargazer.Starlike.Sound.SoundPlayer`는 존재하지 않는 이름이었다.
- `INNER_TrackData.LoadJacketSprite(Action<Sprite>)`는 `LoadBGMClip`/`LoadPreviewClip`과
  시그니처가 같은 트랙별 콜백 로더라, BGM 교체 패턴을 그대로 재사용할 수 있었다.

## 다음 단계

- 롱노트를 실제 차트로 검증한다(테스트 BMS에 `02`/`03` 참조 노트를 배치해야 함).
- 마디를 넘어가는 홀드가 정상 동작하는지 확인한다.
- BPM 변경(채널 03/08)과 마디 길이 변경(채널 02) 지원.
