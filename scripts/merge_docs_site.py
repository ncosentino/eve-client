"""Merge a generated documentation slice with the persistent published site."""

from __future__ import annotations

import argparse
from pathlib import Path
import shutil
import tempfile


def main() -> None:
    """Merge development or release-owned documentation paths."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--incoming", required=True, type=Path)
    parser.add_argument("--existing", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--mode", required=True, choices=("main", "release"))
    parser.add_argument("--version")
    args = parser.parse_args()

    if args.mode == "release" and not args.version:
        parser.error("--version is required in release mode")

    with tempfile.TemporaryDirectory() as temporary_directory:
        preserved = Path(temporary_directory)
        preserve_api_slices(args.existing, preserved, args.mode, args.version)

        if args.output.exists():
            shutil.rmtree(args.output)
        shutil.copytree(args.incoming, args.output)

        preserved_api = preserved / "api"
        if preserved_api.exists():
            copy_tree(preserved_api, args.output / "api")

    (args.output / ".nojekyll").touch()


def preserve_api_slices(
    existing: Path,
    destination: Path,
    mode: str,
    version: str | None,
) -> None:
    """Copy API slices owned by the other workflow mode."""
    api_root = existing / "api"
    if not api_root.exists():
        return

    if mode == "main":
        candidates = [api_root / "stable", *sorted(api_root.glob("v*"))]
    else:
        current_version = f"v{version}"
        candidates = [
            api_root / "dev",
            *[
                path
                for path in sorted(api_root.glob("v*"))
                if path.name != current_version
            ],
        ]

    for candidate in candidates:
        if candidate.exists():
            copy_tree(candidate, destination / "api" / candidate.name)


def copy_tree(source: Path, destination: Path) -> None:
    """Copy a directory tree while allowing an existing destination."""
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(source, destination, dirs_exist_ok=True)


if __name__ == "__main__":
    main()
