"""Tests for documentation-site slice ownership."""

from pathlib import Path
import tempfile
import unittest

from scripts.merge_docs_site import copy_tree, preserve_api_slices


class MergeDocsSiteTests(unittest.TestCase):
    """Verify that main and release builds preserve each other's API slices."""

    def test_main_preserves_release_slices(self) -> None:
        """Main owns development docs but preserves stable and versioned docs."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            existing = root / "existing"
            preserved = root / "preserved"
            self._write(existing / "api/stable/index.html", "stable")
            self._write(existing / "api/v0.1.0/index.html", "version")
            self._write(existing / "api/dev/index.html", "old-dev")

            preserve_api_slices(existing, preserved, "main", None)

            self.assertEqual(
                (preserved / "api/stable/index.html").read_text(),
                "stable",
            )
            self.assertEqual(
                (preserved / "api/v0.1.0/index.html").read_text(),
                "version",
            )
            self.assertFalse((preserved / "api/dev").exists())

    def test_release_preserves_development_and_older_versions(self) -> None:
        """A release replaces its stable/current slices but preserves other owners."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            existing = root / "existing"
            preserved = root / "preserved"
            self._write(existing / "api/dev/index.html", "dev")
            self._write(existing / "api/v0.1.0/index.html", "old-version")
            self._write(existing / "api/v0.2.0/index.html", "current-version")
            self._write(existing / "api/stable/index.html", "old-stable")

            preserve_api_slices(existing, preserved, "release", "0.2.0")

            self.assertEqual(
                (preserved / "api/dev/index.html").read_text(),
                "dev",
            )
            self.assertEqual(
                (preserved / "api/v0.1.0/index.html").read_text(),
                "old-version",
            )
            self.assertFalse((preserved / "api/v0.2.0").exists())
            self.assertFalse((preserved / "api/stable").exists())

    def test_copy_tree_merges_existing_destination(self) -> None:
        """Copying a preserved slice does not remove unrelated output files."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "source"
            destination = root / "destination"
            self._write(source / "index.html", "source")
            self._write(destination / "other.html", "other")

            copy_tree(source, destination)

            self.assertEqual((destination / "index.html").read_text(), "source")
            self.assertEqual((destination / "other.html").read_text(), "other")

    @staticmethod
    def _write(path: Path, content: str) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")


if __name__ == "__main__":
    unittest.main()
