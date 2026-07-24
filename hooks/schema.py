"""Inject SoftwareSourceCode structured data on the documentation home page."""

import json


def on_page_content(html: str, page, config, files) -> str:
    """Append JSON-LD metadata to the home page."""
    if not page.is_homepage:
        return html

    schema = {
        "@context": "https://schema.org",
        "@type": "SoftwareSourceCode",
        "name": config.get("site_name", ""),
        "description": config.get("site_description", ""),
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
    return (
        html
        + '<script type="application/ld+json">\n'
        + json.dumps(schema, indent=2)
        + "\n</script>\n"
    )
