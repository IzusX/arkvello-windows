"""Bounded stage-1 evidence; no GUI, registry, shell registration or real inputs.

Run from the repo: python scripts/probe-reuse-cli.py
Outputs stay in a fresh build/reuse-cli-* folder. This is not a CLI UI adapter
or evidence of full FM parity. A timeout kills only the probe child and fails
the probe; it is never recorded as successful application cancellation.
"""

import hashlib
import json
import platform
import subprocess
import tempfile
from pathlib import Path


repo = Path(__file__).resolve().parents[1]
exe = repo / "build/windows-x64/7z.exe"
codec = exe.with_name("7z.dll")
root = Path(tempfile.mkdtemp(prefix="reuse-cli-", dir=repo / "build"))
source = root / "source"
source.mkdir()
names = ["текст & [1].txt", "-switch.txt", "ordinary.txt"]
for name in names:
    (source / name).write_text("stage 1 fixture: " + name, encoding="utf-8")
records = []


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(label, args, expected=0, stdin="", cwd=source):
    completed = subprocess.run(
        [str(exe), *map(str, args), "-sccUTF-8"],
        input=stdin.encode("utf-8"), stdout=subprocess.PIPE,
        stderr=subprocess.PIPE, cwd=cwd, timeout=30,
        creationflags=subprocess.CREATE_NO_WINDOW,
    )
    entry = {"case": label, "args": list(map(str, args)),
             "exit": completed.returncode, "expected_exit": expected,
             "stdout": completed.stdout.decode("utf-8", errors="strict"),
             "stderr": completed.stderr.decode("utf-8", errors="strict")}
    records.append(entry)
    (root / "commands.json").write_text(
        json.dumps(records, ensure_ascii=False, indent=2), encoding="utf-8")
    if completed.returncode != expected:
        raise RuntimeError(f"{label}: exit {completed.returncode}; see {root}")
    return entry


archive = root / "fixture.7z"
run("create", ["a", "-t7z", "-mx1", archive, "."])
listing = run("technical-list", ["l", "-slt", archive])
assert all(name in listing["stdout"] for name in names)
out = root / "selected"
run("literal-selected-extract", ["x", "-spd", archive, names[0], f"-o{out}"])
assert sorted(p.name for p in out.iterdir()) == [names[0]]
assert digest(out / names[0]) == digest(source / names[0])

for answer in ["y", "a", "n", "s", "u", "q"]:
    dest = root / ("conflict-" + answer)
    dest.mkdir()
    target = dest / "ordinary.txt"
    target.write_bytes(b"existing destination")
    run("overwrite-" + answer,
        ["x", "-spd", archive, "ordinary.txt", f"-o{dest}"],
        expected=255 if answer == "q" else 0, stdin=answer + "\n")
    if answer in ["y", "a"]:
        assert digest(target) == digest(source / "ordinary.txt")
    else:
        assert target.read_bytes() == b"existing destination"
    if answer == "u":
        others = [p for p in dest.iterdir() if p != target]
        assert len(others) == 1
        assert digest(others[0]) == digest(source / "ordinary.txt")

# Public, disposable fixture password. Never use real credentials in probes.
protected = root / "protected.7z"
run("create-header-encrypted", ["a", "-t7z", "-mx1", "-mhe=on",
                              "-pStage1FixtureOnly", protected, "."])
run("password-via-stdin", ["t", protected], stdin="Stage1FixtureOnly\n")
run("wrong-password", ["t", "-pWrongFixturePassword", protected], expected=2)
run("password-eof", ["t", protected], expected=255)
warning = root / "warning.7z"
run("missing-input-warning", ["a", "-t7z", warning,
                              "ordinary.txt", "missing-input.txt"], expected=1)
run("partial-archive-is-readable", ["t", warning])

summary = {
    "os": platform.platform(),
    "source_head": subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip(),
    "cli_sha256": digest(exe), "codec_sha256": digest(codec),
    "cases": len(records), "status": "pass",
    "limits": ["one conflict per case; All persistence not tested",
               "no live prompt detection/parser or GUI callbacks tested",
               "no cooperative running cancellation, pause or priority test",
               "no NTFS metadata, long/UNC paths or nested edit-back test",
               "local development binaries; not pristine baseline comparison"],
}
(root / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
print(json.dumps({"evidence": str(root), **summary}, indent=2))
