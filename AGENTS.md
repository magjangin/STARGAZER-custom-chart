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
- 따라서 커스텀 곡 플레이 종료 시 세이브 루틴(`SaveData`, `TravelResultData`)을 가로채 저장을 원천 차단함.

### 람다 후킹 이중래퍼 아키텍처 (Dual-Wrapper Fallback)
- 컴파일러가 생성하는 람다 메서드(`_Load_b__5_0`)는 Unity 엔진/컴파일러 버전 패치 시 `<Load>b__0`, `_Load_b__0_0` 등으로 심볼명이 변경될 위험이 매우 큼.
- **1차 계층**: `PatchTargetSupport`에서 `b__` / `Load` 패턴 퍼지 매칭으로 시그니처 자동 탐색 및 Harmony 훅 적용.
- **2차 계층 (이중래퍼)**: `INNER_PatternLoader.Load` 호출 시점에 인스턴스의 `onCompleted` 델리게이트(`Action<Pattern>`)를 인터셉트 래퍼로 교체.
- **동일 인스턴스 중복 주입 가드**: 1차 훅과 2차 래퍼가 모두 정상 동작할 때 이중 주입되지 않도록 `LastInjectedPattern` 참조 비교를 통해 멱등성(Idempotency) 보장.

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
| **BMS 차트 주입** | `INNER_PatternLoader._Load_b__5_0(Pattern)`<br>`INNER_PatternLoader.onCompleted` (Dual-Wrapper) | Postfix / Delegate Wrapper | 패턴 로드 직후 `Pattern.Layer.Areas`를 `BmsParser`가 변환한 BMS 채보로 런타임 스왑 (`NoteProbe.cs`). 1차 람다 훅 실패 시 2차 `onCompleted` 이중래퍼가 자동 인터셉트하며, 동일 인스턴스 중복 주입 방지 가드 탑재 |
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

---

## 6. 빌드 및 배포 워크플로

- **솔루션 / 프로젝트**: `STARGAZER custom chart.slnx` / `STARGAZER custom chart/STARGAZER custom chart.csproj`
- **타깃 프레임워크**: `.NET 6.0` (`net6.0`)
- **빌드 명령**:
  ```powershell
  # Visual Studio MSBuild 사용 시
  & "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" "STARGAZER custom chart\STARGAZER custom chart.csproj" /p:Configuration=Debug

  # 또는 루트 빌드 스크립트 실행
  .\build.bat
  ```
- **배포 산출물 위치**:
  - `STARGAZER custom chart\bin\Debug\net6.0\STARGAZER custom chart.dll`
  - 배포 경로: `H:\steam\steamapps\common\Sixtar Gate STARGAZER\Mods\STARGAZER custom chart.dll`
- **로그 확인**:
  - `H:\steam\steamapps\common\Sixtar Gate STARGAZER\MelonLoader\Latest.log`
