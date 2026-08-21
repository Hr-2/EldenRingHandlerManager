#!/usr/bin/env python3
"""Bumps the project version and updates CHANGELOG.md from git history."""
import datetime
import re
import subprocess
import sys

PROJECT = "EldenRingHandlerManager.csproj"
CHANGELOG = "CHANGELOG.md"


def git(args):
    return subprocess.run(args, capture_output=True, text=True).stdout.strip()


def main():
    csproj = open(PROJECT).read()
    m = re.search(r"<Version>([^<]+)</Version>", csproj)
    if not m:
        print("ERROR: no <Version> found in csproj")
        sys.exit(1)

    cur = m.group(1)
    try:
        major, minor, patch = map(int, cur.split("."))
    except ValueError:
        print(f"ERROR: unparseable version '{cur}'")
        sys.exit(1)
    nextv = f"{major}.{minor}.{patch + 1}"

    # Collect commit messages since the last tag (or all if no tags yet).
    last_tag = git(["git", "describe", "--tags", "--abbrev=0"])
    if last_tag:
        log = git(["git", "log", f"{last_tag}..HEAD", "--pretty=format:- %s", "--no-merges"])
    else:
        log = git(["git", "log", "--pretty=format:- %s", "--no-merges"])
    commits = log.strip()

    # Update csproj version fields.
    csproj = re.sub(r"<Version>[^<]+</Version>", f"<Version>{nextv}</Version>", csproj, count=1)
    csproj = re.sub(r"<FileVersion>[^<]+</FileVersion>", f"<FileVersion>{nextv}</FileVersion>", csproj, count=1)
    csproj = re.sub(r"<InformationalVersion>[^<]+</InformationalVersion>",
                    f"<InformationalVersion>{nextv}</InformationalVersion>", csproj, count=1)
    open(PROJECT, "w").write(csproj)

    # Insert a new changelog section right after the [Unreleased] heading.
    text = open(CHANGELOG).read()
    idx = text.find("## [Unreleased]")
    end = text.find("\n## [", idx + 3) if idx != -1 else -1
    if idx == -1:
        print("ERROR: no '## [Unreleased]' heading in CHANGELOG.md")
        sys.exit(1)
    insert_at = end if end != -1 else len(text)

    date = datetime.date.today().isoformat()
    section = f"\n## [{nextv}] - {date}\n\n### Changed\n{commits}\n"
    text = text[:insert_at] + section + text[insert_at:]
    open(CHANGELOG, "w").write(text)

    print(f"CURRENT={cur}")
    print(f"NEXT={nextv}")


if __name__ == "__main__":
    main()
