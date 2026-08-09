# 커스텀 트랙 파이프라인

커스텀 곡이 목록에 뜨고 재생되기까지 어떤 훅이 무엇을 하는지 정리합니다.

## 큰 그림

커스텀 트랙은 **공식 곡 "Starting Point"를 복제해서** 만듭니다.
게임에 새 트랙을 만들 방법이 없어서, 이미 존재하는 트랙 객체를 복제한 뒤 표시 내용만 바꾸는 방식입니다.
이 사실이 아래 설계 전반을 결정합니다.

```
hwa/<폴더>/                     CustomAlbum      폴더 하나 = 곡 하나
      │
      ▼
TrackSelector.Set(List)         앨범 개수만큼 startingpoint 복제 → 목록 맨 앞에 주입
      │                         복제본의 네이티브 포인터 → 앨범 매핑을 등록
      ▼
INNER_TrackData.LoadJacketSprite   자켓 교체    ┐
INNER_TrackData.LoadBGMClip        음원 교체    ├ 매핑 조회로 "내 앨범"을 찾아 서빙
INNER_TrackData.LoadPreviewClip    프리뷰 교체  ┘
      │
      ▼
PlayerBase.Play(TravelArgs)     재생 중인 앨범/난이도 기억, 오토플레이 설정 적용
      │
      ▼
INNER_PatternLoader._Load_b__5_0(Pattern)   Layer.Areas를 BMS 차트로 교체
```

## 커스텀 트랙 식별 — 객체 동일성

**표시명으로도 `TrackID`로도 구분할 수 없습니다.**

- 표시명은 사용자가 `info.txt`로 자유롭게 바꿉니다.
- `TrackID`는 복제 원본과 **완전히 같습니다**(`startingpoint`). `StartsWith` 비교를 쓰면
  공식 "Starting Point"와 "Starting Point (yomoha Jazz Arrange)"까지 커스텀으로 오인해
  공식 곡에 커스텀 자켓·차트가 주입됩니다. 실제로 발생했던 버그입니다.

그래서 **우리가 직접 만든 객체의 IL2CPP 네이티브 포인터**를 주입 시점에 기록하고,
그 포인터를 가진 객체만 커스텀으로 판정합니다.

```
Dictionary<IntPtr, CustomAlbum> InjectedCustomTrackAlbums
```

포인터가 곧 "어느 앨범인가"까지 알려주므로, 자켓·음원·차트·난이도가 모두 이 한 번의 조회로 자기 폴더를 찾습니다.
주입할 때마다 이전 목록을 비웁니다(해제된 주소가 재사용되어 생기는 오탐 방지).

관련 함수: `IsCustomChartTrack`, `TryGetAlbumForTrack`, `RegisterInjectedCustomTrack`
(`src/Hooks/Support/TrackSelector/TrackSelectorCollectionSupport.cs`)

> `IsStartingPointTrack`은 **복제 원본을 찾는 용도로만** 남아 있습니다. 식별에 쓰면 안 됩니다.

## 자켓 / 음원

| 대상 | 훅 | 방식 |
| --- | --- | --- |
| 자켓 | `INNER_TrackData.LoadJacketSprite(Action<Sprite>)` | Prefix에서 원본을 건너뛰고(`return false`) 콜백에 직접 전달 |
| 음원 | `INNER_TrackData.LoadBGMClip(Action<AudioClip>)` | 위와 동일 |
| 프리뷰 | `INNER_TrackData.LoadPreviewClip(Action<AudioClip>)` | 위와 동일 |

세 메서드는 시그니처가 같은 **트랙 인스턴스별 콜백 로더**라 같은 패턴으로 처리합니다.
커스텀 트랙이 아니거나 파일이 없으면 `return true`로 원본에 위임합니다.

콜백은 일반 `Action<T>`가 아니라 IL2CPP delegate 래퍼라서, 리플렉션으로 `Invoke`를 찾아 호출합니다
([CAST_AND_WRAPPER_CODE_REFERENCE.md](CAST_AND_WRAPPER_CODE_REFERENCE.md) 참고).

음원은 `UnityWebRequestMultimedia.GetAudioClip`으로 디코딩해 캐시하고,
로딩이 끝나기 전에 요청이 오면 콜백을 큐에 넣었다가 완료 시 한꺼번에 호출합니다.

## 난이도 표시

**게임 메타데이터(`INNER_TrackMetaData.levelString`)를 바꾸면 안 됩니다.**
바꾸면 곡 진입 후 로딩 화면에서 영구히 멈춥니다.

2026-08-08에 두 번 실험해 확정했습니다.

1. 공유 Dictionary에 8/8/8 기록 → 멈춤
2. 클론 전용 Dictionary로 분리한 뒤 기록(공식 트랙 데이터는 무손상) → **그래도 멈춤**

두 실험의 공통 변수는 "값 변경" 하나뿐이므로, 난이도 문자열은 표시용이 아니라
패턴 에셋 로딩 경로에서 읽힌다고 결론지었습니다. 게임 내부에서 정확히 어디에 쓰는지는
인터롭 어셈블리에 메서드 본문이 없어 확인하지 못했습니다.

그래서 **UI만 덮어씁니다.** 화면마다 그리는 컴포넌트가 다릅니다.

| 화면 | 컴포넌트 | 세터 | 적용 시점 |
| --- | --- | --- | --- |
| 곡 선택 | `Travel.FocusedTrackViewer` | `LevelUnit.Set(string)` | 프로브가 후킹한 4개 메서드의 Postfix |
| 난이도 선택 | `Travel.LevelSelector.LevelSelector` | `LevelItem.FetchLevelText(string)` | `FetchTrackRecord` / `FetchJacektImage` Postfix |
| 플레이 | `Play.Widgets.CurrentTrackViewer` | `TextProvider.DoTextSetter(string)` | `Listen(IPlayManager)` Postfix |
| 결과 | `Travel.Result.PlayInfoViewer` | `LevelItem.FetchLevelText(string)` | `ShowPlayInfo(ITravelResultData)` Postfix |

모두 **게임 자체의 세터**를 호출하므로 원본 서식·색상이 유지됩니다.

### 이 화면들에서 겪은 함정

- **`LevelSelector.Refresh`는 패치하면 안 됩니다.** 호출 빈도가 매우 높고,
  패치하면 `LevelSelector::Refresh`에서 `NullReferenceException`이 초당 수십 번 발생합니다.
  재적용은 `FetchTrackRecord` / `FetchJacektImage`에서 합니다.
- **`LevelSelector.SetTrack`은 실제로 호출되지 않습니다.** 패치 대상으로 등록은 되지만 불리지 않아
  플래그가 켜지지 않습니다. 난이도 선택 화면은 지금 포커스된 곡으로만 열리므로,
  `FocusedTrackViewer.currentTrack`으로 확정한 판정을 재사용합니다.
- **`SelectionItem<T>.selectedValue`는 리플렉션으로 읽으면 enum이 아닙니다.**
  제네릭이라 포인터성 정수가 나옵니다(실측: `847634592`).
  대신 `LevelItem`의 오브젝트 이름(`Cosmic`/`Stellar`/`Void`)을 키로 씁니다.

## 오토플레이

`PlayerBase.Play` Prefix에서 `IsAutoPlay`를 설정합니다.
켜고 끄는 값은 `savecustomkey/config.txt`의 `autoplay`에서 읽습니다(게임 시작 시 1회).

## 노트 연출 (흔들림 / 속도 카오스)

`savecustomkey/config.txt`의 `NoteSway`, `NoteSpeedChaos`로 켜는 순수 시각 효과입니다.
설정 항목은 [GUIDE_CUSTOM_ALBUM.md](GUIDE_CUSTOM_ALBUM.md#노트-연출)를 보세요.

구현: [`src/Hooks/Support/Play/NoteMotionSupport.cs`](../STARGAZER%20custom%20chart/src/Hooks/Support/Play/NoteMotionSupport.cs),
[`src/Hooks/Patches/Play/NoteMotionPatches.cs`](../STARGAZER%20custom%20chart/src/Hooks/Patches/Play/NoteMotionPatches.cs)

### 왜 판정에 영향이 없나

판정은 `JudgementUnit.Judge(NoteObjectBase)`가 노트의 `Timing`(= `PrimitiveNote.Timing`, 차트 데이터)과
재생 시각을 `AcceptableTiming`으로 비교해 냅니다. 노트 오브젝트의 `RectTransform`은 이 경로에 들어가지 않습니다.
그래서 **RectTransform만 옮기는 한** 판정·점수·기록은 그대로입니다.

게임의 속도 관련 필드(`NoteObjectBase.speedMultipleir`, `StargazerNote.MoveSpeed`)는 **건드리지 않습니다.**
그 값들이 렌더링에만 쓰이는지 타이밍에도 쓰이는지 확인할 방법이 없어서(인터롭에 본문 없음),
속도 카오스도 "위치를 배율만큼 스케일"하는 방식으로 구현했습니다.

### 훅 지점

| 대상 | 시점 | 하는 일 |
| --- | --- | --- |
| `StargazerNote.Behaviour(Single)` | Prefix | 지난 프레임에 우리가 얹은 오프셋을 되돌려 게임에 원래 위치를 돌려준다 |
| `StargazerLongNote.Behaviour(Single)` | Postfix | 게임이 계산한 위치를 기준으로 오프셋을 다시 얹는다 |

- 인자 `deltatime`은 **판정선까지 남은 초**입니다(`NoteObjectBase.OnUpdate`가 `Timing - 현재시각`을 넘김).
  판정선을 지나면 음수가 됩니다. 흔들림 감쇠가 이 값을 씁니다.
- Prefix/Postfix 쌍으로 처리하는 이유는, 게임이 위치를 절대값으로 쓰든 이전 값에 누적하든
  **오프셋이 쌓이지 않게** 하기 위함입니다.
- 롱노트는 `Behaviour`를 따로 override하므로 두 타입 모두 후킹합니다.
  override가 `base.Behaviour`를 부르면 한 프레임에 두 번 들어오는데, 노트별 프레임 가드가 두 번째를 걸러 냅니다.
- **이 훅은 매 프레임 노트마다 불립니다.** 공용 `HookPrefix`/`HookPostfix`(로깅 포함)를 쓰면 안 됩니다.
  설정이 전부 꺼져 있으면 `Prepare()`가 `false`를 돌려줘 아예 패치되지 않습니다.

> `TargetMethods()`가 **빈 목록**을 돌려주면 Harmony가 어트리뷰트에서 대상을 찾으려다
> `"Undefined target method"`로 던집니다. 조건부 패치는 반드시 `Prepare()`로 막아야 합니다.

### 속도 카오스 — 진행 방향은 직접 잰다

**`StargazerNote.posRef`는 판정선 기준점이 아닙니다.** 이름만 보고 "노트가 판정선에 놓였을 때의 위치"로
가정했다가 틀렸습니다. 2026-08-09 실측에서 노트의 현재 위치와 **값이 완전히 같았습니다.**

```
[NoteMotion] 첫 노트: pos=(0,1435.7) posRef=(0,1435.7) deltatime=2.991s
```

그래서 기준점을 추측하지 않고, 노트가 실제로 움직이는 것을 두 프레임 재서 씁니다.

```
v      = Δ위치 / Δdeltatime          (deltatime = 판정선까지 남은 시간)
P(t)   = 판정선 + v·t
P(t·f) = P(t) + v·t·(f-1)            (f = 속도 배율)
```

판정선 위치를 몰라도 **오프셋만으로** 계산됩니다. `t`가 0으로 갈수록 오프셋도 0이라
배율이 얼마든 판정선에는 정확히 제때 도착합니다 — 보이는 속도만 달라집니다.

흔들림 축도 이 속도에서 정합니다(`|v.y| >= |v.x|`이면 X축으로 흔듦). 진행 축과 직각으로 흔들어야
"옆으로 흔들린다"가 되기 때문입니다.

측정에는 두 프레임이 필요하므로, 노트가 생긴 첫 프레임에는 아직 속도를 모릅니다.
그대로 두면 두 번째 프레임에 오프셋이 한꺼번에 붙어 노트가 튀므로, **레인 단위로 속도를 공유**해
같은 레인의 다른 노트가 이미 잰 값을 새 노트가 첫 프레임부터 씁니다.
(레인 키는 노트 부모 Transform의 `InstanceID`.)

곡마다 한 번 `[NoteMotion] 진행 방향 측정: ...` 로그로 실제 측정값을 남깁니다.

## 차트 주입

`INNER_PatternLoader._Load_b__5_0(Pattern)` Postfix에서 `Layer.Areas`를 통째로 교체합니다.
자세한 규칙은 [REFERENCE_BMS_CONVERSION.md](REFERENCE_BMS_CONVERSION.md)를 보세요.

주의할 점 두 가지:

- 이 콜백은 **공식 곡이든 커스텀 곡이든 패턴이 로드될 때마다** 불립니다.
  커스텀 트랙일 때만 개입하도록 반드시 막아야 합니다(`IsCustomChartPlayActive`).
  파괴적 작업이라 주입 함수 안에서도 한 번 더 확인합니다.
- 곡 재시작마다 패턴이 새로 로드되므로 **매번 다시 주입해야 합니다.**
  "한 번만 실행" 가드에 묶으면 재시작 시 원본 차트가 나옵니다.

## 기타 실측 메모

- `INNER_TrackData._metaData`는 필드가 아니라 **auto-property**입니다. 필드 조회가 먼저면 실패합니다.
- `SoundPlayer`의 실제 네임스페이스는 `Il2CppStarlike.Sound`입니다.
  `Il2CppStargazer.Starlike`라는 네임스페이스는 존재하지 않습니다.
- IL2CPP는 참조 타입 제네릭 메서드의 네이티브 구현을 공유할 수 있습니다.
  `Action<List<ITrackData>>.Invoke`에 건 패치가 `Action<AudioClip>.Invoke` 호출에도 걸려
  잘못된 로그가 찍힌 사례가 있습니다. 인자 타입을 확인하는 가드가 필요합니다.
