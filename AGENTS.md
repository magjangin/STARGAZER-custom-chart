# Sixtar Gate: STARGAZER Custom Chart — 에이전트 인수인계 및 프로젝트 가이드

> 본 문서는 멀티 에이전트 협업 및 유지보수를 위한 프로젝트 지침서입니다.
> 게임 영구 보존(Preservation), 리버스 엔지니어링 내역, 런타임 훅 아키텍처 및 실패했던 접근법(Dead Ends)을 기록합니다.

---

## 1. 대상 게임 버전 & 영구 보존 (Game Environment & Preservation)

- **게임 명칭**: 식스타 게이트: 스타게이저 (Sixtar Gate: STARGAZER)
- **설치 디렉터리**: `H:\steam\steamapps\common\Sixtar Gate STARGAZER`
- **Steam App ID**: `3446490`
- **Steam Build ID**: `21205351`
- **Steam Depot / Manifest**: Depot `3446491` / Manifest `9032179953170623906`
- **Unity 엔진 버전**: `2022.3.50f1` (x64)
- **Steam ACF 설정**: `H:\steam\steamapps\appmanifest_3446490.acf`
  - 자동 업데이트 방지를 위해 `AutoUpdateBehavior`를 `1`로 고정하거나 읽기 전용 속성 유지.
- **오프라인 영구 보존 (Goldberg Emulator)**:
  - 플러그인 위치: `Sixtar Gate STARGAZER_Data\Plugins\x86_64\` 및 게임 루트 디렉터리
  - 바이너리 사양: `SteamInternal_SteamAPI_Init` 심볼을 지원하는 골드버그 빌드 (`steam_api64.dll`, 7.3MB GSE 바이너리).
    - ⚠️ 일반 Goldberg 0.2.5 릴리즈(1.6MB)는 최신 Steamworks.NET의 `SteamInternal_SteamAPI_Init` 심볼이 없어 `EntryPointNotFoundException`이 발생하므로 반드시 해당 심볼이 포함된 빌드를 유지해야 함.
  - App ID 구성: 루트 및 플러그인 폴더 내 `steam_appid.txt` (`3446490`) 배치 완료

---

## 2. 게임 런타임 백엔드 & 디컴파일 분석 자산

- **런타임 백엔드**: **Unity Il2Cpp (x64)** (`GameAssembly.dll`)
- **모드 로더**: **MelonLoader** (Il2Cpp 지원 빌드)
  - 인터롭 어셈블리 생성 위치: `H:\steam\steamapps\common\Sixtar Gate STARGAZER\MelonLoader\Il2CppAssemblies\`
  - 런타임 인터롭 래퍼는 C# 표준 리플렉션과 unmanaged 포인터를 동시 사용
- **정적 분석 및 스켈레톤 추출 경로**:
  - `il2cpp_dump/`: Il2CppDumper 산출물 (`dump.cs`, `script.json`, `il2cpp.h`, `stringliteral.json`, `DummyDll/`)
  - `slices/`: `pe_slice.py` 및 `il2cpp_asm.py`를 통한 특정 클래스/메서드 조각 PE 및 어셈블리 디스어셈블리 (.dll, .asm)
  - `decompiled/`: 시그니처 덤프 및 복원 코드 보관
  - `scripts/dump_il2cpp.ps1`: 게임 바이너리 기반 Il2CppDumper 자동 실행
  - `scripts/disasm.ps1`: `il2cpp_asm.py` 기반 메서드/클래스 심볼 주석 어셈블리 덤프
  - `scripts/slice.ps1`: `pe_slice.py` 기반 REA/Ghidra 분석용 조각 PE 추출
  - `scripts/decompile.ps1`: `Cpp2IL` / `ilspycmd` 추출 파이프라인
  - `scripts/strip_bodies.py`: 메서드 바디를 제거하고 `.cs` 시그니처 스켈레톤만 정제 추출하는 툴
  - 주요 분석 어셈블리:
    - `Assembly-CSharp.dll` (게임 코어 루프 및 UI)
    - `Assembly-CSharp-firstpass.dll`
    - `Il2CppMikazuki.dll` (노트 엔진 및 트랙 모델)
    - `Il2CppMikazuki.Player.dll` (플레이어 및 판정 루틴)

---

## 3. 핵심 설계 의도 & Rationale (왜 이 우회 방식을 택했는가)

### 공식 커스텀 미지원 대응 (Starting Point 클론 주입)
- 게임 내에 외부 곡/채보를 추가하는 API, 에디터, 창작마당이 존재하지 않음.
- 따라서 이미 등록된 공식 기본 곡인 `"Starting Point"` 객체를 런타임에 `hwa/<곡폴더>` 수만큼 복제(Clone)하여 곡 목록(`TrackSelector`) 최상단에 강제 주입함.

### IntPtr 기반 객체 동일성 판정 (포인터 1:1 매핑)
- **표시명 기반 식별 불가**: 사용자가 `info.txt`에서 곡명을 임의로 지정하므로 고정 식별자로 사용 불가.
- **TrackID 기반 식별 불가**: 복제 원본의 ID인 `startingpoint`를 그대로 유지함. 만약 `StartsWith` 등으로 문자열 비교를 수행할 경우, 공식 수록곡인 `"Starting Point"` 및 `"Starting Point (yomoha Jazz Arrange)"`까지 커스텀으로 오인되어 공식 곡 플레이 시 커스텀 차트와 자켓이 주입되는 버그 발생.
- **해결책**: 주입 시점에 생성된 IL2CPP C++ 인스턴스의 네이티브 포인터(`IntPtr`)를 수집하여 `Dictionary<IntPtr, CustomAlbum> InjectedCustomTrackAlbums`에 보관하고, 포인터 일치 여부로만 커스텀 곡을 식별함.

### 기록 보호 (Save Guard)
- 커스텀 곡의 내부 `TrackID`가 공식 Starting Point와 같으므로, 결과 점수가 그대로 세이브 파일에 기록되면 공식 랭킹 및 로컬 기록이 오염됨.
- 따라서 커스텀 곡 플레이 종료 시 세이브 루틴(`SaveTrackRecord`)을 가로채 `TryGetMemberByType`으로 `PlayData`/`PlayTrack`을 추출해 저장을 원천 차단함.
- 게임 업데이트 등으로 `PlayData`/`PlayTrack` 추출이 실패하더라도 `IsCustomChartPlayActive && CurrentPlayAlbum is not null` 상태면 방어적으로 저장을 차단해 공식 기록 오염을 방지함.

### 람다 후킹 이중래퍼 & 업데이트 내성 아키텍처
- **1차 계층 (`PatchTargetSupport.ResolveTargetMethod`, 3단계 해석)**:
  1. 정확 이름(또는 `PatchSpec.AlternateMethodNames`, 예: `LevelSelector.FetchJacektImage` → `"FetchJacketImage"`) + 파라미터 타입명 점수(정확 일치 2점 / 포함 1점) 최고 후보.
  2. 람다 이름(`_Owner_b__N_M` / `<Owner>b__N_M`, 정규식 `LambdaNamePattern`)이면 `ResolveLambdaMethod`: 바깥 타입 + 컴파일러 생성 중첩 타입(`__c`, `__c__DisplayClassN_M`)만 탐색, **같은 소유 메서드(Load) + 파라미터 타입명 정확 일치(`Pattern`)** 후보가 유일할 때만 채택. 후보가 여럿이면 바깥 타입 직접 선언분이 유일할 때만 채택하고, 그래도 모호하면 걸지 않음(2차 계층이 받음).
  3. 이름은 같고 파라미터 개수만 맞는 후보가 **유일**할 때만 경고와 함께 채택.
  - 대체 해석이 일어나면 `[HookPatch][resolve]` 경고가 로그에 남는다 → 업데이트 후 로그에서 이 태그부터 확인할 것.
- **타입 해석 (`ResolveGameType`)**: FullName 정확 일치 → 실패 시 `외부타입+중첩타입` 짧은 이름이 게임 어셈블리에서 유일할 때만 채택(인터롭 재생성으로 네임스페이스가 바뀌는 경우 대비). 찾은 결과만 캐시.
- **논리 이름 디스패치**: `PrepareTargets`가 실제 패치된 `MethodBase` → `PatchSpec.LogicalName`(원래 `TypeName.MethodName`)을 등록하고, `HookPrefix/HookPostfix`는 런타임 FullName이 아니라 이 논리 이름으로 `HookKind`를 고른다. 그래서 대체 해석된 대상(이름 바뀐 람다/오타 수정 메서드 등)도 같은 핸들러에 연결된다.
- **타입 기반 멤버 폴백 (`ReflectionSupport.TryGetMemberByType`)**: 기대 이름(예: `currentTrack`, `beatInfo`, `property`, `notes`, `PlayData`, `PlayTrack`)을 먼저 조회하고 모두 없으면 기대 타입 짧은 이름의 **유일** 멤버(`k__BackingField`/`<*>` 후순위)로 자동 대체 해석해 `TypeFallbackAccessorCache`에 캐시하고 `[HookPatch][resolve]` 경고를 남김.
- **패치별 안전 해석**: `JudgeAllPerfectPatch.ResolveJudgeMethod`는 정확 이름 → 이름 조각+시그니처 일치 → 유일 시그니처 일치 순으로 해석하며, `TrackUnlockAllPatch`는 `AmbiguousMatchException`을 피하기 위해 이름·인자 수 기반 안전 조회를 사용.
- **2차 계층 (이중래퍼)**: `INNER_PatternLoader.Load` Prefix에서 인스턴스의 `onCompleted`(`Il2CppSystem.Action<Pattern>`, 이름 탐색 실패 시 `Action<Pattern>` 타입 폴백)를 래퍼로 교체. `로더 포인터 → 래퍼 델리게이트 포인터`를 기록해, 현재 onCompleted가 우리 래퍼면 재설치하지 않고 게임이 교체했으면 다시 감싼다. `PlayerBase.Play`마다 초기화.
- **중복 주입 가드**: 람다 본문이 `onCompleted(=래퍼)`를 먼저 부르고 그 뒤 람다 Postfix가 불리므로 같은 Pattern이 두 번 들어온다. `ProbeNoteArrayMembers`가 **Pattern의 IL2CPP 네이티브 포인터**(`LastProbedPatternPtr`)로 거른다(공식곡 포함 — 같은 패턴 이중 순회 방지).

### 성능 병목 제거 & 캐싱 아키텍처 (2026-11-14 감사 반영)
- **리플렉션/생성/콜백 캐시**: `ReflectionSupport.cs` (`MemberAccessorCache`, `TypeFallbackAccessorCache`, `MemberSetterCache`, `InstantiatorCache`, `CallbackInvokeCache`), `HookNoteProbes.Collection.cs` (`CollectionAccessorCache`), `HookNoteProbes.BmsInject.cs` (`Area`/`BeatInfo`/`Note`/`NoteProperty` 메타 캐시), `TrackSelectorCloningSupport.cs` (`TrackMetaCtorCache`, `CopyMemberCache`), `TrackSelectorCollectionSupport.cs` (`InsertMethodCache`, `CastStrategyCache`)로 고빈도 리플렉션 탐색 제거.
- **BMS/앨범 파싱 최적화**: `BmsChart.TryGetOrParse`가 파일 경로·`LastWriteTimeUtc` 기준으로 파싱 결과를 캐시하며 `ReadOnlySpan<char>` + 정수 채널 필터 + `00` 무음 청크 조기 스킵으로 할당 최소화. `CustomAlbum.TryLoad`는 `Directory.GetFiles` 결과를 파일명/확장자 사전으로 1회 인덱싱.
- **조기 탈출(Fast-Path Exit)**: `CustomBgmLoaderPatch`/`CustomJacketLoaderPatch`는 `TryGetAlbumForTrack`로 공식 트랙을 즉시 반환하고, `TrackSelectorHookSupport.HandleTrackSelectorSetTracks`는 앨범 유무 및 첫 항목 중복 주입을 전수 열거 전에 검사하며, `ProbeNoteArrayMembers`와 `TryWrapPatternLoaderOnCompleted`는 비커스텀 플레이 시 즉시 복귀함.
- **FocusedTrackViewer Postfix 전용 훅**: 빈 Prefix를 제거하고 `successCount > 0`일 때만 `_focusedTrackViewerPatched`를 설정하며, `ModEntry`의 `OnLateInitializeMelon`/`OnSceneWasLoaded`에서 지연 재시도를 지원.
- **죽은 프로브·로그 전용 코드 정리**: 미사용·로그 전용 16개 파일(`ExperimentChartSettings`, `AssetBundleLoader`, `SoundInvocation`, `TrackLoaderInvocation`, `TrackListViewerProbe`, `AreaCreate`/`Catalog`/`LinkHold`/`Wipe`/`LongNote`/`Reflection`, `JacketObjectProbe`, `LevelSelectorHookSupport`/`ProbingSupport`, `TrackSelectorMetadataSupport`) 및 빈 폴더 4개 제거.

### 다국어 (중국어 / 영어 / 한국어 / 일본어) 지원 & 인코딩 내성
- **SafeTextReader**: UTF-8 BOM(\uFEFF), UTF-16, ANSI(GB18030/GBK/CP949) 자동 감지 및 BOM 프리 헤더 파싱 (`#BPM`, `#WAV` 누락 방지).
- **info.txt & config.txt 중국어 키워드 지원**: `곡 제목` 외에 `曲名`/`歌名`/`标题`, `艺术家`/`作曲`/`曲师`, `宇宙`/`恒星`/`虚空`, `是`/`开`/`关` 등 완벽 파싱.
- **BMS 롱노트 키워드**: `시작`/`끝` 외에 `开始`/`结束`/`head`/`tail` 등 중국어/영어 키워드 확장 지원.
- **다국어 트랙 메타데이터 동기화**: `displayName` 외에 `displayNameEN` 및 `artistInfos` 딕셔너리(`KR`, `EN`, `JP`, `CN`, `TW`)를 동시 전파하여 영문/중문 클라이언트에서도 정상 표시.

---

## 4. 핵심 후킹 타깃 시그니처 & 커스텀 파이프라인

| 파이프라인 단계 | 타깃 클래스 / 메서드 | 후킹 유형 | 동작 및 구현 위치 |
| :--- | :--- | :--- | :--- |
| **곡 목록 주입** | `TrackSelector.Set(List<INNER_TrackData>)` | Prefix / Postfix | Starting Point 복제본 생성, `hwa/` 폴더 곡 주입, IntPtr 매핑 등록 (`TrackSelectorCollectionSupport.cs`) |
| **자켓 교체** | `INNER_TrackData.LoadJacketSprite(Action<Sprite>)` | Prefix | 커스텀 트랙 판정 시 `return false`로 원본 로딩 차단, 리플렉션 델리게이트 Invoke로 PNG 자켓 전달 |
| **BGM 음원** | `INNER_TrackData.LoadBGMClip(Action<AudioClip>)` | Prefix | `UnityWebRequestMultimedia.GetAudioClip` 비동기 디코딩 및 메모리 캐싱 후 오디오 클립 주입 |
| **프리뷰 음원** | `INNER_TrackData.LoadPreviewClip(Action<AudioClip>)` | Prefix | 프리뷰 전용 오디오 클립 주입 |
| **BMS 차트 주입** | `INNER_PatternLoader._Load_b__5_0(Pattern)`<br>`INNER_PatternLoader.onCompleted` (Dual-Wrapper) | Postfix / Delegate Wrapper | 패턴 로드 직후 `Pattern.Layer.Areas`를 BMS 채보로 런타임 스왑 (`HookNoteProbes.cs` → `HookNoteProbes.BmsInject.cs`). 1차 람다 훅 실패 시 2차 `onCompleted` 이중래퍼가 받으며, Pattern 네이티브 포인터 기준 중복 주입 가드 탑재 |
| **공용 디스패처** | `PatchTargetSupport.Get*InvocationPatchSpecs()` 전체 | Prefix / Postfix | `HookInvocationSupport.cs`의 `HookSite`(MethodBase별 1회 계산: `HookKind` + 로그 자격 + 스로틀)로 switch 디스패치. 핸들러 없는 로그 전용 훅은 두지 않는다 |
| **난이도 표시 (선택 화면)** | `FocusedTrackViewer.SetTrack/Refresh/FetchJacketImage/LoadPreviewBGM` | Postfix (수동 Patch) | `FocusedTrackViewerProbe.cs`. 화이트리스트 4개만 건다(전 메서드 후킹 금지 — Dead End 9) |
| **난이도 UI** | `FocusedTrackViewer`, `LevelSelector`, `CurrentTrackViewer`, `PlayInfoViewer` | Postfix | 게임 내부 문자열 변조 없이 각 화면의 텍스트 UI 컴포넌트 세터를 직접 호출하여 표시 오버라이드 |
| **올 퍼펙트 판정** | `JudgementExtension.FromTimeDiffernce`<br>`JudgementExtension.TryJudgeHead`<br>`TravelPlayer.INNER_Result.BufferProcess`<br>`JudgementWidget.Watch` | Prefix / Transpiler | 키 입력 타이밍에 상관없이 BLUESTAR 최고 판정 유도 및 점수/콤보 100% 반영 (`REFERENCE_ALL_PERFECT.md`) |
| **전곡 해금 (Unlock All)** | `TrackLoader.INNER_TrackData.get_IsUnlocked`<br>`TrackLoader.INNER_TrackData.get_LockType`<br>`TrackLoader.INNER_TrackData.get_UnlockCost`<br>`ITrackData.get_IsUnlocked`<br>`ITrackData.get_LockType`<br>`UserDataLoader.INNER_UnlockDataModule.IsUnlocked`<br>`UserDataLoader.INNER_CurrencyModule.GetVolts` | Prefix | 모든 트랙의 `IsUnlocked`를 `true`로, `LockType`을 `NONE`으로 고정하여 VOLT REQUIRED/DLC/어드벤처 잠금 및 자켓 자물쇠 UI를 제거하고 999,999 볼트 지급 (`TrackUnlockAllPatch.cs`) |

---

## 5. 시도했다가 실패한 것들 (Dead Ends / Failed Attempts)

새로운 기능을 추가하거나 리팩토링할 때 아래 방식들을 재시도하여 시간을 낭비하지 마십시오.

1. **`TrackID.StartsWith("startingpoint")` 식별 시도 (실패)**
   - 원인: 공식 `"Starting Point"` 및 `"Starting Point (yomoha Jazz Arrange)"`까지 커스텀으로 판정되어 공식 곡이 망가짐.
   - 규칙: 반드시 `IntPtr` 네이티브 포인터 비교를 통해서만 커스텀 곡을 식별할 것.
2. **`INNER_TrackMetaData.levelString` 메타데이터 값 변조 (실패)**
   - 원인: 2026-08-08 검증 결과, 해당 문자열은 단순 UI 표시용이 아니라 내부 패턴 에셋 로더의 키로 사용됨. 값을 변경하면 인게임 진입 시 무한 로딩 발생.
   - 규칙: 데이터 필드는 건드리지 말고, 화면별 Text 컴포넌트(`LevelItem`, `LevelUnit`, `TextProvider`)의 UI 세터만 Postfix에서 덮어쓸 것.
3. **`LevelSelector.Refresh` 후킹 (실패)**
   - 원인: 프레임 단위 또는 매우 잦은 주기로 호출되며, 패치 시 `LevelSelector::Refresh`에서 초당 수십 회 `NullReferenceException` 폭타 발생.
   - 규칙: 재적용이 필요한 경우 `FetchTrackRecord` 또는 `FetchJacektImage` Postfix를 활용할 것.
4. **`LevelSelector.SetTrack` 후킹 (실패)**
   - 원인: 시그니처 상 존재하지만 인게임 런타임에서 실제로 트리거되지 않음.
   - 규칙: `FocusedTrackViewer.currentTrack` 상태를 조회하여 현재 선택 트랙 판정을 재사용할 것.
5. **`SelectionItem<T>.selectedValue` 리플렉션 읽기 (실패)**
   - 원인: 제네릭 내부 포인터 정수가 반환되어 enum 파싱 불가능.
   - 규칙: `LevelItem`의 GameObject 이름(`Cosmic`, `Stellar`, `Void`)을 키로 매핑할 것.
6. **람다 퍼지 매칭을 `b__` 또는 `Load` 포함으로 넓히기 (위험, 2026-11-14 제거)**
   - 원인: 이름이 바뀌면 `INNER_PatternLoader.Load(TravelArgs)`(파라미터 1개)까지 후보에 들어오고, 시그니처 불일치 시 "개수만 맞는 첫 후보" 폴백으로 Load가 람다 자리에 이중 후킹될 수 있었음. 같은 이름 `_Load_b__5_0`이 `INNER_TableLoader.__c`에도 실재함.
   - 규칙: 람다 대체 해석은 소유 메서드 이름 + 파라미터 타입명 정확 일치 + 유일성 조건으로만. 모호하면 걸지 말고 2차 경로에 맡길 것.
7. **IL2CPP 제네릭 델리게이트 `Action<T>.Invoke` 패치 (병목, 2026-11-14 제거)**
   - 원인: 참조 타입 인스턴스화의 네이티브 구현이 공유되어, `Action<List<ITrackData>>.Invoke` 하나를 패치하면 게임 전체의 `Action<참조형>` 호출(오디오/스프라이트/패턴 콜백 등)에 Harmony 트램펄린이 붙음. 실제로 LoadPreviewClip 콜백에서 오발동이 관측됐고, 하는 일은 "트랙 N개 로드" 로그 1줄뿐이었음.
   - 규칙: 공유 제네릭 메서드(델리게이트 Invoke, `List<T>` 메서드 등)는 패치하지 말 것. 필요한 값은 그 값을 받는 구체 메서드(예: `TrackSelector.Set`)에서 얻을 것.
8. **IL2CPP 관리 래퍼 객체를 `ReferenceEquals`로 비교 (실패)**
   - 원인: Harmony 인자/델리게이트 인자로 넘어오는 래퍼는 호출마다 새 객체일 수 있어 같은 네이티브 객체여도 참조가 다름 → 1차 훅과 2차 래퍼의 중복 주입 가드가 무력화될 수 있었음.
   - 규칙: 동일성은 항상 `Il2CppObjectBase.Pointer`(`TryGetIl2CppPointer`)로 비교할 것.
9. **타입의 "모든 선언 메서드" 일괄 후킹 (위험)**
   - 원인: `FocusedTrackViewer`처럼 getter/setter만 빼고 전부 걸면, 업데이트로 `Update`/`LateUpdate`가 생기는 순간 매 프레임 리플렉션 열거가 돈다.
   - 규칙: 필요한 메서드 이름을 화이트리스트로 명시할 것.
10. **로그 전용 고빈도 훅 (병목)**
    - `SoundPlayer.PlaySFX`(4개 오버로드, 노트 타격음마다 호출)·`TrackSelectLogic.*`·`TrackSelector.OpenStandby/ChangeTrackCursor`는 핸들러 없이 로그만 남기던 훅이라 제거함. Il2Cpp Harmony 훅은 아무것도 안 해도 호출당 트램펄린 + `object[]` 박싱 비용이 있음.

---

## 6. 빌드 및 배포 워크플로

- **솔루션 / 프로젝트**: `STARGAZER custom chart.slnx` / `STARGAZER custom chart/STARGAZER custom chart.csproj`
- **타깃 프레임워크**: `.NET 6.0` (`net6.0`)
- **빌드 명령**:
  ```powershell
  # Visual Studio MSBuild 사용 시 (이 PC에는 VS "18" Community가 설치돼 있음 — 2022 경로는 없음)
  & "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "STARGAZER custom chart\STARGAZER custom chart.csproj" /p:Configuration=Debug

  # 또는 루트 빌드 스크립트 실행
  .\build.bat
  ```
- **배포 산출물 위치**:
  - `STARGAZER custom chart\bin\Debug\net6.0\STARGAZER custom chart.dll`
  - 배포 경로: `H:\steam\steamapps\common\Sixtar Gate STARGAZER\Mods\STARGAZER custom chart.dll`
- **로그 확인**:
  - `H:\steam\steamapps\common\Sixtar Gate STARGAZER\MelonLoader\Latest.log`
  - 게임 업데이트 후 첫 확인 순서: `[HookPatch][attr] target not found` → `[HookPatch][resolve]`(대체 해석 발생) → `[DualWrapper]` → `[FocusedTrackViewerProbe]`.
- **검증 이력**:
  - 2026-11-14 (리팩토링 전 실기): 탐색기/디스패처 리팩토링 후 실기 기동 확인 — 공용 훅 22개 전부 1단계(정확 일치)로 해석, `[resolve]` 경고 0건, FocusedTrackViewer 4/4, JudgePatch 4/4, UnlockAll 7/7, PlayBGM/StopBGM 핸들러 정상 디스패치.
  - 2026-11-14 (전체 감사/성능/내성 리팩토링): 16개 죽은 파일 제거·공용 캐싱·타입 기반 폴백·하드닝 리팩토링 후 MSBuild `Debug /t:Rebuild` 성공(오류 0개, C# 경고 0개, 기존 `.csproj` MSIL/AMD64 `MSB3270` 1건) 및 `Mods\STARGAZER custom chart.dll` 배포 확인. **리팩토링 후 인게임 실기 플레이(커스텀 곡 BMS 주입·Save Guard·난이도 UI)는 아직 미확인.**
