"""Inject API page metadata and documentation home-page structured data."""

import json


def _schema_script(schema: dict) -> str:
    payload = json.dumps(schema, indent=2, ensure_ascii=False).replace('<', '\\u003c')
    return '<script type="application/ld+json">\n' + payload + '\n</script>\n'


def on_page_content(html: str, page, config, files) -> str:
    """Append structured data using the existing plain-text page metadata."""
    if page.meta.get('api_reference_title') is not None:
        schema = {
            '@context': 'https://schema.org',
            '@type': 'APIReference',
            '@id': page.canonical_url,
            'name': page.meta['api_reference_title'],
            'url': page.canonical_url,
            'description': page.meta['description'],
        }
        return html + _schema_script(schema)
    if not page.is_homepage:
        return html

    schema = {
        "@context": "https://schema.org",
        "@type": "SoftwareSourceCode",
        "name": config.get("site_name", ""),
        "description": page.meta.get("description", config.get("site_description", "")),
        "url": config.get("site_url", ""),
        "codeRepository": config.get("repo_url", ""),
        "license": f"{config.get('repo_url', '')}/blob/main/LICENSE",
        "programmingLanguage": "C#",
        "runtimePlatform": ".NET 10",
        "author": {
            "@type": "Person",
            "name": "Nick Cosentino",
            "url": "https://www.devleader.ca",
        },
    }
    return html + _schema_script(schema)
