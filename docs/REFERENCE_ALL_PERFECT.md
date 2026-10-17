# 올 퍼펙트 (All-Perfect) 판정 시스템 레퍼런스

식스타 게이트: 스타게이저(Sixtar Gate: STARGAZER, IL2CPP x64)의 판정 파이프라인 구조와 올 퍼펙트(All-Perfect) 조작 로직에 대해 정리합니다.

---

## 1. 개요 및 설계 원칙

리듬 게임에서 키 입력 타이밍을 최고 판정으로 유도할 때, 단순 UI 오버라이드만으로는 최종 결과창의 점수/정확도/랭크(BLUE STAR / 100.00%)에 반영되지 않습니다.
반대로 입력 판정만 바꾸고 결과 버퍼나 위젯 연출을 건드리지 않으면 불일치(FAST/SLOW 연출 발생, 점수 누락)가 일어납니다.

따라서 완벽한 올 퍼펙트 환경을 위해 **4중 연계 후킹(4-Stage Pipeline Patch)** 구조를 적용합니다:

```
[노트 입력 발생]
      │
      ▼
1. JudgementExtension.FromTimeDiffernce (Postfix)
      │ ─> 판정(EJudgements)을 BLUE(0)로 강제
      │ ─> judgeTiming 오차를 0.0f로 제거 (FAST/SLOW 억제)
      ▼
2. JudgementExtension.TryJudgeHead (Postfix)
      │ ─> 롱노트/일반노트 헤드 히트 판정 시 BLUE 고정
      ▼
3. TravelPlayer.INNER_Result.BufferProcess (Prefix)
      │ ─> TickEventData.judge = BLUE 강제 주입
      │ ─> 결과 통계, 누적 점수, 콤보에 100% 최고 판정 집계
      ▼
4. JudgementWidget.Watch (Prefix)
      │ ─> TickEventData.judge = BLUE 강제 주입
      │ ─> 인게임 판정 UI 및 블루 스타(BLUE) 이펙트 연출
```

---

## 2. 세부 후킹 지점 및 RVA 정보

| 번호 | 클래스 / 메서드 | RVA | 네이티브 VA | 패치 방식 | 역할 및 처리 내용 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **1** | `Stargazer.Play.Extensions.JudgementExtension.FromTimeDiffernce` | `0x437510` | `0x180437510` | **Postfix** | 입력 시간과 노트 시간차 비교 결과에서 `__result = EJudgements.BLUE`, `judgeTiming = 0.0f`로 덮어씀 |
| **2** | `Stargazer.Play.Extensions.JudgementExtension.TryJudgeHead` | `0x437CC0` | `0x180437CC0` | **Postfix** | 레인의 헤드 노트 판정이 성공(`__result == true`)했을 때 `result = EJudgements.BLUE` 고정 |
| **3** | `Stargazer.Play.TravelPlayer.INNER_Result.BufferProcess` | `0x41A070` | `0x18041A070` | **Prefix** | 판정 이벤트 버퍼 처리 시 `te.judge = EJudgements.BLUE`로 덮어써 최종 결과 점수/콤보 100% 반영 |
| **4** | `Stargazer.Play.Widgets.JudgementWidget.Watch` | `0x41CDD0` | `0x18041CDD0` | **Prefix** | 판정 위젯이 이벤트를 감시할 때 `t.judge = EJudgements.BLUE`로 설정하여 BLUE STAR UI 연출 |

---

## 3. 구현 세부 사항

구현 파일: [`JudgeAllPerfectPatch.cs`](../STARGAZER%20custom%20chart/src/Hooks/Patches/Play/JudgeAllPerfectPatch.cs)

### 3.1 동적 패치 초기화 (`Initialize`)
게임 내 `JudgementExtension`, `TravelPlayer+INNER_Result`, `JudgementWidget`은 정적 리플렉션을 통해 타입을 획득합니다.
`Initialize`는 아래 세 시점에서 불립니다:
- `ModEntry.OnInitializeMelon()`: 모드 로드 시 1차 초기화
- `ModEntry.OnLateInitializeMelon()`: 늦은 초기화 시점 보강
- `ModEntry.OnSceneWasLoaded()`: 플레이 씬 진입 시 확실한 바인딩 보장

4개가 모두 걸릴 때까지는 **빠진 패치만 다시 시도**하고, 이미 건 패치는 이름(`_patched` 집합)으로 기록해 다시 걸지 않습니다.
4개가 모두 걸린 뒤에는 `Initialize`가 바로 반환합니다. 일부만 걸렸다면 로그에
`[JudgePatch] 올 퍼펙트 패치가 N/4개만 적용됐습니다` 경고가 남고, 다음 호출에서 재시도합니다.
(이전에는 성공 개수와 무관하게 첫 호출 뒤 `_initialized`를 켜서, 일부가 실패해도 재시도하지 않았습니다.)

### 3.2 중첩 클래스(Nested Type) 처리
`TravelPlayer+INNER_Result`는 컴파일러가 생성한 비공개 내부 클래스이므로:
```csharp
var innerResultType = typeof(TravelPlayer).GetNestedType("INNER_Result", BindingFlags.NonPublic | BindingFlags.Public);
var bufferProcessMethod = AccessTools.Method(innerResultType, "BufferProcess", new[] { typeof(TickEventData) });
```
형태로 안전하게 리플렉션 탐색 후 Prefix를 연결합니다.

---

## 4. 모드 설정 연동 (`CustomConfig`)

- **설정 파일 경로**: `savecustomkey/config.txt`
- **설정 키**: `AllPerfect` (기본값: `true`)
- **지원 형식**: `true/false`, `1/0`, `on/off`, `yes/no`, `켜기/끄기`

모든 후크는 Prefix/Postfix 최상단에서 `CustomConfig.AllPerfect` 조건을 확인하므로, 옵션을 끄면(`AllPerfect=false`) 원본 게임의 판정 로직이 그대로 동작합니다.
