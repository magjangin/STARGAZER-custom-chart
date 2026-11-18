# Changelog

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
