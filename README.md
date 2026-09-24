# 🌟 STARGAZER Custom Chart (식스타게이저 커스텀 차트 모드)

> **식스타 게이트: 스타게이저 (Sixtar Gate: STARGAZER)** 를 위한 MelonLoader 기반 런타임 후킹 및 커스텀 차트 지원 모드입니다.
>
> 이 게임은 커스텀 채보를 **공식적으로 지원하지 않습니다.** 에디터도, Steam 창작마당도 없습니다.
> 그래서 이 모드는 공식 트랙을 복제하고 런타임에 채보를 갈아끼우는 방식으로 동작합니다.

⚠️ **중요 안내**: 본 프로젝트는 현재 **활발하게 개발 및 실험 중(Work In Progress)**인 프로젝트입니다. 실구현 상태나 게임 패치에 따라 로직이 지속적으로 업데이트 및 변경될 수 있습니다.

본 프로젝트는 `hwa` 폴더에 넣은 BMS 차트·음원·자켓을 게임 안의 독립된 곡으로 띄우고 플레이할 수 있게 하는 모딩 솔루션입니다.

---

## ✨ 주요 기능 (Key Features)

### 🎵 커스텀 곡 주입 (Custom Albums)
* `hwa` 아래 폴더 하나가 곡 하나입니다. 공식곡 "Starting Point"를 앨범 수만큼 복제해 곡 목록 맨 앞에 넣습니다.
* 복제본은 IL2CPP 네이티브 포인터(객체 동일성)로 식별하고 세션 동안 같은 객체를 재사용합니다. TrackID가 원본과 같아도 공식곡과 섞이지 않습니다.
* 곡 제목·아티스트는 복제본 메타데이터에, 난이도 숫자는 화면(UI)에만 덮어씁니다.

### 🎼 BMS 차트 변환 (BMS → Pattern)
* `PatternLoader` 콜백에서 `Layer.Areas`를 BMS 마디 단위로 통째로 교체합니다(분수 대 분수 매핑, 확장 3자리 ID, 롱노트 짝 검증).
* 채널 01에 놓인 곡 음원 위치만큼 노트를 당겨 싱크를 맞추고, UTF-8/CP949 BMS를 모두 읽습니다.
* 자세한 규칙은 [BMS 변환 규칙](docs/REFERENCE_BMS_CONVERSION.md)을 보세요.

### 🖼️ 자켓·음원 교체
* `LoadJacketSprite` / `LoadBGMClip` / `LoadPreviewClip`을 가로채 앨범 폴더의 이미지와 `.ogg`를 서빙합니다.
* 음원은 시작할 때 미리 로드하되 압축(Vorbis) 상태로 메모리에 둬서 앨범이 늘어도 메모리 부담이 작습니다.

### 🛡️ 공식 기록 보호 (Save Guard)
* 커스텀 곡 결과는 저장하지 않습니다(복제 원본인 Starting Point 기록 오염 방지).
* `savecustomkey/config.txt`의 오토플레이는 **커스텀 곡에만** 적용됩니다.

### ❄️ 노트 연출 (선택)
* 노트가 눈송이처럼 흔들리거나(NoteSway) 노트마다 낙하 속도가 달라지는(NoteSpeedChaos) 순수 시각 효과입니다. 판정에는 영향이 없습니다.

### ⏱️ 로그 스로틀링 (Log Throttling)
* 공용 훅 로그는 메서드별로 1초에 최대 3줄(PRE·POST 합산)로 제한합니다.
* 매 프레임·매 타격마다 불리는 지점(BGMPlayChecker, PlaySFX 등)에는 훅을 걸지 않습니다.

---

## 📂 프로젝트 구조 (Directory Structure)

```text
STARGAZER-custom-chart/
├── STARGAZER custom chart.slnx          # Visual Studio 솔루션 파일
├── build.bat                            # scripts/build_mod.bat 바로가기
├── README.md                            # 본 문서
├── scripts/
│   ├── build_mod.bat                    # 모드 빌드 및 Mods 폴더 자동 배포 스크립트
│   ├── decompile.ps1                    # 게임 인터롭 어셈블리 디컴파일(참조용)
│   └── strip_bodies.py                  # 디컴파일 결과에서 시그니처만 추출
├── docs/                                # 가이드·참조 문서·작업 일지 (docs/README.md 참고)
└── STARGAZER custom chart/
    ├── STARGAZER custom chart.csproj    # C# 프로젝트 파일
    ├── Properties/                      # 어셈블리 정보 (MelonInfo)
    └── src/
        ├── Core/                        # 모드 진입점 (MelonMod 상속)
        ├── Custom/                      # 앨범(hwa 폴더) 스캔, info.txt, config.txt
        ├── Bms/                         # BMS 파서(순수 로직)와 게임 패턴 주입기
        ├── Hooks/
        │   ├── Patches/                 # Harmony 패치 클래스(대상은 Prepare에서 해석)
        │   └── Support/                 # 훅이 부르는 기능 코드(트랙 복제, 자켓/BGM, 난이도 표시, 노트 연출)
        └── Probes/                      # 런타임 구조 탐색·진단 로그와 초기 노트 실험 코드
```

---

## 🛠️ 개발 및 빌드 환경 (Development Environment)

* **언어 및 프레임워크**: C# / .NET 6.0
* **모딩 툴**: MelonLoader, HarmonyLib
* **타겟 플랫폼**: PC Steam (Sixtar Gate: STARGAZER, IL2CPP)

### 💻 빌드 및 배포 방법
1. Visual Studio를 통해 `STARGAZER custom chart.slnx` 솔루션을 엽니다.
2. 빌드를 실행하면 `Mods/` 폴더로 자동 복사됩니다. 명령줄에서는 `build.bat`(= `scripts/build_mod.bat`)을 실행합니다.
3. 게임이 기본 경로(`H:\steam\steamapps\common\Sixtar Gate STARGAZER`)가 아니면 환경 변수 `GAME_PATH`(스크립트) 또는
   MSBuild 속성 `/p:GamePath=...`(Visual Studio/dotnet)로 경로를 넘깁니다. 참조 DLL 경로도 이 값을 따릅니다.

---

## 📝 문서 (Docs)

문서 전체 목록과 분류 기준은 **[docs/README.md](docs/README.md)** 를 보세요.

자주 찾는 문서:
* [커스텀 곡 만들기 가이드](docs/GUIDE_CUSTOM_ALBUM.md) — `hwa` 폴더 구성, `info.txt`, `config.txt`
* [BMS 변환 규칙](docs/REFERENCE_BMS_CONVERSION.md) — 채널·레인 매핑, 분수 매핑, 롱노트
* [커스텀 트랙 파이프라인](docs/REFERENCE_CUSTOM_TRACK_PIPELINE.md) — 어느 훅이 무엇을 하는지
* [최신 작업 일지 (2026-09-24)](docs/WORKLOG_05_REVIEW_FIXES_2026-09-24.md) — 리뷰 후 수정 내역과 게임에서 확인할 것

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
