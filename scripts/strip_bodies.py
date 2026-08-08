# ILSpy 디컴파일 결과에서 메서드 본문(IL2CPP 인터롭 스텁)을 걷어내고
# 타입/멤버 시그니처만 남긴다.
#
# 사용법: python scripts/strip_bodies.py <입력 루트> <출력 루트>

import re
import sys
from pathlib import Path

# 인터롭 어셈블리 전 타입에 기계적으로 붙는 노이즈. 시그니처 파악에 도움이 안 되므로 제거한다.
NOISE_ATTR = re.compile(r"^\[(CallerCount|CachedScanResults|ObfuscatedName|HideFromIl2Cpp)\b")
NOISE_FIELD = re.compile(r"\b(NativeFieldInfoPtr_|NativeMethodInfoPtr_|NativeClassPtr\b)")
# `where T : class` 같은 제약 조건과 구분하려면 키워드 뒤에 타입 이름이 와야 한다.
TYPE_DECL = re.compile(r"\b(class|struct|interface|enum|record)\s+[\w@]")
# 제네릭 메서드마다 생기는 인터롭 전용 중첩 클래스.
NOISE_TYPE = re.compile(r"\bMethodInfoStoreGeneric_")
ACCESSOR = re.compile(r"^(?:(private|protected|internal|public)\s+)?(get|set|init|add|remove)$")
STATIC_CTOR = re.compile(r"^static\s+[\w.]+\(\)$")
INTPTR_CTOR = re.compile(r"^public\s+[\w.]+\((?:System\.)?IntPtr\s+\w+\)$")


def scrub(line):
    """중괄호 개수를 셀 때 방해되는 문자열/문자 리터럴과 줄 주석을 지운다."""
    out = []
    i = 0
    n = len(line)
    quote = None
    while i < n:
        c = line[i]
        if quote is None:
            if c in '"\'':
                quote = c
            elif c == "/" and i + 1 < n and line[i + 1] == "/":
                break
            else:
                out.append(c)
        else:
            if c == "\\":
                i += 1
            elif c == quote:
                quote = None
        i += 1
    return "".join(out)


def find_block_end(lines, open_idx):
    """`{` 줄(open_idx)에 대응하는 `}` 줄 인덱스를 찾는다."""
    depth = 0
    for i in range(open_idx, len(lines)):
        s = scrub(lines[i])
        depth += s.count("{") - s.count("}")
        if depth == 0:
            return i
    return len(lines) - 1


def accessors_of(lines, start, end):
    """프로퍼티/이벤트 블록 안의 접근자를 `get; private set;` 형태로 뽑아낸다."""
    found = []
    depth = 0
    for i in range(start, end):
        s = lines[i].strip()
        if s in ("{", "}"):
            depth += 1 if s == "{" else -1
            continue
        if depth != 0:
            continue
        m = ACCESSOR.match(s)
        if m:
            mod = f"{m.group(1)} " if m.group(1) else ""
            found.append(f"{mod}{m.group(2)};")
    return found


def emit_block(lines, start, end, out, depth, in_enum):
    """start~end(중괄호 안쪽) 범위를 처리해 out에 시그니처만 쌓는다."""
    tab = "\t" * depth
    i = start
    pending = []  # 아직 출력하지 않은 어트리뷰트 줄
    while i < end:
        s = lines[i].strip()

        if not s or s.startswith("//"):
            i += 1
            continue

        # enum 본문은 멤버 나열뿐이라 그대로 옮긴다.
        if in_enum:
            out.append(tab + s)
            i += 1
            continue

        if s.startswith("["):
            if not NOISE_ATTR.match(s):
                pending.append(tab + s)
            i += 1
            continue

        # 선언부 수집: `{`(본문 시작)나 `;`(본문 없는 멤버)를 만날 때까지.
        decl_parts = []
        while i < end:
            cur = lines[i].strip()
            if cur == "{" or cur.endswith(";") or cur.endswith("{"):
                break
            decl_parts.append(cur)
            i += 1
        else:
            break

        cur = lines[i].strip()

        if cur.endswith(";") and not decl_parts:
            # 필드, delegate, 인터페이스 멤버 등 본문 없는 선언.
            if not NOISE_FIELD.search(cur):
                out.extend(pending)
                out.append(tab + normalize(cur))
            pending = []
            i += 1
            continue

        decl = normalize(" ".join(decl_parts))
        if cur.endswith("{") and cur != "{":
            decl = normalize(" ".join(decl_parts + [cur[:-1].strip()]))
            open_idx = i
        else:
            open_idx = i

        block_end = find_block_end(lines, open_idx)

        if TYPE_DECL.search(decl):
            if NOISE_TYPE.search(decl):
                pending = []
                i = block_end + 1
                continue
            out.extend(pending)
            out.append(tab + decl)
            out.append(tab + "{")
            emit_block(lines, open_idx + 1, block_end, out, depth + 1, "enum" in decl.split("{")[0].split())
            out.append(tab + "}")
            out.append("")
        elif "(" in decl:
            # 메서드/생성자/연산자. 모든 타입에 동일하게 존재하는 인터롭 생성자는 버린다.
            if not STATIC_CTOR.match(decl) and not INTPTR_CTOR.match(decl):
                out.extend(pending)
                out.append(tab + decl + ";")
        else:
            # 프로퍼티/이벤트.
            acc = accessors_of(lines, open_idx + 1, block_end)
            out.extend(pending)
            out.append(tab + decl + " { " + " ".join(acc) + " }")

        pending = []
        i = block_end + 1


def normalize(decl):
    """모든 인터롭 멤버에 붙는 `unsafe`, 생성자 초기화 호출, 중복 공백을 정리한다."""
    decl = re.sub(r"\bunsafe\s+", "", decl)
    decl = re.sub(r"\)\s*:\s*(?:this|base)\s*\(.*\)\s*$", ")", decl)
    return re.sub(r"\s+", " ", decl).strip()


def strip_file(path):
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    header = []
    body_start = 0
    for i, line in enumerate(lines):
        s = line.strip()
        if s.startswith("using ") or s.startswith("namespace "):
            header.append(s)
        elif s and not s.startswith("//"):
            body_start = i
            break

    out = []
    # 최상위 타입은 emit_block이 기대하는 형태가 아니므로 가상 블록으로 감싼다.
    emit_block(["{"] + lines[body_start:] + ["}"], 1, len(lines) - body_start + 1, out, 0, False)

    result = []
    for line in header:
        if line.startswith("namespace "):
            result.append("")
        result.append(line)
        if line.startswith("namespace "):
            result.append("")
    result.extend(out)

    text = "\n".join(result).rstrip() + "\n"
    return re.sub(r"\n{3,}", "\n\n", text)


def main():
    if len(sys.argv) != 3:
        print("사용법: python strip_bodies.py <입력 루트> <출력 루트>", file=sys.stderr)
        return 1

    src_root = Path(sys.argv[1])
    dst_root = Path(sys.argv[2])
    count = 0

    for src in src_root.rglob("*.cs"):
        rel = src.relative_to(src_root)
        if "Properties" in rel.parts:  # AssemblyInfo.cs 등은 볼 게 없다.
            continue
        dst = dst_root / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        dst.write_text(strip_file(src), encoding="utf-8")
        count += 1

    print(f"{count}개 파일 처리: {src_root} -> {dst_root}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
