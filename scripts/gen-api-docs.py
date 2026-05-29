#!/usr/bin/env python3
"""Generate Markdown API docs by scraping C# source.

Walks csharp-sdk/src/<Project>/**/*.cs, parses namespace declarations and
public types (class/struct/interface/record/enum) along with their public
members (methods/properties/fields/events) and the XML-doc <summary> blocks
that precede each. Emits one Markdown file per project plus an index.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
OUT = ROOT / "docs" / "api"

# A "type" keyword we surface
TYPE_KW = r"(?:class|struct|interface|record(?:\s+(?:struct|class))?|enum)"
TYPE_RE = re.compile(
    rf"^\s*(?P<mods>(?:public|internal|private|protected|sealed|abstract|static|partial|readonly|ref|unsafe|\s)+?)"
    rf"\s+(?P<kind>{TYPE_KW})\s+(?P<name>[A-Za-z_][A-Za-z_0-9]*)(?P<rest>[^{{;]*)",
)
NS_RE = re.compile(r"^\s*namespace\s+([A-Za-z_][\w\.]*)\s*[;{]")
# Member declaration starter: a line beginning with `public` modifiers.
MEMBER_START_RE = re.compile(
    r"^\s*(?P<mods>public(?:\s+(?:static|readonly|virtual|override|sealed|async|partial|unsafe|new|abstract|extern|const|required|init|ref))*)\s+(?P<rest>\S.*)$"
)


def extract_member_signature(lines: list[str], start: int) -> str | None:
    """Reassemble a multi-line declaration, balancing parens/angle-brackets.

    Stops at the first top-level `{`, `=>`, `;`, or `=` (default-value `=` is
    nested inside parens, so it won't be hit there).
    """
    buf: list[str] = []
    paren = angle = 0
    for i in range(start, min(start + 40, len(lines))):
        s = lines[i]
        for j, c in enumerate(s):
            if paren == 0 and angle == 0:
                if c in "{;":
                    buf.append(s[:j])
                    return " ".join(b.strip() for b in buf).strip()
                if c == "=":
                    nxt = s[j + 1] if j + 1 < len(s) else ""
                    if nxt != "=":
                        buf.append(s[:j])
                        return " ".join(b.strip() for b in buf).strip()
            if c == "(":
                paren += 1
            elif c == ")":
                paren -= 1
            elif c == "<":
                angle += 1
            elif c == ">" and angle > 0:
                angle -= 1
        buf.append(s)
    return None
XML_LINE = re.compile(r"^\s*///\s?(.*)$")
SUMMARY_RE = re.compile(r"<summary>(.*?)</summary>", re.DOTALL | re.IGNORECASE)


def strip_xml(text: str) -> str:
    """Reduce simple XML-doc inline tags to plain text/Markdown."""
    text = re.sub(r"<see(?:also)?\s+cref=\"[^:\"]*:?([^\"]+)\"\s*/>", r"`\1`", text)
    text = re.sub(r"<see(?:also)?\s+cref=\"[^:\"]*:?([^\"]+)\"\s*>.*?</see(?:also)?>", r"`\1`", text)
    text = re.sub(r"<paramref\s+name=\"([^\"]+)\"\s*/>", r"`\1`", text)
    text = re.sub(r"<typeparamref\s+name=\"([^\"]+)\"\s*/>", r"`\1`", text)
    text = re.sub(r"<c>(.*?)</c>", r"`\1`", text, flags=re.DOTALL)
    text = re.sub(r"<code>(.*?)</code>", r"`\1`", text, flags=re.DOTALL)
    text = re.sub(r"<para>\s*", "\n\n", text, flags=re.IGNORECASE)
    text = re.sub(r"</para>", "", text, flags=re.IGNORECASE)
    text = re.sub(r"<[^>]+>", "", text)  # drop any other tags
    # collapse whitespace within lines, keep blank lines
    lines = [re.sub(r"\s+", " ", ln).strip() for ln in text.splitlines()]
    return "\n".join(lines).strip()


def collect_xml_block(lines: list[str], end_idx: int) -> str:
    """Walk backwards from end_idx-1 grabbing contiguous '///' lines."""
    buf: list[str] = []
    i = end_idx - 1
    while i >= 0:
        m = XML_LINE.match(lines[i])
        if not m:
            # allow [Attribute] lines between xmldoc and declaration
            if lines[i].strip().startswith("["):
                i -= 1
                continue
            if lines[i].strip() == "":
                # blank lines break the doc comment block
                break
            break
        buf.append(m.group(1))
        i -= 1
    buf.reverse()
    return "\n".join(buf)


def extract_summary(xml_block: str) -> str:
    if not xml_block:
        return ""
    m = SUMMARY_RE.search(xml_block)
    if not m:
        return ""
    return strip_xml(m.group(1))


def parse_file(path: Path):
    """Return (namespace, [type dicts])."""
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    namespace = ""
    current_type = None
    types: list[dict] = []

    for idx, raw in enumerate(lines):
        if not namespace:
            mns = NS_RE.match(raw)
            if mns:
                namespace = mns.group(1)
                continue

        mt = TYPE_RE.match(raw)
        if mt and "public" in mt.group("mods"):
            kind = re.sub(r"\s+", " ", mt.group("kind").strip())
            name = mt.group("name")
            rest = mt.group("rest").strip().rstrip("{").strip()
            decl = f"{mt.group('mods').strip()} {kind} {name}{(' ' + rest) if rest else ''}".strip()
            current_type = {
                "kind": kind,
                "name": name,
                "decl": decl,
                "summary": extract_summary(collect_xml_block(lines, idx)),
                "members": [],
                "file": str(path.relative_to(ROOT)),
            }
            types.append(current_type)
            continue

        if current_type is None:
            continue

        mm = MEMBER_START_RE.match(raw)
        if mm:
            rest = mm.group("rest")
            # Skip nested type declarations (rendered elsewhere).
            if re.match(rf"^({TYPE_KW})\b", rest):
                continue
            full_sig = extract_member_signature(lines, idx)
            if not full_sig:
                continue
            mods_part = mm.group("mods").strip()
            sig_clean = full_sig[len(mods_part):].lstrip() if full_sig.startswith(mods_part) else full_sig
            sig_clean = re.sub(r"\s+", " ", sig_clean).strip()
            if not sig_clean:
                continue
            current_type["members"].append({
                "sig": sig_clean,
                "summary": extract_summary(collect_xml_block(lines, idx)),
            })

    return namespace, types


def render_project(project: str, items: list[tuple[str, list[dict]]]) -> str:
    out: list[str] = [f"# {project}", ""]
    out.append(f"Auto-generated from `src/{project}/**/*.cs`. Do not edit by hand.")
    out.append("")
    # Group types by namespace
    by_ns: dict[str, list[dict]] = defaultdict(list)
    for ns, types in items:
        for t in types:
            by_ns[ns or "(global)"].append(t)
    for ns in sorted(by_ns):
        out.append(f"## Namespace `{ns}`")
        out.append("")
        for t in sorted(by_ns[ns], key=lambda x: x["name"]):
            out.append(f"### {t['kind']} `{t['name']}`")
            out.append("")
            out.append(f"```csharp\n{t['decl']}\n```")
            out.append("")
            if t["summary"]:
                out.append(t["summary"])
                out.append("")
            out.append(f"<sub>Defined in `{t['file']}`</sub>")
            out.append("")
            if t["members"]:
                out.append("#### Members")
                out.append("")
                for m in t["members"]:
                    out.append(f"- `{m['sig']}`" + (f" — {m['summary']}" if m["summary"] else ""))
                out.append("")
    return "\n".join(out).rstrip() + "\n"


def main() -> int:
    if not SRC.is_dir():
        print(f"src directory not found: {SRC}", file=sys.stderr)
        return 1
    OUT.mkdir(parents=True, exist_ok=True)
    projects: dict[str, list[tuple[str, list[dict]]]] = defaultdict(list)
    for cs in sorted(SRC.rglob("*.cs")):
        rel_parts = cs.relative_to(SRC).parts
        if rel_parts[0].endswith(".csproj"):
            continue
        if "obj" in rel_parts or "bin" in rel_parts:
            continue
        project = rel_parts[0]
        ns, types = parse_file(cs)
        if types:
            projects[project].append((ns, types))

    written: list[str] = []
    for project, items in sorted(projects.items()):
        md = render_project(project, items)
        out_path = OUT / f"{project}.md"
        out_path.write_text(md, encoding="utf-8")
        written.append(out_path.name)

    # Index
    idx_lines = ["# C# SDK API Reference", "",
                 "Auto-generated reference for the ARCP C# SDK projects.",
                 "Regenerate with `scripts/docs.sh` (or `make docs-api`).", ""]
    for name in sorted(written):
        idx_lines.append(f"- [{name[:-3]}](./{name})")
    idx_lines.append("")
    (OUT / "index.md").write_text("\n".join(idx_lines), encoding="utf-8")

    total = len(written) + 1
    print(f"Wrote {total} markdown files to {OUT.relative_to(ROOT)}/")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
