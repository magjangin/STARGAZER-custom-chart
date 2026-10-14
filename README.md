# 🌟 STARGAZER Custom Chart (식스타게이저 커스텀 차트 모드)

> **식스타 게이트: 스타게이저 (Sixtar Gate: STARGAZER)** 를 위한 MelonLoader 기반 런타임 후킹 및 커스텀 차트 지원 모드입니다.
>
> 이 게임은 커스텀 채보를 **공식적으로 지원하지 않습니다.** 에디터도, Steam 창작마당도 없습니다.
> 그래서 이 모드는 공식 트랙을 복제하고 런타임에 채보를 갈아끼우는 방식으로 동작합니다.

⚠️ **중요 안내**: 본 프로젝트는 현재 **활발하게 개발 및 실험 중(Work In Progress)**인 프로젝트입니다. 실구현 상태나 게임 패치에 따라 로직이 지속적으로 업데이트 및 변경될 수 있습니다.

본 프로젝트는 게임 플레이 엔진의 동작을 정밀 분석하고, 커스텀 트랙의 메타데이터와 UI 요소를 오버라이드하며, 안정적인 오토플레이 및 판정 분석 도구를 제공하기 위한 모딩 솔루션입니다.

---

## ✨ 주요 기능 (Key Features)

### 🎵 커스텀 곡 추가 (Custom Albums)
* 게임 폴더의 `hwa/` 아래 폴더 하나가 곡 하나가 됩니다. 공식 곡 "Starting Point"를 폴더 수만큼 복제해 곡 목록 맨 앞에 넣습니다.
* 제목·아티스트(`info.txt`), 자켓, 음원·프리뷰(`.ogg`)를 폴더 내용으로 바꿉니다. 난이도 숫자는 화면 표시만 바꿉니다.

### 🎼 BMS 차트 주입 (BMS Chart Injection)
* 커스텀 곡을 플레이하면 패턴이 로드된 직후 차트 전체를 폴더의 `.bms` 내용으로 교체합니다.
* BMS 마디 하나를 게임의 `Area` 하나로, 마디 안 위치를 `BeatInfo` 분수로 그대로 옮깁니다.

### 🎯 올 퍼펙트 판정 모드 (All-Perfect Mode)
* 키 입력 시 타이밍 오차 없이 무조건 BLUE(BLUESTAR) 최고 판정으로 유도하고 결과/점수/콤보 100% 반영 및 연출을 고정합니다.
* `JudgementExtension.FromTimeDiffernce`, `JudgementExtension.TryJudgeHead`, `TravelPlayer.INNER_Result.BufferProcess`, `JudgementWidget.Watch` 4중 런타임 패치 적용.
* `savecustomkey/config.txt`의 `AllPerfect` 옵션(`true`/`false`)으로 손쉽게 켜고 끌 수 있습니다.

### 💾 기록 보호 (Save Guard)
* 커스텀 곡은 Starting Point의 복제본이라 TrackID가 같습니다. 그대로 저장하면 공식 Starting Point 기록을 덮어쓰므로 커스텀 곡의 기록 저장을 막습니다.

### ⚙️ 설정 (`savecustomkey/config.txt`)
* 오토플레이(`autoplay`), 올 퍼펙트 모드(`AllPerfect`), 노트 연출(흔들림, 낙하 속도 카오스)을 켜고 끌 수 있습니다.
* 노트 연출은 그려지는 위치만 바꾸며 판정·점수에는 영향이 없습니다.

### 🔍 자켓 및 UI 탐색기 (Jacket & UI Probing)
* `CurrentTrackViewer`, `Result.PlayInfoViewer` 등의 인게임 UI 컴포넌트를 관측합니다.
* 트랜스폼 경로(Transform Path), 게임 오브젝트 이름 및 연결된 자켓 이미지를 스냅샷 형태로 분석하고 로깅합니다.

### ⏱️ 로그 스로틀링 (Log Throttling)
* 자주 불리는 후킹 지점의 로그가 쌓이지 않도록, 같은 메서드의 PRE/POST 로그를 합쳐 1초에 최대 3줄까지만 남깁니다
  (`HookRuntimeSupport.cs`의 `InvocationLogMaxPerWindow`).

---

## 📂 프로젝트 구조 (Directory Structure)

```text
STARGAZER custom chart/
├── STARGAZER custom chart.slnx          # Visual Studio 솔루션 파일
├── .gitignore                           # 빌드 산출물, VS 설정, decompiled/ 제외
├── README.md                            # 본 문서
├── build.bat                            # 모드 빌드 및 Mods 폴더 배포 스크립트
├── scripts/
│   ├── build_mod.bat                    # 모드 빌드 및 Mods 폴더 배포 스크립트
│   ├── decompile.ps1                    # 게임 어셈블리를 decompiled/로 디컴파일
│   └── strip_bodies.py                  # 디컴파일 결과에서 시그니처만 추출 (decompile.ps1이 호출)
├── docs/                                # 문서 (목록과 분류는 docs/README.md)
└── STARGAZER custom chart/
    ├── STARGAZER custom chart.csproj    # C# 프로젝트 파일 (빌드 후 게임 Mods 폴더로 자동 복사)
    ├── Properties/                      # 어셈블리 정보
    └── src/                             # 소스 코드
        ├── Core/                        # 모드 진입점 (MelonMod 상속)
        ├── Custom/                      # hwa 앨범 스캔, info.txt / config.txt 파서
        ├── Bms/                         # BMS 파서
        ├── Hooks/
        │   ├── Patches/                 # Harmony 패치 클래스
        │   └── Support/                 # 패치가 호출하는 처리 (트랙 주입, 자켓·음원, 난이도 표시, 노트 연출 등)
        ├── Probes/                      # 런타임 탐색 로그, BMS 차트 주입 (Probes/Notes)
        └── Diagnostics/                 # 타입 덤프 유틸
```

---

## 🛠️ 개발 및 빌드 환경 (Development Environment)

* **언어 및 프레임워크**: C# / .NET 6.0
* **모딩 툴**: MelonLoader, HarmonyLib
* **타겟 플랫폼**: PC Steam (Sixtar Gate: STARGAZER, IL2CPP)

### 💻 빌드 및 배포 방법
1. Visual Studio를 통해 `STARGAZER custom chart.slnx` 솔루션을 엽니다.
2. 빌드를 실행하거나, `scripts/build_mod.bat` 스크립트를 사용하여 최신 빌드된 DLL 파일을 게임 디렉토리 내 `Mods/` 폴더에 자동으로 복사해 배포할 수 있습니다.

빌드에는 게임 폴더의 `MelonLoader/Il2CppAssemblies/`(MelonLoader가 게임 실행 시 생성하는 인터롭 DLL)가 필요합니다.
이 폴더가 없으면 게임을 MelonLoader와 함께 한 번 실행한 뒤 빌드하세요.

---

## 📝 문서 (Docs)

문서 전체 목록과 분류 기준은 **[docs/README.md](docs/README.md)** 를 보세요.

자주 찾는 문서:
* [커스텀 곡 만들기 가이드](docs/GUIDE_CUSTOM_ALBUM.md) — `hwa` 폴더 구성, `info.txt`, `config.txt`
* [BMS 변환 규칙](docs/REFERENCE_BMS_CONVERSION.md) — 채널·레인 매핑, 분수 매핑, 롱노트
* [커스텀 트랙 파이프라인](docs/REFERENCE_CUSTOM_TRACK_PIPELINE.md) — 어느 훅이 무엇을 하는지
* [올 퍼펙트 판정 시스템](docs/REFERENCE_ALL_PERFECT.md) — 4중 후킹을 통한 무조건 최고 판정 유도 구조
* [최신 작업 일지 (2026-08-08)](docs/WORKLOG_04_CUSTOM_TRACK_PIPELINE_2026-08-08.md)

---

## 👥 개발자 및 기여자 (Credits)

| 이름 | 역할 |
| --- | --- |
| **화영왕** ([@magjangin](https://github.com/magjangin)) | 기획 · 개발 · 실기 검증 |
| **Claude** (Anthropic) | 코드 작성 및 게임 내부 분석 보조 |
| **Antigravity** (Google) | 코드 작성 및 게임 내부 분석 보조 |

게임 어셈블리 분석에는 [ILSpy](https://github.com/icsharpcode/ILSpy)를, 런타임 후킹에는
[MelonLoader](https://github.com/LavaGang/MelonLoader)와 [HarmonyLib](https://github.com/pardeike/Harmony)을 사용합니다.

이 모드는 게임 파일이나 음원을 일절 재배포하지 않습니다. 커스텀 곡의 차트·음원·자켓은 사용자가 직접 준비합니다.
