# Changelog

## 2026-11-20 (기록 보호 내성 강화 · 타입 개명 폴백 · 결과 화면 범위 감사)
- **기록 보호(SaveGuard) 강화**:
  - `SaveGuardLogic`: `BlockDefensiveTrackMismatch` 추가 — `PlayTrack`은 추출됐지만 포인터 매핑으로 커스텀 트랙이 식별되지 않는데 `PlayerBase.Play` 시점엔 커스텀 플레이였다면 저장을 차단(업데이트로 결과 트랙이 복사·래핑돼 포인터가 달라지는 경우 공식 Starting Point 기록 오염 방지). 이전엔 이 경우 저장을 허용했다.
  - `TrackRecordSaveGuardPatch`: `.WithTypeScanFallback()` 적용 — 저장 모듈 클래스 이름이 통째로 바뀌어도 게임 전체에서 `SaveTrackRecord(ITravelResultData)`(대체 이름 포함) 구체 메서드가 유일하면 연결.
- **업데이트 내성 (해석 4단계)**:
  - `PatchSpec.WithTypeScanFallback()` / `PatchTargetSupport.ResolveTargetMethodByTypeScan`: 옵트인 타입 개명 폴백. 파라미터 타입명 정확 일치 + 유일성 조건, 모호하면 후보 목록과 함께 경고만 남기고 걸지 않음.
  - `PatchTargetSupport.IsConcreteIl2CppClass`: Il2CppInterop이 IL2CPP 인터페이스를 일반 클래스로 생성하는 문제를 `Il2CppClassPointerStore<T>.NativeClassPtr` + `il2cpp_class_is_interface/_is_abstract`로 판정.
  - `GetGameTypeIndex`로 짧은 이름 색인 재구축 로직을 공용화.
- **실기 검증**: 대상 타입명을 일부러 틀린 임시 빌드에서 폴백이 `ITrackRecordModule`(인터페이스 클래스)을 거르고 `INNER_TrackRecordModule.SaveTrackRecord`에 유일 연결됨을 확인 → 원복 빌드에서 `[Health]` 전 기능 정상, 대체 해석 경고 없음.
- **테스트**: `FindUniqueMethodAcrossTypes` 3건, 트랙 불일치 차단 1건 추가(총 43건 통과).
- **문서**: `AGENTS.md`에 해석 4단계, 트랙 불일치 차단, SaveGuard 범위 밖(볼트 보상·통계·도전과제) 정리, Dead End 15(IL2CPP 인터페이스 판정)·16(폴백은 실기 변조 빌드로 검증) 추가. `docs/REFERENCE_CUSTOM_TRACK_PIPELINE.md`의 "확인 필요" 항목을 덤프 대조 결과로 갱신.

## 2026-11-19 (3차: 올 퍼펙트 회귀 복구 · 게임 버전 감지 정밀화 · 핵심 기능 에스컬레이션)
- **버그 수정 (실기 검증으로 발견)**:
  - `JudgeAllPerfectPatch`: 2차 리팩터에서 `JudgementExtension.FromTimeDiffernce`/`TryJudgeHead` 탐색을 `BindingFlags.Static` 전용으로 좁혀, 실제로는 **인스턴스 메서드**인 두 훅이 조용히 빠져 있었음(`[Health] 올 퍼펙트 판정: 일부 (2/4)`). `JudgementExtensionMethodFlags`(`Instance | Static`)로 복구 → 4/4 확인.
  - `HookHealth`: MelonLoader 0.7.3(net6)에서 `AppDomain.BaseDirectory`가 빈 문자열이라 메타데이터·ACF 경로 해석이 예외로 실패하던 문제를 `ResolveGameDirectory`(`MelonEnvironment.GameRootDirectory` 우선)로 수정.
- **업데이트 내성 강화**:
  - `GameVersionLogic`(신규, 순수 로직): 지문 v2(`GameAssembly.dll` + `global-metadata.dat`), v1/`meta=none` 하위 호환 비교, ACF 키 파싱, 고정 빌드(`21205351`) 판정, 자동 업데이트 차단 여부 판정.
  - `HookHealth.CheckSteamBuild`: Steam `buildid`가 고정 빌드와 다르거나 `TargetBuildID` 업데이트 대기 중이거나 `AutoUpdateBehavior≠1`(읽기 전용 아님)이면 경고. 바뀐 바이너리 파일 이름도 함께 표시.
  - `HookHealth.CriticalFeatures`: 기록 보호(SaveGuard)가 실패/일부이거나 시작 완료 후에도 보고되지 않으면(PatchAll 중단 의심) `[Health][CRITICAL]` Error로 별도 경고.
  - `ValidateInvocationHookTable` + `PatchResolutionLogic.FindDispatchTableMismatches`: `InvocationPatchSpecs` ↔ `HookKind` 매핑 ↔ Prefix/Postfix 단계 ↔ 포커스 로그 목록의 어긋남을 시작 시 `[HookPatch][table]` Error로 드러냄.
- **테스트**: `GameVersionLogicTests` 8건, 디스패처 표 검사 2건, 인스턴스 판정 메서드 해석 회귀 테스트 1건 추가(총 39건 통과).
- **문서**: `AGENTS.md`에 ACF 실측(`AutoUpdateBehavior=0`, 읽기 전용 아님 — 고정 미적용), MelonLoader `v0.7.3` 실측, Dead End 13/14, 실기 스모크 테스트 절차, 트러블슈팅 맵 추가. `README.md`·`docs/GUIDE_CUSTOM_ALBUM.md`·`docs/REFERENCE_CUSTOM_TRACK_PIPELINE.md`·`docs/REFERENCE_ALL_PERFECT.md` 동기화.

## 2026-11-19 (2차: 수정 기록·업데이트 내성 감사, 리팩토링 및 인수인계 강화)
- **업데이트 내성 감사 & 버그 예방**:
  - `TrackSelectorCloningSupport.GetCopyMemberSet`: `skipMetaDataMembers` 판정에 이름(`metadata`)뿐 아니라 `INNER_TrackMetaData` 타입 일치도 포함해, `_metaData` 이름이 변경돼도 복제 시 새 메타데이터가 원본으로 덮어씌워지는 회귀를 방지.
  - `CustomJacketSupport`: `CustomJacketFailedCache`(파일 수정 시각 기준)를 추가해 손상된 자켓 이미지로 인한 반복 디스크 읽기·텍스처 할당·경고 스팸을 차단(파일 교체 시 자동 재시도).
  - `HookInvocationSupport.TryEnablePlayerBaseAutoPlay`: `AutoPlay`/`autoPlay` 대체 멤버명 추가 및 미발견 시 `LogOnce` 경고 출력.
  - `TrackUnlockAllPatch`: `UnlockAll=false`일 때 1회 로그(`_disabledLogged`)를 남겨 `JudgeAllPerfectPatch`·`NoteMotionPatches`와 동작 일관화.
- **리팩토링 & 테스트 보강**:
  - `HookNoteProbes.BmsInject`: `GetOrBuildNoteMeta`/`GetOrBuildNotePropertyMeta`의 중복 유일 쓰기 멤버 탐색을 `TryFindUniqueWritableMember` 헬퍼로 통합, `ResolveLaneUidsInOrder`에 `id`/`Id` 폴백 이름 추가.
  - `PatchResolutionLogicTests`: 모호한 외부 람다(`null` 반환 → Dual-Wrapper 위임) 및 `PickBestBySignature`/`ScoreSignature` 정확·부분 일치 테스트 2건 추가(총 28건 통과).
- **인수인계 문서 강화**:
  - `AGENTS.md`: `InjectedCustomTrackAlbums` 시그니처 최신화 및 "빠른 트러블슈팅 인수인계 맵(증상 → 로그 태그 → 파일)" 추가.
  - `docs/TRACK_METADATA_REFERENCE.md`, `docs/CAST_AND_WRAPPER_CODE_REFERENCE.md`: `TrackRef` 튜플 타입 및 `ReflectionSupport.Instantiation.cs` 분할 위치 반영.

## 2026-11-19 (업데이트 내성 격리 · 위치 기반 바인딩 · 문서 정합성 강화)
- **업데이트 내성 & 패치 격리 강화**:
  - `JudgeAllPerfectPatch`: `Initialize()`의 대상 타입 해석을 컴파일 타임 `typeof`에서 `ResolveGameType`으로 전환하고 4개 패치를 개별 `try/catch`로 격리. `PatchResolutionLogic.ResolveNestedType`(유일성 조건) 및 `ResolveMethodByShortSignature`(짧은 타입명·`ref` 여부 검증) 적용. 4개 훅 핸들러를 이름 기반에서 Harmony 위치 기반 바인딩(`__0`, `__2`)으로 전환해 파라미터 이름 변경·메타데이터 스트립 내성 확보.
  - `TrackUnlockAllPatch`: `Initialize()` 대상 타입 및 `ELockType` 해석을 `ResolveGameType`으로 전환하고 7개 패치를 개별 `try/catch`로 격리.
  - `PatchTargetSupport`: 지연 로드 어셈블리가 추가되면 `_shortTypeNameIndex`를 자동 재구축하도록 `_shortTypeNameIndexAssemblyCount` 추적 추가.
  - `HookNoteProbes.BmsInject`: `Note.property` 및 `NoteProperty.linked` 이름 변경 시 타입/`None·StartPoint·EndPoint` enum 일치 유일 멤버 폴백 추가.
  - `FocusedTrackViewerProbe`: 타입 미발견 시에도 `HookHealth.Report(..., 0, 4, "타입 미발견")`을 기록해 `[Health]` 요약 누락 방지.
  - `NoteMotionPatches`: `StargazerNote`/`StargazerLongNote.Behaviour`에 대체 메서드명(`UpdateBehaviour`, `OnBehaviour`) 추가.
- **단위 테스트 보강**:
  - `PatchResolutionLogicTests`: `ResolveNestedType` 유일성/모호성, `ResolveMethodByShortSignature` 대체 이름·`ref` 구분·이름 조각 폴백 테스트 3건 추가(총 26건 통과).
- **문서 정합성 강화**:
  - `docs/README.md`, `docs/REFERENCE_BMS_CONVERSION.md`: `docs/archive/` 이관 후 깨졌던 워크로그 링크 일괄 수정, BMS 채널 02 지원 반영 및 제거된 중복 노트 억제 섹션 정리.
  - `docs/GUIDE_CUSTOM_ALBUM.md`, `docs/REFERENCE_ALL_PERFECT.md`: `AllPerfect` 기본값(`false`), `AllPerfectCustomOnly`, `BgmCacheMaxEntries`, 디버그 로그 키, 난이도별 `.bms`, 핫리로드 및 위치 기반 바인딩 반영.
  - `docs/REFERENCE_CUSTOM_TRACK_PIPELINE.md`, `README.md`, `AGENTS.md`, `BmsChart.cs`: 최신 구현(래퍼 참조 보관, `STARGAZER custom chart.Tests/`, 삭제된 `scripts/build_mod.bat` 제거, BMS 채널 02 주석) 동기화.

## 2026-11-18 (2차: BGM 캐시/메모리 누수 수정 · BMS 02 채널 지원 · 테스트 보강 · 파일 분할)
- **BGM / 자켓 캐시 및 메모리 관리**:
  - `CustomBgmSupport`: 핫리로드·LRU 퇴거 시 `UnityEngine.Object.Destroy(clip)` 및 `DownloadHandlerAudioClip.Dispose()` 즉시 호출.
  - `CustomBgmFailedCache`: 로드 실패 시 수정 시각 기준 실패 캐시를 기록해 커서 이동마다 재시도/에러 로그 반복 방지.
  - `BgmCacheMaxEntries`(기본 `24`, `0` = 무제한): `config.txt`에 LRU 캐시 상한 추가.
  - `LoadCustomAudioClipCoroutine` `finally`에서 미완료 `PendingCustomBgmCallbacks` 정리 보장 및 메인 스레드 전용 주석 명시.
  - `CustomJacketSupport`: 핫리로드 시 이전 `Sprite`/`Texture2D` 파괴.
- **BMS 마디 길이 변경(채널 02) 지원**:
  - `BmsChart`: `#mmm02:<비율>` 파싱(`LengthRatio`) 및 마디 내 노트 박자 비율 스케일링 적용.
  - `HookNoteProbes.BmsInject`: 비-4/4 마디에 대해 `Area.length`(`BeatInfo`) 비율 생성 및 적용.
- **테스트 보강 · 코드/문서 정리**:
  - `PatchResolutionLogic`·`SaveGuardLogic` 순수 로직 분리 및 xUnit 단위 테스트 9건 + BMS 02 채널 테스트 1건 추가(총 23건 통과).
  - `ReflectionSupport.Instantiation.cs` · `HookNoteProbes.BmsInject.Meta.cs` 분할로 대형 파일 정리.
  - `TrackRecordSaveGuardPatch` 공식 AllPerfect 차단 로그 `LogOnce` 적용, `decompiled/Directory.Build.props` 추가.

## 2026-11-18 (감사 개선 · 안전성 강화 · 난이도별 BMS · xUnit 테스트)
- **동작 · 안전**:
  - `AllPerfect` 기본값을 `false`로 변경하고 `AllPerfectCustomOnly`(기본 `true`) 추가. 커스텀 곡 전용이 꺼진 상태(`AllPerfectCustomOnly=false`)로 공식 곡을 플레이할 때는 `TrackRecordSaveGuardPatch`가 공식 기록 저장도 차단하도록 보강.
  - `HookNoteProbes.BmsInject`: 새 `Area` 목록을 임시 리스트에 모두 조립한 뒤 `builtAreas.Count > 0`일 때만 `Areas.Clear()` + 교체를 수행해, 실패 시 원본 차트가 보존되도록 수정.
  - `TrackSelectorCollectionSupport`: `InjectedCustomTrackAlbums`가 `IntPtr`뿐 아니라 관리 래퍼 참조(`TrackRef`)도 함께 보관해 네이티브 객체 수명 보장.
  - `BmsChart`: 사용되지 않던 `enableNoiseSuppression` 죽은 코드 제거(캐시 키 일관성 확보).
  - `ModEntry` / `CustomBgmSupport` / `TrackSelectorHookSupport`: 비-플레이 씬 로드 및 곡 선택 진입 시 `ResetPlaySessionState()`로 `CurrentPlayAlbum` 등 플레이 세션 정적 상태 초기화.
- **기능 개선**:
  - `CustomAlbum`: 난이도별 `.bms` 파일(`cosmic.bms`, `stellar.bms`, `void.bms`, `*_cosmic.bms` 등) 매칭 및 `GetBmsPath(levelKey)` 지원.
  - `BmsChart`: 미지원 채널(02 마디 길이, 03/08 BPM 변경, 09 STOP) 및 공백 없는 `#BPM140` 헤더 감지 시 경고 기록.
  - `CustomAlbum`: 파일 목록 정렬, 다중 `.bms`/`.txt`/이미지 경고, `readme.txt` 및 배경 이미지(`bg.png` 등) 폴백 제외.
  - `CustomBgmSupport` / `CustomJacketSupport`: 프리뷰 전용 음원만 선로딩하고 본 음원은 지연 로딩, 파일 수정 시각(`LastWriteTimeUtc`) 기반 핫리로드 지원.
- **코드 · 빌드 · 테스트 · 문서**:
  - `GameTypeEnumeratorMod` → `StargazerCustomChartMod`, Harmony ID → `com.hwayoungwang.stargazer.customchart`, `AssemblyInfo.cs` 작성자/게임 정보(`Starlike` / `Sixtar Gate STARGAZER`) 정리.
  - `CustomConfig` 기본값 단일화(`Entries` 기준) 및 훅 로그 플래그 4종(`VerboseInvocationLogging` 등)을 `config.txt`로 이동.
  - `ReflectionSupport` / `HookNoteProbes.BmsInject` / `TrackSelectorCloningSupport` 빈 `catch` 블록에 `LogOnce` 경고 추가.
  - `build.bat` 통합(`scripts/build_mod.bat` 제거, `pause` 제거, `dotnet build` 우선), `.csproj` 배포 복사 `ContinueOnError="WarnAndContinue"` 적용.
  - `STARGAZER custom chart.Tests` (xUnit, `net8.0`) 및 `.github/workflows/test.yml` 추가 (13개 단위 테스트 통과).
  - 작업 일지(`TODAY_WORK_*`, `WORKLOG_*`)를 `docs/archive/`로 이동, `AGENTS.md` 이력을 `CHANGELOG.md`로 분리.

## 2026-11-18 (업데이트 내성 전방위 보강 · 문서 동기화 · MSB3270 경고 제거)
- `UpdateCustomChartPlayState`/`LevelSelectorDisplaySupport`/`CloneTrackData` 타입 기반 폴백 통일, `TryGetMemberByType` 컬렉션 원소 폴백 추가, `CustomBgmLoaderPatch` 논리 이름 기반 프리뷰 분기, `FocusedTrackViewerProbe` 역할별 재시도 추적, `TrackUnlockAllPatch` 반환 타입 검증 및 enum getter 폴백.
- `.csproj` `$(GamePath)` 일원화 및 `MSB3270` 경고 제거, `README.md`·`docs/` 5종 문서 최신화.

## 2026-11-16 (HookHealth · 게임 업데이트 감지 · 철자 변형 대응)
- `HookHealth.cs` 추가: `[Health]` 훅 상태 요약 및 `GameAssembly.dll` 지문 비교를 통한 게임 업데이트 감지.

## 2026-11-15 (훅 단계 분리 · 설정 기반 무패치 · 서빙 경로 경량화 · 죽은 코드 정리)
- 공용 디스패처 `Prefix`/`Postfix` 분리(`InvocationPatches.cs`), `AllPerfect=false` 무패치, BGM/자켓 서빙 로그 1회 제한, 16개 미사용 파일 정리.
- 실기 기동 확인: 공용 훅 8개(Prefix 2 + Postfix 6), FocusedTrackViewer 4/4, JudgePatch 4/4, UnlockAll 7/7, 3개 앨범 `TrackSelector.Set` 주입 성공.

## 2026-11-14 (전체 감사 / 성능 / 내성 리팩토링)
- 리플렉션/생성/콜백 캐싱, BMS/앨범 파싱 최적화, 람다 3단계 해석 및 이중래퍼(`onCompleted`) 도입.
