# BMS → STARGAZER 차트 변환 규칙

구현: [`src/Bms/BmsChart.cs`](../STARGAZER%20custom%20chart/src/Bms/BmsChart.cs) (순수 파서),
[`src/Probes/Notes/HookNoteProbes.BmsInject.cs`](../STARGAZER%20custom%20chart/src/Probes/Notes/HookNoteProbes.BmsInject.cs) (게임 주입).

파서는 게임/Harmony/Unity 의존성이 없어 단독으로 검증할 수 있습니다.

## 전체 흐름

```
BMS 텍스트
  └─ BmsChart.TryParse       마디별 노트 목록(분수 위치 + 노트 종류)
       └─ TryInjectBmsChart  Layer.Areas를 비우고 마디마다 Area 생성 → Note 추가
```

주입은 `StargazerPlayer+INNER_PatternLoader._Load_b__5_0(Pattern)` 시점에 일어납니다.
게임이 원본 패턴을 다 읽은 직후이며, 이때 원본 노트 하나를 **템플릿**으로 삼아
`Note` / `Area` / `BeatInfo` / `NoteProperty`의 실제 IL2CPP 타입을 얻습니다.
그래서 원본 패턴 로딩은 반드시 성공해야 합니다.

## 위치 매핑 — 분수 그대로

BMS는 마디 안 위치를 `i/N`으로 적습니다(`N`은 그 채널 데이터의 슬롯 개수).
게임의 `BeatInfo`도 `BeatIndex / BeatSplit` 분수라서, **초 단위 변환 없이 분수 대 분수로 직접 옮깁니다.**

```
beatIndex   = (슬롯 번호 × 마디 비트 수) / GCD
beatSplit   = (슬롯 개수)               / GCD
```

시간으로 환산했다가 되돌리지 않으므로 부동소수점 오차가 생기지 않습니다.

- `Area` 하나 = 마디 하나(4/4 기준 4비트). 2026-07-09 실측으로 확정([WORKLOG_03](WORKLOG_03_AREA_STRUCTURE_AND_BMS_INJECTION_2026-07-09.md)).
- `BeatInfo`는 **그 노트가 속한 Area 시작점 기준 상대 위치**입니다.
- `Area.Duration`, `KickIntervalTime`은 읽기 전용이라 건드리지 않습니다. 엔진이 `AreaBPM`+`length`로 계산합니다.

## 채널 ↔ 레인 매핑

| BMS 채널 | 방향 |
| --- | --- |
| 16 | 위 |
| 12 | 오른쪽 |
| 13 | 아래 |
| 11 | 왼쪽 |

16을 기준으로 시계 방향입니다. 코드에서는 `BmsChannelLaneOrder = { 16, 12, 13, 11 }`이며,
이 배열 순서가 `Layer.Lanes[0..3]`의 물리 배치(위/아래/왼쪽/오른쪽)에 대응합니다.

실제 `LaneUID` 문자열(`L0`, `L1`…)은 곡마다 다를 수 있어 **하드코딩하지 않고 런타임에 `Layer.Lanes`에서 순서대로 조회**합니다.

매핑에 없는 채널의 노트는 건너뛰고, 완료 로그에 `skippedByChannel=N(channels=...)`으로 개수를 남깁니다.

## 무시하는 채널

| 채널 | 의미 | 상태 |
| --- | --- | --- |
| 01 | BGM 자동재생 | 무시 (곡 음원은 `music.ogg`를 씀) |
| 02 | 마디 길이 변경 | **미지원** — 모든 마디를 4/4로 고정 |
| 03, 08 | BPM 변경 | **미지원** — `#BPM` 헤더 하나만 사용 |
| 09 | STOP | **미지원** |

곡 중간에 BPM이나 박자가 바뀌는 차트는 아직 정확히 변환되지 않습니다.

## 확장 사운드 ID (3자리)

BMS는 원래 사운드 ID를 36진수 2자리로 씁니다. 1296개를 넘기면 3자리를 쓰는 확장 포맷이 있습니다.

- **판별**: `#WAV` 헤더의 ID 부분이 3글자면 확장 포맷으로 간주합니다.
- 확장 포맷이면 노트 데이터도 2글자가 아니라 **3글자씩** 잘라 읽습니다.
- 잘라낸 값의 앞자리가 `0`이면 떼어내 기존 2자리 체계로 정규화합니다(`002` → `02`).

```
#WAV001 normal.wav      ->  id 01
#WAV002 hold 시작.wav   ->  id 02
#WAV003 hold 끝.wav     ->  id 03
#WAV016 music.ogg       ->  id 16

#00512:000002000000000003000000    3글자씩: 000 002 000 000 000 003 000 000
```

## 롱노트

**사운드 ID 번호가 아니라 `#WAV` 파일 이름으로 판별합니다.** 번호를 바꿔도 의미가 유지되게 하기 위해서입니다.

| 파일 이름 조건 | 노트 종류 | 게임 `NoteProperty.linked` |
| --- | --- | --- |
| `hold` + (`시작` 또는 `start`) | HoldStart | `StartPoint` |
| `hold` + (`끝` 또는 `end`) | HoldEnd | `EndPoint` |
| 그 외 | Normal | `None` |

대소문자는 구분하지 않습니다. 분류 결과는 주입 시 로그로 남습니다.

```
[BmsInject] 사운드 ID 분류: 01=Normal, 02=HoldStart, 03=HoldEnd, 16=Normal
[BmsInject] 완료: ... notesCreated=553(hold 3시작/3끝) ...
```

시작과 끝 개수가 맞지 않으면 경고가 나옵니다.

홀드가 마디를 넘어가면 시작과 끝이 서로 다른 `Area`에 들어갑니다.
게임 원본 차트도 인접 Area에 짝이 나뉘어 있는 것이 관측되어 같은 방식으로 두었으나,
**마디를 넘는 홀드는 아직 실제로 검증되지 않았습니다.**

## 노트 사운드에 대해

게임의 `Note`에는 "이 노트가 어떤 소리를 낸다"는 필드가 없습니다.
타격음은 게임의 SFX 시스템(`SoundPlayer.PlaySFX`)이 고정으로 처리합니다.

따라서 변환기는 **그 자리에 노트가 있는지 없는지**만 보면 되고,
BMS의 키음(`normal.wav` 등)은 게임에서 재생되지 않습니다.
사운드 ID는 롱노트 판별 용도로만 읽습니다.

## 검증 상태

`hwa2.bms`(73마디, 553노트) 기준으로 확인된 것:

- 확장 3자리 포맷 판별 — 데이터 249줄 전부 3의 배수 길이로 정확히 분할됨
- 채널 11/12/13/16 노트 변환 및 게임 반영 — `areasCreated=73 notesCreated=552`
- 매핑 없는 채널(21) 1개 정상 스킵

아직 검증되지 않은 것:

- 롱노트 실제 생성 (테스트 차트에 `02`/`03` 참조 노트가 아직 없음)
- 마디를 넘어가는 홀드
- BPM 변경 / 마디 길이 변경 (미지원)
