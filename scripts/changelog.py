#!/usr/bin/env python3
"""Append unreleased changelog notes to the right section in CHANGELOG.md."""

from __future__ import annotations

import argparse
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
CHANGELOG_PATH = ROOT / "CHANGELOG.md"


def ensure_unreleased_block(lines: list[str]) -> int:
    heading = "## Unreleased"
    for index, line in enumerate(lines):
        if line.strip() == heading:
            return index

    insert_at = 0
    if lines and lines[0].startswith("# "):
        insert_at = 1
    lines.insert(insert_at, heading)
    if insert_at == 0:
        lines.insert(1, "")
    else:
        lines.insert(insert_at + 1, "")
    return insert_at


def append_entries(changelog_text: str, category: str, entries: list[str]) -> str:
    lines = changelog_text.splitlines()
    if not lines:
        lines = ["# Changelog", ""]

    unreleased_index = ensure_unreleased_block(lines)
    section_heading = f"### {category}"
    section_index = None
    for index in range(unreleased_index + 1, len(lines)):
        line = lines[index].strip()
        if line.startswith("## ") and line != "## Unreleased":
            break
        if line == section_heading:
            section_index = index
            break

    if section_index is None:
        insert_at = unreleased_index + 1
        while insert_at < len(lines) and not lines[insert_at].strip().startswith("## "):
            insert_at += 1
        lines.insert(insert_at, section_heading)
        lines.insert(insert_at + 1, "")
        section_index = insert_at

    bullet_items = []
    for item in entries:
        text = item.strip()
        if not text:
            continue
        bullet_items.append(f"- {text.lstrip('- ').strip()}")

    if not bullet_items:
        return "\n".join(lines) + "\n"

    insert_at = section_index + 1
    while insert_at < len(lines) and lines[insert_at].strip() and not lines[insert_at].startswith("### ") and not lines[insert_at].startswith("## "):
        insert_at += 1

    for bullet in reversed(bullet_items):
        lines.insert(insert_at, bullet)

    if insert_at < len(lines) and lines[insert_at] != "":
        lines.insert(insert_at, "")

    return "\n".join(lines) + "\n"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Add release notes to the appropriate Unreleased changelog section.")
    parser.add_argument("--added", action="append", default=[], help="Added entry to append under Added.")
    parser.add_argument("--fixed", action="append", default=[], help="Fixed entry to append under Fixed.")
    parser.add_argument("--changed", action="append", default=[], help="Changed entry to append under Changed.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not CHANGELOG_PATH.exists():
        raise FileNotFoundError(f"Changelog not found at {CHANGELOG_PATH}")

    text = CHANGELOG_PATH.read_text(encoding="utf-8")
    for category, key in [("Added", args.added), ("Fixed", args.fixed), ("Changed", args.changed)]:
        text = append_entries(text, category, key)

    CHANGELOG_PATH.write_text(text, encoding="utf-8")
    print(f"Updated {CHANGELOG_PATH.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
