"""Verify API entities in rendered documentation, not source string fragments."""

from html.parser import HTMLParser
import json
from pathlib import Path, PurePosixPath
import tempfile
import unittest
from urllib.parse import quote

from mkdocs.commands.build import build
from mkdocs.config import load_config


ROOT = Path(__file__).resolve().parents[2]


class PageMetadata(HTMLParser):
    def __init__(self):
        super().__init__()
        self.canonical = None
        self.description = None
        self.entities = []
        self.payload = None

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'link' and attrs.get('rel') == 'canonical':
            self.canonical = attrs['href']
        elif tag == 'meta' and attrs.get('name') == 'description':
            self.description = attrs['content']
        elif tag == 'script' and attrs.get('type') == 'application/ld+json':
            self.payload = ''

    def handle_data(self, data):
        if self.payload is not None:
            self.payload += data

    def handle_endtag(self, tag):
        if tag == 'script' and self.payload is not None:
            self.entities.append(json.loads(self.payload))
            self.payload = None


class ApiSchemaTests(unittest.TestCase):
    def test_rendered_api_entities_reuse_metadata_and_preserve_version_identity(self):
        summary = 'Registers `Factory<T, U>` with &amp; "plugins" and `</script>` inputs.'
        pages = {'index.md': '# Home\n', 'guide.md': '# Guide\n\nGuide prose.\n'}
        for version in ('dev', 'stable', 'v0.1.0-alpha.1'):
            pages[f'api/{version}/Example/Factory_T, U_.md'] = (
                '# Factory\n\n## Factory Class\n\n' + summary
                + '\n\n```csharp\nclass Factory<T, U> {}\n```\n'
            )
        pages['api/index.md'] = '# API Reference\n'
        with tempfile.TemporaryDirectory(prefix='.schema-test-', dir=ROOT) as directory:
            workspace = Path(directory)
            docs = workspace / 'docs'
            for source, markdown in pages.items():
                path = docs / source
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(markdown, encoding='utf-8')
            config = load_config(
                config_file=str(ROOT / 'mkdocs.yml'),
                docs_dir=str(docs), site_dir=str(workspace / 'site'),
                nav=list(pages), extra_javascript=[],
            )
            build(config)
            identities = []
            for source, markdown in pages.items():
                with self.subTest(source=source):
                    route = PurePosixPath(source).with_suffix('')
                    if route.name == 'index':
                        route = route.parent
                    html = (workspace / 'site' / str(route) / 'index.html').read_text(
                        encoding='utf-8',
                    )
                    parsed = PageMetadata()
                    parsed.feed(html)
                    entities = [e for e in parsed.entities if e.get('@type') == 'APIReference']
                    self.assertEqual(len(entities), 1 if source.startswith('api/') else 0)
                    self.assertEqual((docs / source).read_text(encoding='utf-8'), markdown)
                    if not entities:
                        continue
                    entity = entities[0]
                    expected = config.site_url.rstrip('/') + '/' + '/'.join(
                        quote(segment, safe='') for segment in route.parts
                    )
                    self.assertEqual(parsed.canonical, expected)
                    self.assertEqual(entity, {
                        '@context': 'https://schema.org',
                        '@type': 'APIReference',
                        '@id': expected,
                        'name': (
                            'API Reference' if source == 'api/index.md' else
                            'Example.Factory_T, U_ ('
                            + {'dev': 'Development', 'stable': 'Stable'}.get(
                                route.parts[1], route.parts[1],
                            ) + ' API)'
                        ),
                        'url': expected,
                        'description': parsed.description,
                    })
                    identities.append(entity['@id'])
                    if source != 'api/index.md':
                        self.assertIn(
                            'Registers Factory<T, U> with & "plugins" and </script> inputs.',
                            entity['description'],
                        )
                        self.assertNotIn('with & "plugins" and </script> inputs.', html)
                        self.assertIn('\\u003c/script>', html)
            self.assertEqual(len(identities), len(set(identities)))


if __name__ == '__main__':
    unittest.main()
