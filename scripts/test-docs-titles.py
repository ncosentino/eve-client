"""Render API metadata and verify historical title-only maintenance."""

from html.parser import HTMLParser
import importlib.util
import os
from pathlib import Path, PurePosixPath
import tempfile
import unittest

from mkdocs.commands.build import build
from mkdocs.config import load_config
import yaml


ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    "refresh_titles", ROOT / "scripts" / "refresh-docs-titles.py",
)
refresh = importlib.util.module_from_spec(spec)
spec.loader.exec_module(refresh)


class Metadata(HTMLParser):
    def __init__(self):
        super().__init__()
        self.title = ""
        self.headings = []
        self.meta = {}
        self.navigation = []
        self.in_navigation = False
        self.in_title = False
        self.in_heading = False

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "title":
            self.in_title = True
        elif tag == "h1":
            self.in_heading = True
            self.headings.append("")
        elif tag == "meta":
            self.meta[attrs.get("property") or attrs.get("name")] = attrs.get("content")
        elif tag == "nav":
            self.in_navigation = True

    def handle_endtag(self, tag):
        if tag == "title":
            self.in_title = False
        elif tag == "h1":
            self.in_heading = False
        elif tag == "nav":
            self.in_navigation = False

    def handle_data(self, data):
        if self.in_title:
            self.title += data
        if self.in_heading:
            self.headings[-1] += data
        if self.in_navigation:
            self.navigation.append(data.strip())


class RenderedTitlesTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workspace = tempfile.TemporaryDirectory(prefix=".title-test-", dir=ROOT)
        cls.addClassCleanup(cls.workspace.cleanup)
        root = Path(cls.workspace.name)
        docs = root / "docs"
        cls.site = root / "site"
        cls.pages = {
            "index.md": ("NexusLabs.Eve", None),
            "getting-started.md": ("Getting Started", None),
            "api/index.md": ("API Reference", "API Reference"),
            "api/dev/index.md": ("API Reference (Development)", "API Reference (Development)"),
        }
        for version, label in (("dev", "Development"), ("stable", "Stable"), ("v0.1.0-alpha.1", "v0.1.0-alpha.1")):
            cls.pages[f"api/{version}/NexusLabs.Eve/index.md"] = (
                "Index", f"NexusLabs.Eve API Reference ({label})",
            )
            cls.pages[f"api/{version}/NexusLabs.Eve/NexusLabs.Eve.md"] = (
                "NexusLabs.Eve", f"NexusLabs.Eve Namespace ({label} API)",
            )
            for namespace in ("NexusLabs.Eve", "NexusLabs.Eve.Models"):
                cls.pages[f"api/{version}/{namespace}/Event.md"] = (
                    "Event", f"{namespace}.Event ({label} API)",
                )
        for source, (heading, _) in cls.pages.items():
            path = docs / source
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(f"# {heading}\n", encoding="utf-8")
        options = {}
        if os.environ.get("EVE_TITLE_BASELINE") == "true":
            options["hooks"] = ["docs/hooks/schema.py", "docs/hooks/llms_txt.py"]
        config = load_config(
            config_file=str(ROOT / "mkdocs.yml"),
            docs_dir=str(docs), site_dir=str(cls.site),
            site_url="https://example.test/reference/",
            nav=[
                {"Overview" if source == "api/index.md" else heading: source}
                for source, (heading, _) in cls.pages.items()
            ],
            markdown_extensions=["tables"],
            **options,
        )
        build(config)

    def test_titles_are_distinct_and_headings_and_navigation_are_preserved(self):
        titles = []
        for source, (heading, metadata) in self.pages.items():
            with self.subTest(source=source):
                relative = source.removesuffix(".md")
                path = self.site / (
                    relative + ".html" if relative.endswith("index")
                    else relative + "/index.html"
                )
                html = path.read_text(encoding="utf-8")
                page = Metadata()
                page.feed(html)
                self.assertEqual(page.headings, [heading])
                expected = metadata or heading
                if source == "index.md":
                    self.assertEqual(page.title.strip(), "NexusLabs.Eve")
                else:
                    self.assertEqual(page.title.strip(), f"{expected} - NexusLabs.Eve")
                self.assertEqual(page.meta["og:title"], f"{expected} – NexusLabs.Eve")
                self.assertEqual(page.meta["twitter:title"], page.meta["og:title"])
                if metadata:
                    titles.append(page.title.strip())
                self.assertIn(
                    "Overview" if source == "api/index.md" else heading,
                    page.navigation,
                )
        self.assertEqual(len(titles), len(set(titles)))


class PublishedTitlesTests(unittest.TestCase):
    def html(self):
        return (
            '<html><head>\r\n<title>Index - NexusLabs.Eve</title>\r\n'
            '<meta property="og:title" content="Index – NexusLabs.Eve">\r\n'
            '<meta name="twitter:title" content="Index – NexusLabs.Eve">\r\n'
            '<meta name="description" content="Untouched"><link rel="canonical" href="/old/">'
            '</head><body><h1>Index</h1><nav>Index</nav>'
            '<svg><title>Diagram</title></svg></body></html>\r\n'
        )

    def make_site(self, root):
        sources = (
            "api/stable/NexusLabs.Eve/index.html",
            "api/v0.1.0-alpha.1/NexusLabs.Eve.Models/Event/index.html",
            "api/dev/NexusLabs.Eve/index.html",
            "getting-started/index.html",
        )
        for source in sources:
            path = root / source
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(self.html().encode("utf-8"))
        return sources

    def test_preview_apply_and_repeat_preserve_all_other_bytes(self):
        with tempfile.TemporaryDirectory(prefix=".title-test-", dir=ROOT) as directory:
            root = Path(directory)
            sources = self.make_site(root)
            before = {source: (root / source).read_bytes() for source in sources}
            preview = refresh.refresh_site(root)
            self.assertEqual(preview["changed"], 2)
            for source in sources:
                self.assertEqual((root / source).read_bytes(), before[source])
            self.assertEqual(refresh.refresh_site(root, apply=True), preview)
            for source, expected in (
                (sources[0], "NexusLabs.Eve API Reference (Stable)"),
                (sources[1], "NexusLabs.Eve.Models.Event (v0.1.0-alpha.1 API)"),
            ):
                self.assertEqual(
                    (root / source).read_bytes(),
                    before[source].decode("utf-8").replace("Index -", f"{expected} -")
                    .replace("Index –", f"{expected} –").encode("utf-8"),
                )
            for source in sources[2:]:
                self.assertEqual((root / source).read_bytes(), before[source])
            self.assertEqual(refresh.refresh_site(root)["changed"], 0)

    def test_all_pages_are_validated_before_any_write(self):
        with tempfile.TemporaryDirectory(prefix=".title-test-", dir=ROOT) as directory:
            root = Path(directory)
            sources = self.make_site(root)
            before = (root / sources[0]).read_bytes()
            (root / sources[1]).write_text("<html><head></head></html>", encoding="utf-8")
            with self.assertRaises(ValueError):
                refresh.refresh_site(root, apply=True)
            self.assertEqual((root / sources[0]).read_bytes(), before)

    def test_missing_duplicate_and_redirected_metadata_are_rejected(self):
        source = PurePosixPath("api/stable/NexusLabs.Eve/index.md")
        for html in (
            self.html().replace("<title>Index - NexusLabs.Eve</title>", ""),
            self.html().replace("</head>", '<meta property="og:title" content="extra"></head>'),
        ):
            with self.assertRaises(ValueError):
                refresh.refresh_html(html, source)
        with tempfile.TemporaryDirectory(prefix=".title-test-", dir=ROOT) as directory:
            root = Path(directory)
            sources = self.make_site(root / "site")
            outside = root / "outside.html"
            outside.write_bytes(self.html().encode("utf-8"))
            target = root / "site" / sources[0]
            target.unlink()
            target.symlink_to(outside)
            with self.assertRaises(ValueError):
                refresh.refresh_site(root / "site", apply=True)
            self.assertEqual(outside.read_bytes(), self.html().encode("utf-8"))


class PublicationTests(unittest.TestCase):
    def test_all_writers_share_the_existing_non_cancelling_lock(self):
        workflows = {}
        for name in ("docs.yml", "release.yml", "docs-cloudflare.yml", "docs-titles.yml"):
            workflows[name] = yaml.safe_load(
                (ROOT / ".github" / "workflows" / name).read_text(encoding="utf-8"),
            )
        for name in ("release.yml", "docs-cloudflare.yml"):
            self.assertEqual(workflows[name]["concurrency"], {
                "group": "pages", "cancel-in-progress": False,
            })
        self.assertIn("'pages'", workflows["docs.yml"]["concurrency"]["group"])
        self.assertEqual(
            workflows["docs-titles.yml"]["jobs"]["refresh-published-titles"]["concurrency"],
            {"group": "pages", "cancel-in-progress": False},
        )
        for job in workflows["docs-titles.yml"]["jobs"].values():
            for step in job["steps"]:
                if step.get("uses", "").startswith("actions/upload-artifact@"):
                    self.assertIn("${{ github.run_attempt }}", step["with"]["name"])


if __name__ == "__main__":
    unittest.main()
