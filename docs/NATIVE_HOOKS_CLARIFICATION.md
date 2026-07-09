# 네이티브 훅(오해 방지) — 명확한 안내

요약: 이 저장소는 전통적인 네이티브 훅(P/Invoke, WinAPI, 인라인 패치, MinHook/Detours 등)을 포함하거나 사용하지 않습니다. 아래 내용은 레포를 검토한 결과를 바탕으로 작성된 설명입니다.

- 핵심 문장: "이 레포는 Harmony + Reflection 기반의 런타임 패치(IL2CPP 대상)를 사용하며, 네이티브(P/Invoke/WinAPI) 훅 구현은 없습니다."

왜 오해가 생기는가?
- 일부 코드에서 `Pointer`, `IntPtr`, `DescribePointer`, 혹은 `Pointer` 생성자 사용처럼 보이는 부분이 있습니다. 이것은 Unity IL2CPP의 "래퍼 객체"(managed wrapper)가 내부 네이티브 핸들(포인터)을 노출하는 패턴 때문이며, 직접적인 P/Invoke나 네이티브 메모리 패치를 의미하지 않습니다.
- 코드에 "injected" 또는 "inject" 같은 단어가 사용되는 경우가 있습니다. 이 단어는 컬렉션에 트랙 데이터를 "주입(insert)"하는 로직을 의미하며, 프로세스 인젝션/네이티브 훅과는 무관합니다.

레포에서 네이티브 훅으로 오해하기 쉬운 파일(예시)
- `src/Hooks/Support/TrackSelector/TrackSelectorReflectionSupport.cs` — `Pointer`/`IntPtr` 접근
- `src/Hooks/Support/TrackSelector/TrackSelectorCloningSupport.cs` — Il2Cpp 객체 복제 및 `Pointer` 비교
- `src/Hooks/Support/TrackSelector/TrackSelectorCollectionSupport.cs` — `IntPtr` 생성자 시도 및 컬렉션 주입
- `src/Hooks/Support/Sound/CustomBgmSupport.cs` — `AudioClip`의 포인터 기술 및 Unity 네이티브 핸들 접근
- `src/Hooks/Support/Sound/SoundHookSupport.cs` — `PlayBGM`/`PlaySFX` 오버로드 패치(매니지드 레벨)

간단한 기술적 정정
- "Pointer/IntPtr 사용" = 네이티브 훅 아님: IL2CPP 래퍼가 네이티브 핸들을 보관하거나 표시하기 위해 `IntPtr`을 제공할 수 있습니다. 이는 네이티브 메모리 패치나 P/Invoke 선언을 포함하지 않습니다.
- "Hook" 용어: 이 저장소에서의 "hook"은 Harmony를 통한 메서드 전/후 패치(매니지드 메서드의 PRE/POST 훅)를 의미합니다. 네이티브 바이트 패치(inline hook)와는 다른 범주입니다.

권장 주의사항(문서용 짧은 안내)
- 이 저장소를 검토하거나 확장할 때는 `DllImport`, `extern`, `SetWindowsHookEx`, `MinHook`, `Detours` 등의 존재 여부를 먼저 확인하세요. 현재 레포에는 해당 네이티브 훅 구현이 없습니다.
- 네이티브 훅이 정말로 필요할 경우에는 별도의 네이티브 모듈(명확한 ABI, 버전 관리, 테스트 행렬)을 설계하고, 사전에 팀 합의 및 사용자 고지(안티치트 영향 등)를 수행하세요.

문의가 있으면 이 문서에 코멘트로 남겨 주세요. 필요하면 이 문서를 기존 WORKLOG에 병합하거나 README에 요약본을 추가해 드리겠습니다.
