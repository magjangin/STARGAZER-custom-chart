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

**복제 원본 선택도 같은 이유로 정확 일치를 우선합니다.** `CloneSourceSelection.Pick`은 곡 목록에서 ID가 정확히
`startingpoint`인 트랙을 먼저 고르고, 없을 때만 접두 일치(`StartsWith`) 트랙으로 내려가며 `[TrackSelector.Set]` 경고를
남깁니다. 접두 일치만 쓰면 목록 순서에 따라 "Jazz Arrange"가 원본으로 복제될 수 있기 때문입니다.
(이 함수는 *복제 원본 선택* 전용이고, 커스텀 곡 *식별*은 아래 포인터 동일성으로만 합니다.)

그래서 **우리가 직접 만든 객체의 IL2CPP 네이티브 포인터**를 주입 시점에 기록하고,
그 포인터를 가진 객체만 커스텀으로 판정합니다.

```
Dictionary<IntPtr, (CustomAlbum Album, object TrackRef)> InjectedCustomTrackAlbums
```

포인터가 곧 "어느 앨범인가"까지 알려주므로, 자켓·음원·차트·난이도가 모두 이 한 번의 조회로 자기 폴더를 찾습니다.
관리 래퍼 참조(`TrackRef`)도 함께 보관해 GC 수거로 인한 네이티브 주소 재사용을 막으며,
주입할 때마다 이전 목록을 비웁니다.

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

대상은 `StargazerNote.Behaviour(Single)`와 `StargazerLongNote.Behaviour(Single)` 두 개이며, **둘 다** Prefix와 Postfix를 겁니다.

| 시점 | 하는 일 |
| --- | --- |
| Prefix | 지난 프레임에 우리가 얹은 오프셋을 되돌려 게임에 원래 위치를 돌려준다 |
| Postfix | 게임이 계산한 위치를 기준으로 오프셋을 다시 얹는다 |

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
> 이 규칙은 노트 연출만이 아니라 **모든 패치 클래스**에 적용됩니다 — 아래 [패치 대상 해석](#패치-대상-해석) 참고.

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

## 차트 주입 (2계층: 람다 Postfix + onCompleted 이중래퍼)

1차 경로인 `INNER_PatternLoader._Load_b__5_0(Pattern)` Postfix와 2차 백업 경로인 `INNER_PatternLoader.Load(TravelArgs)` Prefix의 `onCompleted` 이중래퍼(Dual-Wrapper)에서 `Layer.Areas`를 통째로 교체합니다.
자세한 변환 규칙은 [REFERENCE_BMS_CONVERSION.md](REFERENCE_BMS_CONVERSION.md)를 보세요.

- **1차 경로**: 컴파일러 생성 람다(`_Load_b__5_0`). 게임 업데이트로 람다 번호나 클로저 위치(`__c`, `__c__DisplayClassN_M`)가 바뀌어도 `ResolveLambdaMethod`가 소유 메서드(`Load`)와 정확한 시그니처(`Pattern`) 및 유일성 조건으로 추적합니다.
- **2차 경로 (Dual-Wrapper)**: 1차 람다 훅이 모호하거나 사라진 경우를 대비해, `INNER_PatternLoader.Load` Prefix에서 인스턴스의 `onCompleted`(`Il2CppSystem.Action<Pattern>`, 이름 변경 시 타입 기반 유일 멤버 폴백)를 래퍼로 감쌉니다.
- **중복 주입 가드**: 두 경로가 모두 살아 있으면 람다 본문 안의 `onCompleted(=래퍼)`가 먼저 불리고 직후에 람다 Postfix가 불립니다. `ProbeNoteArrayMembers`가 `Pattern`의 IL2CPP 네이티브 포인터(`LastProbedPatternPtr`)를 비교하여 같은 패턴에 대해 차트가 두 번 주입되지 않도록 막습니다(관리 래퍼는 호출마다 새 인스턴스일 수 있어 `ReferenceEquals`는 쓰지 않음).
- **공식 곡 보호**: 이 콜백은 공식 곡이든 커스텀 곡이든 패턴이 로드될 때마다 불립니다. 커스텀 트랙일 때만 개입하도록 호출부와 주입 함수(`TryInjectBmsChart`) 양쪽에서 `IsCustomChartPlayActive && CurrentPlayAlbum is not null`을 이중 확인합니다.
- **곡 재시작 대응**: 곡 재시작(`PlayerBase.Play`)마다 `LastProbedPatternPtr`과 `WrappedPatternLoaders`를 비우므로, 재시작 시 새로 로드된 패턴에도 정상적으로 다시 주입됩니다.
- 앨범에 `.bms`가 없으면 주입을 건너뛰어 **Starting Point 원본 차트**가 그대로 나옵니다.

## 기록 저장 차단 (Save Guard)

커스텀 트랙은 `TrackID`가 Starting Point와 같아서, 결과를 그대로 저장하면 **공식 Starting Point 기록을 덮어씁니다.**

| 훅 | 방식 |
| --- | --- |
| `UserDataLoader+INNER_TrackRecordModule.SaveTrackRecord(ITravelResultData)` | Prefix에서 `PlayData.PlayTrack`이 우리가 주입한 객체면 `return false`로 저장을 건너뜀 |

- `result`에서 `PlayData`(`TravelArgs`) → `PlayTrack`(`ITrackData`) 추출은 `TryGetMemberByType`을 사용해 프로퍼티 이름이 바뀌어도 동작합니다.
- 만약 게임 업데이트로 `result` 또는 `PlayTrack` 추출 자체가 실패하더라도, 현재 `IsCustomChartPlayActive && CurrentPlayAlbum is not null` 상태라면 **방어적으로 저장을 차단(`return false`)**하여 공식 Starting Point 기록이 오염되는 것을 막습니다.
- `PlayTrack`은 꺼냈지만 주입 객체로 식별되지 않는데 플레이 시작 시엔 커스텀 곡이었다면(업데이트로 결과 데이터가 트랙을 복사·래핑하는 경우) `BlockDefensiveTrackMismatch`로 역시 차단합니다(2026-11-20).
- 저장 모듈 클래스 이름이 통째로 바뀌어도 `PatchSpec.WithTypeScanFallback()`이 게임 전체에서 `SaveTrackRecord(ITravelResultData)`(대체 이름 포함) 구체 메서드가 **유일**하면 그것에 겁니다. Il2CppInterop이 인터페이스 `ITrackRecordModule`을 일반 클래스로 노출하므로 네이티브 클래스 플래그로 인터페이스/추상 클래스를 걸러 냅니다(실기 검증 완료, `AGENTS.md` 1차 계층 4단계).
- 판정 규칙은 순수 로직 `SaveGuardLogic.Evaluate`(단위 테스트 대상)에 있습니다.
- 구현: [`src/Hooks/Patches/UserData/TrackRecordSaveGuardPatch.cs`](../STARGAZER%20custom%20chart/src/Hooks/Patches/UserData/TrackRecordSaveGuardPatch.cs)

> **범위 밖(덤프 대조 2026-11-20).** 막는 것은 `SaveTrackRecord` 하나뿐입니다. `ITrackRecordModule` 구현체는 현재
> `UserDataLoader+INNER_TrackRecordModule` 하나뿐이라 다른 기록 저장 경로는 없습니다. 다만 결과 화면 `ResultViewer`의
> `RewardVolts`(볼트 보상 → `INNER_CurrencyModule.AddVolts`), `UpdateStatistics`(통계), `CheckAchievement`(도전과제)는
> 커스텀 곡에서도 그대로 동작합니다. `UnlockAll`이 켜져 있으면 `GetVolts`가 999,999로 고정돼 볼트 영향은 체감되지 않고,
> 도전과제는 골드버그 에뮬레이터라 오프라인에 머뭅니다. 차단이 필요해지면 결과 화면이 Play 씬 안에서 열리는지
> (`IsCustomChartPlayActive`가 살아 있는지) 실기 확인부터 해야 합니다.

## 패치 대상 해석 & 훅 상태 요약 (`HookHealth`)

모든 `[HarmonyPatch]` 클래스는 대상 메서드를 `Prepare()`에서 찾고 기능별 적용 수를 `HookHealth`에 보고합니다
([`PatchTargetSupport.PrepareTargets`](../STARGAZER%20custom%20chart/src/Hooks/Support/Common/PatchTargetSupport.cs),
[`HookHealth.cs`](../STARGAZER%20custom%20chart/src/Core/HookHealth.cs)).

```csharp
private static readonly List<MethodBase> Targets = new List<MethodBase>();
private static bool Prepare() => PrepareTargets(Targets, specs, "기능명");
private static IEnumerable<MethodBase> TargetMethods() => Targets;
```

- **단계 분리(`HookPhase`)**: 공용 디스패처(`InvocationPatches.cs`)는 `InvocationPrefixPatches`와 `InvocationPostfixPatches`로 나뉘어 각 대상에 실제로 필요한 쪽(Prefix 또는 Postfix) 하나만 붙입니다. 핸들러 없는 빈 반대쪽 트램펄린과 `object[]` 박싱 비용을 제거하기 위함입니다.
- **논리 이름 디스패치**: `PrepareTargets`가 실제 패치된 `MethodBase` → `PatchSpec.LogicalName`을 등록하고, `HookPrefix`/`HookPostfix`는 런타임 메서드명이 아니라 논리 이름으로 `HookKind`를 결정합니다. 따라서 대체 이름(`WithAlternates`, 예: `FetchJacektImage` → `FetchJacketImage`)이나 람다 대체 해석으로 연결된 메서드도 원래 핸들러로 정확히 라우팅됩니다.
- **안전한 건너뛰기**: 대상을 하나도 못 찾으면 `Prepare()`가 `false`를 돌려줘 **그 클래스만** 건너뜁니다. `TargetMethods()`가 빈 목록을 돌려주면 Harmony가 `"Undefined target method"` 예외를 던져 `PatchAll` 전체가 중단되므로 절대 빈 목록을 반환하면 안 됩니다.
- **상태 요약 및 업데이트 감지**: 모든 패치(지연 패치 포함) 시도가 끝난 뒤 `HookHealth.LogSummary()`가 `[Health]` 블록으로 전체 기능의 `(적용 수 / 기대 수)`를 한 번 출력하며, `GameAssembly.dll`·`global-metadata.dat`의 크기·수정시각(`savecustomkey/.game-fingerprint`, v2 형식 `v2|ga=…|meta=…`)이 이전 실행과 다르면 바뀐 파일 이름과 함께 게임 업데이트 경고와 확인 순서를 안내합니다. 비교 규칙은 순수 로직 `GameVersionLogic`(단위 테스트 대상)에 있으며, v1 지문(`크기:Ticks`)과 `meta=none`(파일 못 찾음)은 거짓 경고를 막기 위해 GameAssembly 부분만 비교합니다.
- **Steam 빌드 확인**: `steamapps/appmanifest_3446490.acf`의 `buildid`가 `GameVersionLogic.PinnedSteamBuildId`와 다르면, `TargetBuildID`가 대기 중이면, `AutoUpdateBehavior≠1`이고 읽기 전용도 아니면 각각 경고합니다(ACF가 없으면 Steam 밖 보존 사본으로 보고 조용히 넘어감).
- **핵심 기능 에스컬레이션**: `HookHealth.CriticalFeatures`(현재 `기록 보호(SaveGuard)`)가 실패/일부이거나, `OnLateInitializeMelon` 이후에도 한 번도 보고되지 않으면(PatchAll 중단 의심) `[Health][CRITICAL]` Error 로그를 별도로 남깁니다.
- **디스패처 표 자가 검사**: 시작 시 `ValidateInvocationHookTable`이 `InvocationPatchSpecs` ↔ `HookKindsByLogicalName` ↔ `PrefixHookKinds`(단계) ↔ `FocusedHarmonyInvocationMethods`의 어긋남을 `PatchResolutionLogic.FindDispatchTableMismatches`로 찾아 `[HookPatch][table]` Error로 알립니다(훅은 걸렸는데 핸들러가 `HookKind.None`으로 떨어지는 조용한 고장 방지).

## 기타 실측 메모

- `INNER_TrackData._metaData`는 필드가 아니라 **auto-property**입니다. 필드 조회가 먼저면 실패합니다.
- `SoundPlayer`의 실제 네임스페이스는 `Il2CppStarlike.Sound`입니다.
  `Il2CppStargazer.Starlike`라는 네임스페이스는 존재하지 않습니다.
- IL2CPP는 참조 타입 제네릭 메서드의 네이티브 구현을 공유할 수 있습니다.
  `Action<List<ITrackData>>.Invoke`에 건 패치가 `Action<AudioClip>.Invoke` 호출에도 걸려
  잘못된 로그가 찍힌 사례가 있습니다. 인자 타입을 확인하는 가드가 필요합니다.
