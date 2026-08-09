# 디컴파일 자료 — 게임 어셈블리 API 시그니처

저장소 루트의 `decompiled/` 폴더는 STARGAZER(Sixtar Gate: STARGAZER)의 IL2CPP 인터롭 어셈블리를
ILSpy로 디컴파일한 뒤, 메서드 본문을 걷어내고 **타입/멤버 시그니처만 남긴 읽기 전용 참조 자료**입니다.

빌드 대상이 아니며 **폴더 전체가 `.gitignore`에 등록되어 있습니다.** 저장소에는 이 문서만 들어가고,
`decompiled/` 안에는 아래 재생성 절차로 만든 결과물만 놓습니다.

## 폴더 구성

| 폴더 | 원본 DLL | 내용 |
| --- | --- | --- |
| `Il2CppMikazuki/` | `Il2CppMikazuki.dll` | 차트 자료구조 본체 — `Area`, `Note`, `Layer`, `Lane`, `BeatInfo`, `Pattern`, `MikazukiSerializer` |
| `Il2CppMikazuki.Player/` | `Il2CppMikazuki.Player.dll` | 차트 재생/판정 측 타입 |
| `Assembly-CSharp/` | `Assembly-CSharp.dll` | 게임 본체 (422개 타입) |
| `Assembly-CSharp-firstpass/` | `Assembly-CSharp-firstpass.dll` | 서드파티/플러그인 |
| `.raw/` | — | ILSpy 원본 출력. 본문이 필요할 때만 보면 되며, 아래 이유로 볼 일은 거의 없다 |

원본 위치: `H:\steam\steamapps\common\Sixtar Gate STARGAZER\MelonLoader\Il2CppAssemblies\`

## 왜 본문을 지웠나

이 DLL들은 MelonLoader의 Il2CppInterop이 생성한 **프록시(인터롭) 어셈블리**라,
메서드 본문이 전부 `IL2CPP.il2cpp_runtime_invoke`로 네이티브에 넘기는 동일한 스텁입니다.
게임 로직은 한 줄도 들어 있지 않으므로 읽을 가치가 없습니다(8.2MB → 0.7MB).

- **정확함**: 타입 이름, 상속 관계, 필드/프로퍼티/메서드 시그니처, 생성자 오버로드, enum 값, 기본 인자, 제네릭 제약, `[OriginalName]` 어트리뷰트
- **없음**: 실제 구현. "이 메서드가 내부에서 뭘 하는가"는 런타임 프로브(`src/Probes/`)나 네이티브 디스어셈블로 확인해야 함

시그니처만 보고 의미를 단정하면 안 됩니다. 필드 이름으로 역할을 추론했다가 틀린 사례가 있습니다
(`StargazerNote.posRef` — [REFERENCE_CUSTOM_TRACK_PIPELINE.md](REFERENCE_CUSTOM_TRACK_PIPELINE.md#속도-카오스--진행-방향은-직접-잰다)).

### 시그니처 추출 시 제거되는 것

노이즈라 전부 버립니다. 필요하면 `.raw/`에서 확인하세요.

- `NativeFieldInfoPtr_*` / `NativeMethodInfoPtr_*` 정적 필드
- 정적 생성자, `Type(IntPtr pointer)` 인터롭 생성자, `: base(...)` / `: this(...)` 초기화 호출
- 제네릭 메서드마다 생기는 `MethodInfoStoreGeneric_*` 중첩 클래스
- `[CallerCount]`, `[CachedScanResults]`, `[HideFromIl2Cpp]`, `[ObfuscatedName]` 어트리뷰트
- 모든 멤버에 붙는 `unsafe` 키워드

> `.raw/`에는 접근 제한자가 네이티브 메서드 포인터 이름에 남아 있습니다
> (`NativeMethodInfoPtr_Positioning_Protected_Void_Single_0` → `protected void Positioning(float)`).
> 리플렉션 `BindingFlags`를 정할 때 유용합니다.

## 재생성 방법

ILSpy CLI와 Python이 필요합니다.

```bash
dotnet tool install -g ilspycmd
```

```bash
powershell -ExecutionPolicy Bypass -File "scripts/decompile.ps1"
```

디컴파일(`scripts/decompile.ps1`) → 시그니처 추출(`scripts/strip_bodies.py`) 순으로 돌아갑니다.
`.raw/`만 있는 상태에서 추출만 다시 하려면:

```bash
python scripts/strip_bodies.py "decompiled/.raw/Il2CppMikazuki" "decompiled/Il2CppMikazuki"
```
