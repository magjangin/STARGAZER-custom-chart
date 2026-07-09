# STARGAZER Custom Chart - 문서 3
# Area 구조 확정 및 BMS 차트 주입 가능성 검증 (2026-07-09)

## 목적

- `Area`(마디 단위 컨테이너)의 실제 구조를 실측으로 확정한다.
- BMS로 작성한 차트를 이 게임 포맷으로 변환/주입할 수 있는지 검증한다.
- Note뿐 아니라 Area 자체도 런타임에 새로 생성해서 삽입할 수 있는지 확인한다.

## Area 구조 확정

- 런타임 경로: `Pattern -> Layers -> Areas -> notes` (기존 문서와 동일)
- `Il2CppMikazuki.Area` 멤버 카탈로그 (`AreaProbe`로 덤프)
  - `AreaBPM`(Double, rw) — 마디별 독립 BPM
  - `length`(BeatInfo, rw) — 마디의 비트 길이
  - `Duration`(Double, ro) — `AreaBPM`+`length`로 파생 계산된 실제 재생 시간(초)
  - `KickIntervalTime`(Double, ro), `notes`(List, rw), `TargetLayer`(Layer, rw) 등
- 실측 로그 (곡 "테스트 2", 32개 Area 샘플)
  - 전 구간 `bpm=160, len=192/48, dur=1.5` 로 동일
  - 검증: `len=192/48 = 4`(비트), BPM 160에서 1비트=0.375초 → `4 × 0.375 = 1.5초` = `dur`와 정확히 일치
- **결론: `Area` 하나 = 정확히 마디(measure) 하나(4/4 기준 4비트).** BMS의 마디 개념과 1:1 대응.
- 노트의 `BeatInfo.BeatIndex/BeatSplit`은 **그 노트가 속한 Area 시작점 기준 상대 비트 위치**.
  - 예: `Context 8`의 롱노트 `BeatIndex=2304, BeatSplit=768` → `2304/768=3` → 소속 Area(4비트 마디) 안에서 3비트 지점.
  - 노트마다 분모(BeatSplit)가 다른 건 필요한 세밀도(1/8, 1/16, 1/32박 등)에 맞춘 개별 해상도.

## BMS → STARGAZER 변환 규칙 (확정)

시간(초) 변환이나 BPM 역산 없이, 분수 대 분수로 정확히 매핑 가능:

1. BMS 마디(또는 BPM이 바뀌는 지점)마다 `Area` 하나 생성
2. 그 구간 BPM → `AreaBPM`
3. 그 마디의 비트 수(기본 4/4=4비트, `#xxx02` 마디길이 변경 시 배율 반영) → `Area.length`(BeatInfo)
4. 마디 내 오브젝트 위치 `i/N`(BMS 원본 그리드) → `beatsIntoMeasure = (i/N) × 마디비트수` → 노트 `BeatInfo.BeatIndex/BeatSplit`에 분수로 대입(필요시 GCD로 약분)
5. `Duration`/`KickIntervalTime`은 readonly라 직접 안 건드림 — 엔진이 `AreaBPM`+`length`로 자동 계산

## Area/Note 신규 생성 및 주입 검증

- **Note 생성** (기존 문서에서 이미 확정된 내용, 재확인)
  - `Note(Area, string, BeatInfo)` 생성자로 생성 → `Area.notes`(List, writable)에 Add → 실제 게임플레이에 반영됨(판정 가능).
- **Area 생성** (오늘 신규 검증)
  - `Il2CppMikazuki.Area`는 파라미터 없는 생성자가 없음. 생성자 2개만 존재:
    - `Area(Il2CppMikazuki.Layer _Owner)`
    - `Area(System.IntPtr pointer)`
  - 범용 인스턴스화 헬퍼(`InstantiateIl2CppObject`, ScriptableObject/빈 생성자/Activator 순으로 시도)는 전부 실패 — Area 전용으로 `Area(Layer)` 생성자를 직접 찾아 호출해야 함.
  - `Layer.Areas` 프로퍼티 자체도 쓰기 가능(`rw`)하고 실제 타입은 `Il2CppSystem.Collections.Generic.List<Area>`.
  - 검증 절차: 기존 Area를 템플릿 삼아 `Area(Layer)` 생성자로 새 Area 생성 → `AreaBPM`/`length`/`TargetLayer` 값 복사 → `List.Add()`로 `Layer.Areas`에 추가.
  - 결과 로그:
    ```
    [AreaCreateTest] Area(Layer) 생성자로 Area를 성공적으로 생성했습니다.
    [AreaCreateTest] areasWritable=True areasType=List<Area> instantiated=True
      bpmCopied=True lengthCopied=True layerCopied=True added=True
      countBefore=80 countAfter=81
    ```
  - **결론: Area 신규 생성 + Layer.Areas 삽입까지 성공.**
- 관련 코드
  - `src/Probes/Notes/HookNoteProbes.cs` — `AreaProbe`/`AreaCreateTest` 트리거 및 집계
  - `src/Probes/Notes/HookNoteProbes.AreaCreate.cs` — `Area(Layer)` 생성자 탐색·호출, 속성 복사, Areas 리스트 삽입
  - `src/Probes/Notes/ExperimentChartSettings.cs` — `EnableAreaCreationTest` 플래그(검증 후 다시 `false`로 비활성화)

## 최종 결론

- BMS 차트를 이 게임에 완전히 새로 주입하는 데 필요한 모든 조각이 실측으로 확인됨:
  - 마디(Area) 신규 생성 + BPM/길이 지정 + `Layer.Areas`에 추가 — 확인
  - 노트(Note) 신규 생성 + BeatInfo로 마디 내 위치 지정 + `Area.notes`에 추가 — 확인
- 더 이상 구조적으로 미확인된 항목 없음.
- 다음 단계: 실제 BMS 파서 출력을 이 (Area, Note) 생성 파이프라인에 연결하는 변환기 구현.
