"""Tests for Cloudflare documentation-mirror trimming."""

import json
from pathlib import Path
import tempfile
import unittest

from scripts.trim_cloudflare_mirror import trim_search_index, version_key


class TrimCloudflareMirrorTests(unittest.TestCase):
    """Verify semantic version ordering and search-index cleanup."""

    def test_stable_version_sorts_after_prerelease(self) -> None:
        """A stable release must be newer than prereleases of the same version."""
        self.assertGreater(version_key("v1.2.3"), version_key("v1.2.3-rc.1"))

    def test_numeric_prerelease_segments_sort_numerically(self) -> None:
        """Prerelease number 10 must sort after number 2."""
        self.assertGreater(
            version_key("v1.2.3-alpha.10"),
            version_key("v1.2.3-alpha.2"),
        )

    def test_trim_search_index_removes_omitted_versions(self) -> None:
        """Search documents for removed archives must not survive deployment."""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "search_index.json"
            path.write_text(
                json.dumps(
                    {
                        "docs": [
                            {"location": "api/v1.0.0/type/"},
                            {"location": "api/v0.9.0/type/"},
                            {"location": "api/stable/type/"},
                        ]
                    }
                ),
                encoding="utf-8",
            )

            removed = trim_search_index(path, {"v1.0.0"})
            documents = json.loads(path.read_text(encoding="utf-8"))["docs"]

            self.assertEqual(removed, 1)
            self.assertEqual(
                [document["location"] for document in documents],
                ["api/v1.0.0/type/", "api/stable/type/"],
            )


if __name__ == "__main__":
    unittest.main()
