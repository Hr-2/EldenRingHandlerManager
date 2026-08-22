#!/usr/bin/env python3
"""Prepares the next release from git history.

Rules:
- Only creates a release when actual source files (.cs/.xaml/.csproj)
  changed since the last tag. Doc-only pushes (README, CHANGELOG, workflow,
  etc.) print SKIP=1 and do nothing, so they don't produce useless versions.
- If the version in the csproj is already tagged, bump the patch version.
  Otherwise release the csproj version as-is (set it before pushing).
- Updates CHANGELOG.md with a section for the version being released.
"""
import datetime
import re
import subprocess
import sys

PROJECT = "EldenRingHandlerManager.csproj"
CHANGELOG = "CHANGELOG.md"

# Files that count as a real code change worth a release.
CODE_SUFFIXES = (".cs", ".xaml", ".csproj")


def git(args):
    return subprocess.run(args, capture_output=True, text=True).stdout.strip()


def last_tag():
    tag = git(["git", "describe", "--tags", "--abbrev=0"])
    return tag or None


def changed_files():
    """Files changed since the last tag (or all tracked files if no tags)."""
    base = last_tag()
    if base:
        out = git(["git", "diff", "--name-only", f"{base}..HEAD"])
    else:
        out = git(["git", "ls-files"])
    return [f for f in out.splitlines() if f]


def has_code_changes(files):
    return any(f.endswith(CODE_SUFFIXES) for f in files)


def read_version():
    csproj = open(PROJECT).read()
    m = re.search(r"<Version>([^<]+)</Version>", csproj)
    if not m:
        print("ERROR: no <Version> found in csproj")
        sys.exit(1)
    return m.group(1)


def write_version(nextv):
    csproj = open(PROJECT).read()
    csproj = re.sub(r"<Version>[^<]+</Version>", f"<Version>{nextv}</Version>", csproj, count=1)
    csproj = re.sub(r"<FileVersion>[^<]+</FileVersion>", f"<FileVersion>{nextv}</FileVersion>", csproj, count=1)
    csproj = re.sub(r"<InformationalVersion>[^<]+</InformationalVersion>",
                    f"<InformationalVersion>{nextv}</InformationalVersion>", csproj, count=1)
    open(PROJECT, "w").write(csproj)


def add_changelog_section(nextv, commits):
    text = open(CHANGELOG).read()
    idx = text.find("## [Unreleased]")
    if idx == -1:
        print("ERROR: no '## [Unreleased]' heading in CHANGELOG.md")
        sys.exit(1)
    end = text.find("\n## [", idx + 3)
    insert_at = end if end != -1 else len(text)

    # If a section for this version already exists (hand-written), keep it.
    if f"## [{nextv}]" in text:
        return

    date = datetime.date.today().isoformat()
    section = f"\n## [{nextv}] - {date}\n\n### Changed\n{commits}\n"
    text = text[:insert_at] + section + text[insert_at:]
    open(CHANGELOG, "w").write(text)


def main():
    if not has_code_changes(changed_files()):
        print("SKIP=1")
        return

    cur = read_version()

    # Collect commit messages since the last tag (or all if no tags yet).
    base = last_tag()
    if base:
        log = git(["git", "log", f"{base}..HEAD", "--pretty=format:- %s", "--no-merges"])
    else:
        log = git(["git", "log", "--pretty=format:- %s", "--no-merges"])
    commits = log.strip()

    # If this version was already released, bump the patch; otherwise ship it.
    if git(["git", "tag", "-l", f"v{cur}"]):
        try:
            major, minor, patch = map(int, cur.split("."))
        except ValueError:
            print(f"ERROR: unparseable version '{cur}'")
            sys.exit(1)
        nextv = f"{major}.{minor}.{patch + 1}"
        write_version(nextv)
    else:
        nextv = cur

    add_changelog_section(nextv, commits)

    print(f"CURRENT={cur}")
    print(f"NEXT={nextv}")


if __name__ == "__main__":
    main()
