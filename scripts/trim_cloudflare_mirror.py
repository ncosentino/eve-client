"""Trim old versioned API archives before a Cloudflare Pages deployment."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import shutil


_VERSION_PATTERN = re.compile(
    r"^v(?P<major>\d+)\.(?P<minor>\d+)\.(?P<patch>\d+)"
    r"(?:-(?P<prerelease>[0-9A-Za-z.-]+))?$"
)


def main() -> None:
    """Remove Git metadata and version archives beyond the configured limit."""
    parser = argparse.ArgumentParser()
    parser.add_argument("mirror_directory", type=Path)
    parser.add_argument("--keep", type=int, default=20)
    args = parser.parse_args()

    if args.keep < 0:
        parser.error("--keep cannot be negative")
    if not args.mirror_directory.is_dir():
        parser.error("mirror_directory must be an existing directory")

    git_directory = args.mirror_directory / ".git"
    if git_directory.exists():
        shutil.rmtree(git_directory)

    api_directory = args.mirror_directory / "api"
    version_directories = (
        sorted(
            (
                path
                for path in api_directory.glob("v*")
                if path.is_dir() and version_key(path.name) is not None
            ),
            key=lambda path: version_key(path.name),
            reverse=True,
        )
        if api_directory.exists()
        else []
    )
    retained_names = {path.name for path in version_directories[: args.keep]}

    removed_directories = 0
    for directory in version_directories[args.keep :]:
        shutil.rmtree(directory)
        removed_directories += 1

    removed_search_entries = trim_search_index(
        args.mirror_directory / "search/search_index.json",
        retained_names,
    )
    print(
        f"Kept {len(retained_names)} version archive(s); "
        f"removed {removed_directories} archive(s) and "
        f"{removed_search_entries} search entries."
    )


def version_key(name: str):
    """Return a sortable semantic-version key for a v-prefixed directory."""
    match = _VERSION_PATTERN.fullmatch(name)
    if match is None:
        return None

    prerelease = match.group("prerelease")
    prerelease_key = (
        ((2, ""),)
        if prerelease is None
        else tuple(
            (0, int(part)) if part.isdigit() else (1, part)
            for part in prerelease.split(".")
        )
    )
    return (
        int(match.group("major")),
        int(match.group("minor")),
        int(match.group("patch")),
        prerelease_key,
    )


def trim_search_index(path: Path, retained_names: set[str]) -> int:
    """Remove search records that point at omitted version archives."""
    if not path.is_file():
        return 0

    data = json.loads(path.read_text(encoding="utf-8"))
    original_documents = data.get("docs", [])
    retained_documents = []

    for document in original_documents:
        location = str(document.get("location", ""))
        parts = location.split("/", 2)
        if (
            len(parts) >= 2
            and version_key(parts[1]) is not None
            and parts[1] not in retained_names
        ):
            continue
        retained_documents.append(document)

    data["docs"] = retained_documents
    path.write_text(
        json.dumps(data, separators=(",", ":")),
        encoding="utf-8",
    )
    return len(original_documents) - len(retained_documents)


if __name__ == "__main__":
    main()
