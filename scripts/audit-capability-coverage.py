"""Audit explicit command mappings against pinned upstream; no runtime claims.

This is a lexical cross-check of MENUITEMs and command-handler cases, not a
C++/RC preprocessor or proof that every feature/keyboard path is inventoried.
Dynamic IDs/options are reviewed separately in CAPABILITY-ASSESSMENT.md.
"""

import json
import re
import subprocess
from pathlib import Path


repo = Path(__file__).resolve().parents[1]
baseline = "d3296e39e1f1f92c7b41c0c3848e65640428d42d"
document = (repo / "docs/CAPABILITY-ASSESSMENT.md").read_text(encoding="utf-8")
ledger = document.split("<!-- command-coverage-start -->")[1].split(
    "<!-- command-coverage-end -->")[0]
mappings = {}
for line in ledger.strip().splitlines():
    group, commands = line.split(":", 1)
    for command in commands.split():
        if command in mappings:
            raise ValueError(f"Duplicate command mapping: {command}")
        mappings[command] = group


def source(path):
    original = subprocess.check_output(
        ["git", "show", f"{baseline}:{path}"], cwd=repo).decode("utf-8-sig")
    # Keep quoted strings, remove both styles of comment without treating
    # comment-like characters inside captions/strings as comments.
    return re.sub(r'"(?:\\.|[^"\\])*"|//[^\n]*|/\*[\s\S]*?\*/',
                  lambda m: m[0] if m[0].startswith('"') else " ", original)


folder = "CPP/7zip/UI/FileManager/"
menu = source(folder + "resource.rc")
handlers = source(folder + "MyLoadMenu.cpp")
frame = source(folder + "FM.cpp")
menu_ids = set(re.findall(
    r'MENUITEM\s+"(?:\\.|[^"\\])*"\s*,\s*(IDM_\w+|IDCLOSE)', menu))
handler_ids = set(re.findall(r"case\s+(IDM_\w+|IDCLOSE)\s*:", handlers))
toolbar_ids = set(re.findall(r"case\s+(kMenuCmdID_Toolbar_\w+)\s*:", frame))
expected = menu_ids | handler_ids | toolbar_ids
missing = sorted(expected - mappings.keys())
extra = sorted(mappings.keys() - expected)
headings = set(re.findall(r"^### ([A-Z]+-\d+):", document, re.MULTILINE))
invalid_groups = sorted(set(mappings.values()) - headings)
missing_files = []
for target in re.findall(r"\]\(([^)#]+)(?:#[^)]*)?\)", document):
    if "://" not in target and not (repo / "docs" / target).is_file():
        missing_files.append(target)

report = {
    "baseline": baseline, "capability_groups": len(headings),
    "static_menu_ids": len(menu_ids), "handler_case_ids": len(handler_ids),
    "archive_toolbar_ids": len(toolbar_ids), "distinct_command_ids": len(expected),
    "mapped_command_ids": len(mappings), "missing": missing, "extra": extra,
    "invalid_groups": invalid_groups, "missing_link_targets": missing_files,
    "limits": "lexical command coverage; not full preprocessing or runtime parity",
}
print(json.dumps(report, indent=2))
raise SystemExit(bool(missing or extra or invalid_groups or missing_files))
