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
  - ⚠️ **2026-11-19·11-20 실측: 실제 파일은 `AutoUpdateBehavior=0`(항상 최신 유지), 읽기 전용 아님** — 위 고정이 적용돼 있지 않다. 모드가 기동 시 `[Health] Steam 자동 업데이트가 막혀 있지 않습니다` 경고로 알린다. 사용자가 직접 `1` + 읽기 전용으로 바꿀 것(에이전트는 Steam 파일을 임의로 고치지 않는다).
  - 런타임 확인: `HookHealth.CheckSteamBuild`가 `buildid`를 `GameVersionLogic.PinnedSteamBuildId`(`21205351`)와 비교하고 업데이트 대기 여부(`GameVersionLogic.IsUpdatePending`: `TargetBuildID`가 `0`/빈 값이 아니면서 다르거나 `StateFlags`&2)도 경고한다. 2026-11-23 실측 ACF: `StateFlags=4`, `buildid=TargetBuildID=21205351`. **고정 빌드를 바꾸면 이 상수와 본 문서를 함께 갱신할 것.**
- **MelonLoader 버전 (2026-11-19 실측)**: `v0.7.3 Open-Beta`, Runtime `net6`, 게임 버전 문자열 `1.0.7.001`.
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
- **복제 원본 선택은 별개 문제 (2026-11-22)**: `TrackSelector.Set`에서 복제할 공식 트랙은 `CloneSourceSelection.Pick`(순수 로직, xUnit 대상)이 ID **정확 일치 `startingpoint`를 우선**하고 없을 때만 접두 일치로 폴백한다(`StartsWith`만 쓰면 목록 순서에 따라 Jazz Arrange가 원본이 될 수 있었음). 커스텀 곡 *식별*에는 여전히 쓰지 않는다. 옛 `IsStartingPointTrack`은 2026-11-23 미사용으로 제거했고, TrackID 읽기는 `GetTrackIdText`만 남았다.
- **해결책**: 주입 시점에 생성된 IL2CPP C++ 인스턴스의 네이티브 포인터(`IntPtr`)와 관리 래퍼 참조(`TrackRef`, GC 수거 및 주소 재사용 방지)를 `Dictionary<IntPtr, (CustomAlbum Album, object TrackRef)> InjectedCustomTrackAlbums`에 보관하고, 포인터 일치 여부로만 커스텀 곡을 식별함.
- **복제 형식 폴백 (2026-11-23, v1.2.0)**: `INNER_TrackData`/`INNER_TrackMetaData`를 `ResolveGameType`(FullName → 짧은 이름 유일)으로 못 찾으면 복제 원본 객체(`source.GetType()`)와 그 메타데이터의 실제 형식을 쓴다. `IsConcreteIl2CppClass`로 인터페이스 래퍼(ITrackData)를 거른다(Dead End 15). 실기에서 이름을 틀린 빌드로는 아직 확인하지 않았다.
- **복제본 재사용 (2026-11-23)**: 앨범마다 복제는 **한 번만** 하고 `InjectedCloneByAlbum`(앨범 → (포인터, 래퍼))으로 계속 재사용한다. `TrackSelector.Set`은 트래블 모드에 들어올 때마다 **새 목록**으로 불리는데(실측: 재진입마다 호출), 예전엔 그때마다 등록표를 비우고 새로 복제해서 이전 복제본이 식별을 잃었다 — 그 객체를 붙잡은 결과 데이터가 `SaveTrackRecord`에 오면 공식 Starting Point로 판정돼 기록이 오염될 수 있었다. 지금은 목록에 이미 든 복제본(정렬된 목록 중간 포함)은 건너뛰고 빠진 것만 다시 넣는다(`CustomTrackInjectionLogic.FindAlbumsMissingFromList`, xUnit). 복제 원본 후보에서는 이미 주입한 복제본을 뺀다(같은 TrackID). 포인터를 못 얻어 등록에 실패한 복제본은 공식 곡으로 오인되므로 목록에 넣지 않는다. 실기: 첫 진입 `적용=3/3 (재사용 0)`, 재진입 `적용=3/3 (재사용 3) 현재 트랙 수: 90`(중복 없음), 재사용 복제본에 커스텀 자켓 서빙 확인.

### 기록 보호 (Save Guard)
- 커스텀 곡의 내부 `TrackID`가 공식 Starting Point와 같으므로, 결과 점수가 그대로 세이브 파일에 기록되면 공식 랭킹 및 로컬 기록이 오염됨.
- 따라서 커스텀 곡 플레이 종료 시 세이브 루틴(`SaveTrackRecord`)을 가로채 `TryGetMemberByType`으로 `PlayData`/`PlayTrack`을 추출해 저장을 원천 차단함.
- 게임 업데이트 등으로 `PlayData`/`PlayTrack` 추출이 실패하더라도 `IsCustomChartPlayActive && CurrentPlayAlbum is not null` 상태면 방어적으로 저장을 차단해 공식 기록 오염을 방지함.
- **트랙 불일치 차단 (2026-11-20)**: `PlayTrack`은 꺼냈지만 포인터 매핑으로 커스텀 트랙이 식별되지 않는데 `PlayerBase.Play` 시점엔 커스텀 플레이였다면 `SaveGuardDecision.BlockDefensiveTrackMismatch`로 막는다. 업데이트로 결과 데이터가 트랙을 복사·래핑해 포인터가 달라지는 경우 대비. 플래그는 `Play`마다 재계산되고 Play 씬 밖(`OnSceneWasLoaded`)·`TrackSelector.Set`에서 꺼지므로 오래된 값으로 공식 곡을 막지 않는다.
- **판정 표 (`SaveGuardLogic.Evaluate`, xUnit 대상)**: 결과 없음/트랙 추출 실패/트랙 불일치 → 커스텀 플레이 중일 때 차단, 커스텀 트랙 → 항상 차단, 공식 곡 → `AllPerfect=true && AllPerfectCustomOnly=false`이거나 `autoplay=true`일 때 차단. 결과 없음/트랙 추출 실패도 커스텀 플레이가 아니면 autoplay일 때만 차단(`BlockOfficialAutoPlay`).
- **오토플레이 공식 기록 차단 (2026-11-23, v1.2.0)**: `autoplay`는 `PlayerBase.Play`마다 곡 구분 없이 `IsAutoPlay`를 켜는데, `ITravelResultData`(PlayData/Item/Accuracy/Rank/MaximumCombo/Score)엔 오토플레이 여부가 없어 게임이 스스로 저장을 거르는지 정적 분석으로는 알 수 없다. 그래서 모드가 막는다(판정 예외 시에도 autoplay면 차단). 게임이 원래 거른다면 이중 차단일 뿐 해가 없다.
- **범위 밖(알려진 한계)**: `SaveTrackRecord` 하나만 막는다. 결과 화면 `ResultViewer.RewardVolts`(볼트 보상 → `INNER_CurrencyModule.AddVolts`), `UpdateStatistics`(통계), `CheckAchievement`(도전과제)는 커스텀 곡에서도 그대로 돈다. 볼트는 `UnlockAll`이 `GetVolts`를 999,999로 고정해 체감 영향이 없고, 도전과제는 골드버그라 오프라인에 머문다. 막으려면 `INNER_CurrencyModule.AddVolts`/`ResultViewer.UpdateStatistics` Prefix를 커스텀 플레이 판정과 묶어야 하는데, 결과 화면이 Play 씬 안인지 실기 확인이 먼저다(미검증).

### 람다 후킹 이중래퍼 & 업데이트 내성 아키텍처
- **1차 계층 (`PatchTargetSupport.ResolveTargetMethod`, 3단계 해석)**:
  1. 정확 이름(또는 `PatchSpec.AlternateMethodNames`, 예: `LevelSelector.FetchJacektImage` → `"FetchJacketImage"`) + 파라미터 타입명 점수(정확 일치 2점 / 포함 1점) 최고 후보.
  2. 람다 이름(`_Owner_b__N_M` / `<Owner>b__N_M`, 정규식 `LambdaNamePattern`)이면 `ResolveLambdaMethod`: 바깥 타입 + 컴파일러 생성 중첩 타입(`__c`, `__c__DisplayClassN_M`)만 탐색, **같은 소유 메서드(Load) + 파라미터 타입명 정확 일치(`Pattern`)** 후보가 유일할 때만 채택. 후보가 여럿이면 바깥 타입 직접 선언분이 유일할 때만 채택하고, 그래도 모호하면 걸지 않음(2차 계층이 받음). 같은 소유 후보가 **하나도 없을 때만**(소유 메서드 개명 의심) 다른 소유 람다 중 시그니처 정확 일치 유일 후보로 폴백한다(`PatchResolutionLogic.ResolveLambdaMethod`의 `anyOwner`) — 잘못 걸려도 BMS 주입은 패턴 포인터 중복 가드·커스텀 플레이 가드를 거치므로 공식 곡엔 영향이 없다.
  3. 이름은 같고 파라미터 개수만 맞는 후보가 **유일**할 때만 경고와 함께 채택.
  4. **(옵트인, 2026-11-20) 타입 개명 폴백 `PatchSpec.WithTypeScanFallback()`**: 위 단계가 모두 실패하면(대상 타입 이름이 통째로 바뀜) 게임 어셈블리 전체(`GetGameTypeIndex`)에서 이름(대체 이름 포함) + 파라미터 타입명 **정확** 일치 + IL2CPP 구체 클래스(`IsConcreteIl2CppClass`)인 메서드가 **유일**할 때만 채택(`PatchResolutionLogic.FindUniqueMethodAcrossTypes`). 현재 기록 보호(`SaveTrackRecord(ITravelResultData)`)에만 켬 — 흔한 시그니처에 켜면 엉뚱한 메서드에 걸릴 수 있으니 파라미터 타입이 고유한 핵심 기능에만 쓸 것.
     - Il2CppInterop은 IL2CPP **인터페이스를 일반 클래스로 생성**한다(`Type.IsInterface == false`). 그래서 `ITrackRecordModule.SaveTrackRecord`도 후보로 보이는데, `Il2CppClassPointerStore<T>.NativeClassPtr` → `IL2CPP.il2cpp_class_is_interface/_is_abstract`로 거른다(Dead End 15).
     - 실기 검증(2026-11-20): 대상 타입명을 일부러 `…INNER_TrackRecordModuleRENAMEDTEST`로 바꾼 빌드에서 `[HookPatch][resolve] … 게임 전체 시그니처 탐색으로 유일 후보 …INNER_TrackRecordModule.SaveTrackRecord(ITravelResultData result) 에 연결` + `기록 보호(SaveGuard): 정상 (1/1)` 확인 후 원복.
  - 대체 해석이 일어나면 `[HookPatch][resolve]` 경고가 로그에 남는다 → 업데이트 후 로그에서 이 태그부터 확인할 것.
- **타입 해석 (`ResolveGameType`)**: FullName 정확 일치 → 실패 시 `외부타입+중첩타입` 짧은 이름이 게임 어셈블리에서 유일할 때만 채택(인터롭 재생성으로 네임스페이스가 바뀌는 경우 대비). 찾은 결과만 캐시하며, 로드된 어셈블리 수가 늘어나면 `_shortTypeNameIndex`를 자동 재구축해 지연 로드 어셈블리도 놓치지 않는다.
- **논리 이름 디스패치**: `PrepareTargets`가 실제 패치된 `MethodBase` → `PatchSpec.LogicalName`(원래 `TypeName.MethodName`)을 등록하고, `HookPrefix/HookPostfix`는 런타임 FullName이 아니라 이 논리 이름으로 `HookKind`를 고른다. 그래서 대체 해석된 대상(이름 바뀐 람다/오타 수정 메서드 등)도 같은 핸들러에 연결된다.
- **타입 기반 멤버 폴백 (`ReflectionSupport.TryGetMemberByType`)**: 기대 이름(예: `currentTrack`, `beatInfo`, `property`, `notes`, `PlayData`, `PlayTrack`)을 먼저 조회하고 모두 없으면 기대 타입 짧은 이름의 **유일** 멤버(`k__BackingField`/`<*>` 후순위)로 자동 대체 해석해 `TypeFallbackAccessorCache`에 캐시하고 `[HookPatch][resolve]` 경고를 남김. 기대 타입이 `"List<T>"` 형태일 때 정확 일치 후보가 없으면 원소 타입 `T`가 같은 배열/컬렉션(`T[]`, `Il2CppReferenceArray<T>`, `IList<T>` 등) 유일 후보로 한 단계 더 폴백함.
- **패치별 안전 해석 & 격리 (2026-11-19 보강)**:
  - `JudgeAllPerfectPatch`와 `TrackUnlockAllPatch`는 `Initialize()`에서 컴파일 타임 `typeof` 대신 `ResolveGameType`으로 대상 타입을 해석하고 각 패치를 개별 `try/catch`로 격리해, 한 타입이 이동·삭제돼도 JIT 실패로 전체 패치가 죽지 않고 나머지 훅이 살아남는다.
  - `JudgeAllPerfectPatch`는 `PatchResolutionLogic.ResolveNestedType`(유일성 보장) + `ResolveMethodByShortSignature`(짧은 타입명·`ref` 여부 일치) 및 Harmony 위치 기반 바인딩(`__0`/`__2`)을 사용해 파라미터 이름 변경/메타데이터 스트립에도 내성을 가진다.
  - `HookNoteProbes.BmsInject`의 `GetOrBuildNoteMeta`/`GetOrBuildNotePropertyMeta`는 `Note.property`와 `NoteProperty.linked` 이름이 바뀔 경우 `NoteProperty` 타입·`None/StartPoint/EndPoint` enum 일치 유일 멤버로 폴백한다.
  - `FocusedTrackViewerProbe`는 타입 미발견 시에도 `HookHealth.Report(..., 0, 4, "타입 미발견")`을 기록해 `[Health]` 요약에서 누락되지 않게 한다.
- **UI 세터 & 플레이 상태 진입부 내성 강화 (2026-11-18)**:
  - `CustomBgmSupport.UpdateCustomChartPlayState`(`PlayerBase.Play` 진입부)가 `TravelArgs`에서 `PlayLevel`/`PlayTrack`을 추출할 때 `TryGetMemberByType`(`ELevels`/`ITrackData`)을 사용하여 프로퍼티 이름 변경 시에도 커스텀 곡 판정(`IsCustomChartPlayActive`)이 끊기지 않도록 통일.
  - `CustomBgmLoaderPatch.Prefix`가 런타임 메서드명 대신 `TryGetResolvedTargetLogicalName(__originalMethod)`를 우선 참조하여 `LoadPreviewClip`이 대체 이름(예: `LoadPreviewBGM`)으로 연결돼도 프리뷰 음원을 정확히 서빙.
  - `LevelSelectorDisplaySupport`의 `levels`/`item`/`levelItems`/`targetLevel` 조회를 모두 `TryGetMemberByType`으로 전환하고, `LevelUnit.Set`·`LevelItem.FetchLevelText`·`TextProvider.DoTextSetter`에 대체 세터 이름(`SetLevel`, `SetLevelText`, `SetText` 등) 해석을 추가.
  - `TrackSelectorCloningSupport.CloneTrackData`가 수동 4개 이름 조회 대신 `GetTrackMetaDataObject`(`INNER_TrackMetaData` 타입 폴백 포함)를 공유하고, `GetTrackIdText`도 트랙 본체에 ID 프로퍼티가 없으면 메타데이터의 `trackid`로 폴백.
  - `FocusedTrackViewerProbe`가 단일 불리언 대신 역할(`PatchedFocusedTrackViewerRoles`)·메서드(`PatchedFocusedTrackViewerMethods`) 집합으로 추적하여 일부만 패치됐을 때 중복 패치 없이 빠진 역할만 재시도하고 대체 이름 연결 시 `[HookPatch][resolve]` 경고를 남김.
- **2차 계층 (이중래퍼)**: `INNER_PatternLoader.Load` Prefix에서 인스턴스의 `onCompleted`(`Il2CppSystem.Action<Pattern>`, 이름 탐색 실패 시 `Action<Pattern>` 타입 폴백)를 래퍼로 교체. `로더 포인터 → 래퍼 델리게이트 포인터`를 기록해, 현재 onCompleted가 우리 래퍼면 재설치하지 않고 게임이 교체했으면 다시 감싼다. `PlayerBase.Play`마다 초기화.
- **중복 주입 가드**: 람다 본문이 `onCompleted(=래퍼)`를 먼저 부르고 그 뒤 람다 Postfix가 불리므로 같은 Pattern이 두 번 들어온다. `ProbeNoteArrayMembers`가 **Pattern의 IL2CPP 네이티브 포인터**(`LastProbedPatternPtr`)로 거른다(공식곡 포함 — 같은 패턴 이중 순회 방지).

### 성능 병목 제거 & 캐싱 아키텍처 (2026-11-14 감사 반영)
- **리플렉션/생성/콜백 캐시**: `ReflectionSupport.cs` (`MemberAccessorCache`, `TypeFallbackAccessorCache`, `MemberSetterCache`, `InstantiatorCache`, `CallbackInvokeCache`), `HookNoteProbes.Collection.cs` (`CollectionAccessorCache`), `HookNoteProbes.BmsInject.cs` (`Area`/`BeatInfo`/`Note`/`NoteProperty` 메타 캐시), `TrackSelectorCloningSupport.cs` (`TrackMetaCtorCache`, `CopyMemberCache`), `TrackSelectorCollectionSupport.cs` (`InsertMethodCache`, `CastStrategyCache`)로 고빈도 리플렉션 탐색 제거.
- **BMS/앨범 파싱 최적화**: `BmsChart.TryGetOrParse`가 파일 경로·`LastWriteTimeUtc` 기준으로 파싱 결과를 캐시하며 `ReadOnlySpan<char>` + 정수 채널 필터 + `00` 무음 청크 조기 스킵으로 할당 최소화. `CustomAlbum.TryLoad`는 `Directory.GetFiles` 결과를 파일명/확장자 사전으로 1회 인덱싱.
- **조기 탈출(Fast-Path Exit)**: `CustomBgmLoaderPatch`/`CustomJacketLoaderPatch`는 `TryGetAlbumForTrack`로 공식 트랙을 즉시 반환하고, `TrackSelectorHookSupport.HandleTrackSelectorSetTracks`는 앨범 유무 및 첫 항목 중복 주입을 전수 열거 전에 검사하며, `ProbeNoteArrayMembers`와 `TryWrapPatternLoaderOnCompleted`는 비커스텀 플레이 시 즉시 복귀함.
- **커서 이동 경로 로그 억제 (2026-11-23)**: `LevelSelectorDisplaySupport`의 `LevelUnit.Set`/`FetchLevelText`/`DoTextSetter`/`Text` 실패 경고는 타입별 1회(`LogOnce`)만 남긴다. 곡 선택 커서가 움직일 때마다 불리는 경로라 실패가 나면 로그 파일 쓰기가 폭주했다.
- **FocusedTrackViewer Postfix 전용 훅**: 빈 Prefix를 제거하고 `successCount > 0`일 때만 `_focusedTrackViewerPatched`를 설정하며, `ModEntry`의 `OnLateInitializeMelon`/`OnSceneWasLoaded`에서 지연 재시도를 지원.
- **훅 단계 분리 (2026-11-15)**: 공용 디스패처 대상은 `PatchSpec.Phase`(`HookPhase.Prefix`/`Postfix`)로 나뉘어 `Hooks/Patches/InvocationPatches.cs`의 `InvocationPrefixPatches`(PlayerBase.Play, PatternLoader.Load) / `InvocationPostfixPatches`(람다, CurrentTrackViewer.Listen, TrackSelector.Set, PlayInfoViewer.ShowPlayInfo, LevelSelector.FetchTrackRecord/FetchJacektImage)에만 걸린다. 예전 Play/TrackSelector/Travel 3개 클래스는 모든 대상에 Prefix+Postfix를 동시에 붙여 호출마다 빈 쪽 트램펄린·`object[]` 박싱이 한 번 더 돌았다. `PrepareTargets`는 다른 패치 클래스가 **다른 논리 이름**으로 이미 등록한 메서드도 거른다(람다 오해석으로 Load가 Prefix/Postfix 양쪽에 걸리는 것 방지).
- **노트 연출 프레임 값 캐시 (2026-11-23)**: `NoteMotion` Prefix/Postfix가 노트마다 읽던 `Time.frameCount`·`Time.time`(인터롭 게터 = IL2CPP 호출)을 `ModEntry.OnUpdate`에서 프레임당 1회 읽어 두고(`CacheNoteMotionFrame`) 정적 필드로 쓴다. 연출이 꺼져 있으면 `OnUpdate`도 즉시 반환. 값은 "프레임마다 달라지는 단조 증가 정수"로만 쓰이므로(같은 프레임 중복 호출·5프레임 공백 판정), Unity Update 실행 순서가 프레임마다 같으면 게임 Update보다 먼저든 나중이든 결과가 같다. **실기 미확인** — 노트 흔들림/속도 카오스를 켜고 1곡 확인할 것.
- **설정 기반 무(無)패치 (2026-11-15)**: `AllPerfect=false`면 `JudgeAllPerfectPatch`가 판정 핫패스 4개를 아예 걸지 않는다(`UnlockAll`/`NoteMotion`과 같은 방식). 설정은 재시작 시에만 바뀌므로 런타임 토글은 없다. `CustomConfig`는 1회 로드한 `Settings` 스냅샷을 null 검사 1회로 돌려준다.
- **서빙 경로 경량화 (2026-11-15)**: `CustomBgmLoaderPatch`는 캐시 적중 시 `File.Exists`를 보지 않고, BGM/자켓 "Serving" 로그는 경로별 1회만 남긴다(곡 선택 커서 이동마다 로그 파일 쓰기 제거). `JudgePatch`/`UnlockAllPatch`/`FocusedTrackViewerProbe`의 "못 찾음" 경고도 씬 로드마다 반복하지 않고 키별 1회.
- **BMS 주입 루프 할당 제거 (2026-11-15)**: `TrySetValueByNameCandidates` 후보 배열을 정적 필드로, `List.Add`/`Note(Area,string,BeatInfo)` 호출 인자 배열을 재사용 버퍼로 바꿨다(메인 스레드 전용, 호출 후 참조 해제). 2026-11-23: 후보가 여럿일 때 호출마다 `string.Join`으로 만들던 캐시 키도 `ConditionalWeakTable`로 목록 인스턴스당 1회로 줄였다(`ReflectionSupport.GetCandidatesKey` — 임시 배열을 넘겨도 누수 없음).
- **이름 기반 세터 정확 일치 우선 (2026-11-15)**: `ResolveMemberSetter`는 밑줄 제거·소문자 정규화 이름이 후보와 **완전히 같은** 프로퍼티/필드를 먼저 고르고, 없을 때만 예전의 "포함" 일치로 내려간다. 포함 일치만 쓰면 `displayname`이 선언 순서에 따라 `displayNameEN`에, `targetlayer`가 `<TargetLayer>k__BackingField`에 걸릴 수 있었다.
- **죽은 프로브·로그 전용 코드 정리**: 미사용·로그 전용 16개 파일(`ExperimentChartSettings`, `AssetBundleLoader`, `SoundInvocation`, `TrackLoaderInvocation`, `TrackListViewerProbe`, `AreaCreate`/`Catalog`/`LinkHold`/`Wipe`/`LongNote`/`Reflection`, `JacketObjectProbe`, `LevelSelectorHookSupport`/`ProbingSupport`, `TrackSelectorMetadataSupport`) 및 빈 폴더 4개 제거.

### 훅 상태 요약 & 게임 업데이트 감지 (2026-11-16, 2026-11-19 보강, `src/Core/HookHealth.cs`, `src/Core/GameVersionLogic.cs`)
- **목적**: 훅이 `PrepareTargets` / 수동 Patch(Judge·UnlockAll·FocusedTrackViewer) / 지연 재시도로 흩어져 걸리고 실패 로그도 각자 따로라, 업데이트 직후 "무엇이 살아 있나"를 로그 여러 곳에서 모아 봐야 했음.
- **동작**: 각 기능이 `HookHealth.Report(기능명, 적용 수, 기대 수)`를 남기고(`PrepareTargets`는 필수 `feature` 인자로 자동 기록), `ApplyDeferredPatches` 끝에서 `HookHealth.LogSummary()`가 `[Health]` 블록을 한 번 찍는다(요약이 같으면 재출력 없음). 일부/전부 실패면 Warning, 설정으로 끈 기능은 "꺼짐"으로 표시(실패로 세지 않음).
- **업데이트 감지**: `HookHealth.CheckGameFingerprint()`가 `GameAssembly.dll` + `global-metadata.dat`의 크기+수정시각을 `savecustomkey/.game-fingerprint`(v2: `v2|ga=…|meta=…`)에 저장하고, 다음 실행에서 달라지면 바뀐 파일 이름과 함께 `[Health] … 이(가) 지난 실행과 다릅니다` 경고 및 확인 순서를 안내. 동작은 바꾸지 않는 힌트 전용이며 파일이 없거나 읽기 실패해도 조용히 넘어간다.
  - 비교 규칙은 순수 로직 `GameVersionLogic`(xUnit 대상). v1 지문(`크기:Ticks`)이나 `meta=none`(파일 못 찾음)은 GameAssembly 부분만 비교해 형식 변경·경로 실패로 인한 거짓 경고를 막는다.
  - **게임 루트 경로**: MelonLoader 0.7.3(net6)에서 `AppDomain.CurrentDomain.BaseDirectory`가 **빈 문자열**이다(실측). 상대 경로는 작업 디렉터리 덕에 우연히 맞지만 `Directory.GetParent("")`는 예외. `HookHealth.ResolveGameDirectory`는 `MelonEnvironment.GameRootDirectory` → `BaseDirectory` → `Environment.CurrentDirectory` 순으로 절대 경로를 만든다. 새 코드에서 게임 폴더 기준 경로가 필요하면 이 순서를 따를 것.
- **Steam 빌드 감지**: `appmanifest_3446490.acf`의 `buildid`가 고정 빌드와 다르면, 업데이트가 대기 중이면(`IsUpdatePending` — `TargetBuildID=0`은 "대기 없음"으로 보고 거짓 경고를 내지 않음, `StateFlags`의 UpdateRequired 비트도 봄), 자동 업데이트가 막혀 있지 않으면(`AutoUpdateBehavior≠1` && 읽기 전용 아님) 각각 경고. ACF가 없으면(Steam 밖 보존 사본) 조용히 넘어간다.
- **핵심 기능 에스컬레이션 (`HookHealth.CriticalFeatures`)**: 실패 시 데이터가 망가지는 기능(현재 `기록 보호(SaveGuard)` = `HookHealth.SaveGuardFeature`)이 실패/일부거나, `OnLateInitializeMelon`(`MarkStartupComplete`) 이후에도 한 번도 보고되지 않으면(=PatchAll 중단 의심, Dead End 12) `[Health][CRITICAL]` **Error** 로그를 따로 남긴다. 새로 "실패하면 데이터 오염" 성격의 기능을 추가하면 이 표에 등록할 것.
- **디스패처 표 자가 검사 (`ValidateInvocationHookTable`)**: 시작 시 `InvocationPatchSpecs` ↔ `HookKindsByLogicalName` ↔ `PrefixHookKinds`(Prefix switch 소속) ↔ `FocusedHarmonyInvocationMethods`가 어긋나면 `[HookPatch][table]` Error. 공용 훅을 추가/이름 변경할 때 네 곳을 같이 고치지 않으면 "훅은 걸렸는데 `HookKind.None`이라 아무 일도 안 하는" 조용한 고장이 생기기 때문.
- **새 패치를 추가할 때 규칙**: `PrepareTargets(…, "기능명")`을 쓰거나, 수동 Patch면 적용 수를 `HookHealth.Report`로 남길 것. 그렇지 않으면 `[Health]`에서 그 기능이 빠진다.
- **FocusedTrackViewer 철자 변형**: 게임 원본에 `FetchJacektImage` 오타가 있어 `FetchJacketImage` ↔ `FetchJacektImage`를 같은 훅으로 취급(`FocusedTrackViewerAlternateNames`). 새 변형이 생기면 이 표에 추가.
- **UnlockAll 오버로드 모호성**: 같은 이름 오버로드 중 기대 인자 수와 맞는 것이 없으면 첫 후보로 대체하되 `[UnlockAllPatch] … 오버로드 … 대체` 경고를 1회 남긴다.

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
| **공용 디스패처** | `PatchTargetSupport.InvocationPatchSpecs` 전체 | Prefix **또는** Postfix (`PatchSpec.Phase`) | `InvocationPatches.cs`가 단계별로 한쪽에만 걸고, `HookInvocationSupport.cs`의 `HookSite`(MethodBase별 1회 계산: `HookKind` + 로그 자격 + 스로틀)로 switch 디스패치. 핸들러 없는 로그 전용 훅은 두지 않는다 |
| **난이도 표시 (선택 화면)** | `FocusedTrackViewer.SetTrack/Refresh/FetchJacketImage/LoadPreviewBGM` | Postfix (수동 Patch) | `FocusedTrackViewerProbe.cs`. 화이트리스트 4개만 건다(전 메서드 후킹 금지 — Dead End 9) |
| **난이도 UI** | `FocusedTrackViewer`, `LevelSelector`, `CurrentTrackViewer`, `PlayInfoViewer` | Postfix | 게임 내부 문자열 변조 없이 각 화면의 텍스트 UI 컴포넌트 세터를 직접 호출하여 표시 오버라이드 |
| **올 퍼펙트 판정** | `JudgementExtension.FromTimeDiffernce`<br>`JudgementExtension.TryJudgeHead`<br>`TravelPlayer.INNER_Result.BufferProcess`<br>`JudgementWidget.Watch` | Prefix / Postfix (수동 Patch) | 키 입력 타이밍에 상관없이 BLUESTAR 최고 판정 유도 및 점수/콤보 100% 반영 (`REFERENCE_ALL_PERFECT.md`). `AllPerfect=false`면 아예 패치하지 않음 |
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
4. **`LevelSelector.SetTrack` 후킹 (실패, 2026-11-15 훅·핸들러 제거)**
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
11. **공용 훅에 Prefix+Postfix 동시 부착 (낭비, 2026-11-15 제거)**
    - 원인: 한쪽 핸들러만 있는 대상에도 빈 반대쪽이 붙어 호출마다 디스패치·박싱이 2회 돌았음.
    - 규칙: 새 공용 훅은 `InvocationPatchSpecs`에 `.InPhase(HookPhase.Prefix|Postfix)`로 필요한 쪽만 등록하고, 해당 `HookKind` 분기를 `HookPrefix` 또는 `HookPostfix` 한쪽에만 둘 것.

12. **`TargetMethods()`가 빈 목록 반환 (치명적)**
    - 원인: Harmony가 어트리뷰트에서 대상을 찾다 "Undefined target method"로 던져 PatchAll이 중단, 이후 훅 전부 미적용.
    - 규칙: 항상 `Prepare()`에서 `PrepareTargets`로 해석하고 못 찾으면 `false`.
13. **`AccessTools.Method` → `BindingFlags.Static` 전용 탐색으로 교체 (회귀, 2026-11-19 복구)**
    - 원인: `AccessTools.Method`는 정적/인스턴스를 가리지 않는데, 위치 바인딩 리팩터에서 `ResolveMethodByShortSignature(..., Static)`로 바꾸며 인스턴스 메서드인 `JudgementExtension.FromTimeDiffernce`/`TryJudgeHead`가 조용히 빠짐(`[Health] 올 퍼펙트 판정: 일부 (2/4)`). 단위 테스트는 가짜 타입만 봐서 못 잡았고 실기 로그로 발견.
    - 규칙: 리플렉션 탐색을 바꿀 때는 `il2cpp_dump/dump.cs`에서 대상의 static 여부를 확인하고, 모르면 `Instance | Static`을 함께 넘길 것. 리팩터 후엔 반드시 게임을 한 번 띄워 `[Health]` 요약이 전부 `정상`인지 볼 것.
14. **`AppDomain.CurrentDomain.BaseDirectory`로 게임 폴더 계산 (MelonLoader 0.7에서 빈 문자열)**
    - 원인: 빈 문자열이라 `Path.Combine("", …)`은 작업 디렉터리 기준 상대 경로로 우연히 동작하지만 `Directory.GetParent`/`GetDirectories("")`는 `ArgumentException`.
    - 규칙: `HookHealth.ResolveGameDirectory` 순서(`MelonEnvironment.GameRootDirectory` 우선)를 쓸 것. (`CustomConfig`/`CustomAlbumRegistry`는 테스트 프로젝트와 공유돼 MelonLoader 의존을 넣을 수 없어 현행 유지 — 상대 경로라 실동작에는 문제없음.)
15. **IL2CPP 인터페이스를 `Type.IsInterface`/.NET 인터페이스 구현 목록으로 찾기 (불가)**
    - 원인: Il2CppInterop은 IL2CPP 인터페이스(`ITrackRecordModule` 등)를 `Il2CppObjectBase` 파생 **일반 클래스**로 만들고, 구현 클래스도 그걸 .NET 인터페이스로 구현하지 않는다. `GetInterfaces()`/`IsAssignableFrom`/`GetInterfaceMap`으로는 구현체를 찾을 수 없고, 반대로 인터페이스 클래스의 메서드가 일반 메서드처럼 보여 시그니처 탐색 후보에 섞인다.
    - 규칙: 인터페이스/추상 여부는 네이티브 클래스 플래그(`Il2CppClassPointerStore<T>.NativeClassPtr` + `IL2CPP.il2cpp_class_is_interface/_is_abstract`, `PatchTargetSupport.IsConcreteIl2CppClass`)로 판정할 것. "인터페이스의 모든 구현체를 건다"는 설계가 필요하면 `il2cpp_class_get_interfaces`를 써야 하며 아직 구현하지 않았다.
16. **폴백 경로를 단위 테스트만으로 검증 (부족)**
    - 원인: 폴백(대체 이름·타입 개명·멤버 타입 폴백)은 정상 빌드에선 한 번도 실행되지 않아, Il2CppInterop 특유의 표현(위 15번, Dead End 13의 static/instance) 때문에 실기에서만 깨지는 경우를 놓친다.
    - 규칙: 새 폴백을 넣으면 **대상 이름을 일부러 틀린 임시 빌드**로 게임을 한 번 띄워 `[HookPatch][resolve]`가 기대한 대상에 붙는지 보고, 정상 이름으로 되돌려 다시 빌드·스모크할 것(원복 시 PowerShell `Set-Content`가 붙인 UTF-8 BOM도 제거).

17. **모드 본체(`net6.0`)에서 .NET 7+ API 사용 (빌드 오류, 2026-11-22)**
    - 원인: 모드는 `net6.0`이라 `Stopwatch.GetElapsedTime` 등 .NET 7 이후 API가 없다(`CS0117`). 단위 테스트(`net8.0`)는 통과해도 모드 본체 빌드에서만 깨지는 경우가 있다.
    - 규칙: 시간 측정은 `Stopwatch.GetTimestamp()` 차이 × `1000.0 / Stopwatch.Frequency`로 계산하고, 순수 로직을 테스트와 공유하는 파일(`BmsChart`/`CustomAlbum` 등)에도 .NET 7+ 전용 API를 넣지 말 것. 변경 후엔 테스트와 **모드 본체 빌드를 둘 다** 돌릴 것.


18. **`TrackSelector.Set`마다 등록표를 비우고 재복제 (위험, 2026-11-23 제거)**
    - 원인: Set은 트래블 모드 재진입마다 새 목록으로 불린다. 매번 `InjectedCustomTrackAlbums.Clear()` 후 새로 복제하면 이전 복제본이 식별을 잃어, 그걸 들고 있는 화면/결과 데이터가 공식 Starting Point로 취급된다(기록 보호 우회 가능). 정렬된 목록처럼 복제본이 첫 항목이 아니면 "첫 항목 검사"도 빗나가 중복 삽입됐다.
    - 규칙: 복제본은 앨범마다 1회 생성·영구 재사용(`InjectedCloneByAlbum`). 등록표는 비우지 않는다(래퍼를 보관하므로 주소 재사용 위험 없음). 목록에 이미 있는 복제본은 포인터 집합으로 걸러 다시 넣지 않는다.
---

## 6. 빌드 및 배포 워크플로

- **솔루션 / 프로젝트**: `STARGAZER custom chart.slnx` / `STARGAZER custom chart/STARGAZER custom chart.csproj`
- **테스트 프로젝트**: `STARGAZER custom chart.Tests/STARGAZER custom chart.Tests.csproj` (`net8.0` xUnit, 게임 DLL 불필요)
- **타깃 프레임워크**: `.NET 6.0` (`net6.0`, 모드 본체) / `.NET 8.0` (`net8.0`, 단위 테스트)
- **빌드 및 테스트 명령**:
  ```powershell
  .\build.bat Release
  dotnet test "STARGAZER custom chart.Tests/STARGAZER custom chart.Tests.csproj" -c Release
  ```
- **모드 본체 net6 컴파일 검사 (게임 DLL 불필요, CI 연동)**: `dotnet run --project tools/bodycheck -c Release` (`--self-test`로 검사기 자체 확인, `--interop "<게임>\MelonLoader\Il2CppAssemblies"`로 로컬 전체 검사). 단위 테스트(net8)는 통과하는데 본체(net6)만 깨지는 Dead End 17 유형과 모드 내부 오타를 CI에서 잡는다. 게임 인터롭에 걸린 오류는 거르므로 게임 API 이름 검사는 못 한다(PC 빌드 몫).
- **릴리즈 (`tools/release.ps1`, 게임 설치 PC에서 실행 — 빌드에 게임 DLL이 필요해 GitHub Actions에서는 불가)**:
  ```powershell
  .\tools\release.ps1            # 버전 일치 확인 + Release 빌드(SkipDeploy) + 단위 테스트 + bodycheck(인터롭 포함) → dist\STARGAZER-custom-chart-v<버전>.zip, 노트 초안
  .\tools\release.ps1 -Publish   # 위 + main=origin/main·깨끗한 트리 확인 → 태그 v<버전> 푸시 → gh release create
  ```
  네이티브 명령 성공 여부는 종료 코드로만 본다(PS 5.1은 `Stop`일 때 git의 stderr 진행 출력도 예외로 던짐 — v1.2.0 때 태그는 푸시됐는데 실패로 보고된 원인). 태그 푸시 후 릴리즈 생성만 실패하면 스크립트가 출력하는 `gh release create …` 명령으로 마무리. 버전 기준은 csproj `<Version>`이며 `Properties/AssemblyInfo.cs` `MelonInfo`·`CHANGELOG.md` `## [<버전>]`이 다르면 중단한다. 이미 있는 태그면 중단하므로 먼저 세 곳의 버전을 올릴 것. zip에는 DLL 하나만 넣는다. 배포 복사를 건너뛰려면 `-p:SkipDeploy=true`.
- **배포 산출물 위치**:
  - `STARGAZER custom chart\bin\Release\net6.0\STARGAZER custom chart.dll`
  - 배포 경로: `H:\steam\steamapps\common\Sixtar Gate STARGAZER\Mods\STARGAZER custom chart.dll`
- **로그 확인**:
  - `H:\steam\steamapps\common\Sixtar Gate STARGAZER\MelonLoader\Latest.log`
  - 게임 업데이트 후 첫 확인 순서: `[Health][CRITICAL]`/`[HookPatch][table]`(Error) → `[HookPatch][attr] target not found` → `[HookPatch][resolve]`(대체 해석 발생) → `[DualWrapper]` → `[FocusedTrackViewerProbe]` → `[Health]`.
  - 실기 스모크 테스트(에이전트용): 게임 exe를 직접 실행 → `Latest.log`에 `Registered mono type MelonLoader.Support`가 찍힌 뒤 몇 초 기다려 `[Health]` 요약을 읽고 프로세스 종료. 골드버그라 Steam 클라이언트를 거치지 않는다.
- **게임 업데이트 대응 체크리스트** (순서대로):
  1. Steam 자동 업데이트가 걸렸다면 `[Health] GameAssembly.dll / global-metadata.dat 이(가) 지난 실행과 다릅니다` 및 `[Health] Steam BuildID … 가 고정 빌드 … 와 다릅니다` 경고가 뜬다. 고정 빌드(Build ID `21205351`)라면 `고정 빌드와 일치합니다`가 떠야 정상. `[Health][CRITICAL]`이나 `[HookPatch][table]` Error가 있으면 그것부터 고칠 것.
  2. 로그에서 `[HookPatch][attr] target not found` → `[HookPatch][resolve]`(대체 해석) → `[DualWrapper]` → `[FocusedTrackViewerProbe]` → `[Health]` 요약 순으로 본다.
  3. `[Health]` 가 "일부/실패"면 해당 기능의 `PatchSpec`(PatchTargetSupport.cs) 또는 수동 패치 대상 이름을 `dump_game_reference` 등으로 재확인.
  4. 인터롭 어셈블리(`MelonLoader\Il2CppAssemblies\`)가 재생성됐으면 빌드부터 다시 해서 컴파일 오류(타입/멤버 이동)를 확인. 수동 패치 3종(Judge·UnlockAll·FocusedTrackViewer)은 일부가 컴파일 타임 타입(`typeof`)을 참조하므로 타입 이동 시 여기서 먼저 깨진다.
  5. 커스텀 곡을 실제로 1곡 플레이해 `[BmsInject] 완료`, `[SaveGuard]`, `[CustomLevel][Result]` 확인.
- **알려진 한계 (BGM 첫 로드 실패, 2026-11-23 감사)**: 커스텀 음원 디코딩이 처음 실패하면 그 사이 대기열에 있던 콜백은 `null` AudioClip을 받는다(원본 로더는 이미 `return false`로 건너뜀). 이후 요청은 `CustomBgmFailedCache`로 원본(공식 Starting Point 음원)에 위임된다. 원본 재호출 폴백(우회 플래그 + `__originalMethod.Invoke`)은 실기 검증 수단이 없어 보류 — `[CustomBgm] 커스텀 BGM 로드에 실패했습니다` 로그가 보이면 음원 파일(ogg/mp3/wav)부터 확인할 것.
- **알려진 한계**: `JudgeAllPerfectPatch`/`TrackUnlockAllPatch`의 `Initialize()`는 `ResolveGameType`으로 런타임 해석·격리되지만, 개별 훅 핸들러 시그니처는 `EJudgements`/`TickEventData`/`ELockType`을 직접 참조한다. 인터롭에서 해당 enum/이벤트 타입이 이동·삭제되면 재빌드 시 컴파일 오류로 드러난다.
- **빠른 트러블슈팅 인수인계 맵 (증상 → 로그 태그 → 확인할 파일)**:
  - **커스텀 곡이 목록에 안 뜸** → `[Album]`, `[TrackSelector.Set]` → `src/Custom/CustomAlbumRegistry.cs`, `src/Hooks/Support/TrackSelector/TrackSelectorHookSupport.cs`, `TrackSelectorCloningSupport.cs`, `CustomTrackInjectionLogic.cs` (재진입 때 `재사용 N`이 앨범 수와 같고 `현재 트랙 수`가 늘지 않아야 정상)
  - **자켓/BGM/프리뷰가 원본으로 나옴** → `[CustomJacket]`, `[CustomBgm]` → `src/Hooks/Support/Visual/CustomJacketSupport.cs`, `src/Hooks/Support/Sound/CustomBgmSupport.cs`
  - **BMS 채보가 주입되지 않음** → `[HookPatch][resolve]`, `[DualWrapper]`, `[NoteProbe]`, `[BmsInject]` → `src/Hooks/Support/Common/HookInvocationSupport.cs`, `src/Probes/Notes/HookNoteProbes.cs`, `HookNoteProbes.BmsInject.cs`
  - **난이도 숫자가 원본으로 표시됨** → `[FocusedTrackViewerProbe]`, `[CustomLevel]` → `src/Probes/Track/FocusedTrackViewerProbe.cs`, `src/Hooks/Support/LevelSelector/LevelSelectorDisplaySupport.cs`
  - **공식 기록 오염/저장 차단 이상** → `[SaveGuard]`, `[Health][CRITICAL]`, `[HookPatch][resolve] … 시그니처 탐색` → `src/Hooks/Patches/UserData/TrackRecordSaveGuardPatch.cs`, `SaveGuardLogic.cs`, `PatchTargetSupport.ResolveTargetMethodByTypeScan` (`트랙 불일치` 경고가 반복되면 결과 데이터의 `PlayTrack`이 주입 객체와 다른 포인터인지부터 확인)
  - **올 퍼펙트 / 전곡 해금 / 오토플레이 미동작** → `[JudgePatch]`, `[UnlockAllPatch]`, `[PlayerBaseAutoPlay]`, `[Health]` → `JudgeAllPerfectPatch.cs`, `TrackUnlockAllPatch.cs`, `HookInvocationSupport.cs`, `HookHealth.cs` (올 퍼펙트 2/4면 Dead End 13 — static/instance 플래그부터)
  - **업데이트 경고가 거짓으로 뜸 / 안 뜸** → `[Health]` → `src/Core/GameVersionLogic.cs`(비교 규칙), `HookHealth.CheckBinaryFingerprint`/`CheckSteamBuild`, `savecustomkey/.game-fingerprint`
- **변경 이력**: [CHANGELOG.md](CHANGELOG.md) 참고 (과거 작업 일지는 `docs/archive/` 보관).
