"""Generate an llms.txt index from the MkDocs navigation."""

from pathlib import Path


_PAGE_DESCRIPTIONS: dict[str, tuple[str, str]] = {}


def on_page_context(context, page, config, nav) -> None:
    """Collect each page title and description."""
    description = (
        page.meta.get("description", "")
        if page.meta
        else config.get("site_description", "")
    )
    _PAGE_DESCRIPTIONS[page.url or ""] = (page.title or "", description)


def on_post_build(config) -> None:
    """Write the llms.txt file after the site is built."""
    site_url = config.get("site_url", "").rstrip("/")
    lines = [
        f"# {config.get('site_name', '')}\n\n",
        f"> {config.get('site_description', '')}\n\n",
        "Author: Nick Cosentino (https://www.devleader.ca).\n\n",
    ]

    def walk(items) -> None:
        for item in items:
            if isinstance(item, dict):
                for title, value in item.items():
                    if isinstance(value, list):
                        lines.append(f"\n## {title}\n\n")
                        walk(value)
                    elif isinstance(value, str):
                        append_page(value)
            elif isinstance(item, str):
                append_page(item)

    def append_page(markdown_path: str) -> None:
        url = markdown_path.removesuffix(".md").replace("index", "").strip("/")
        page_url = f"{url}/" if url else ""
        title, description = _PAGE_DESCRIPTIONS.get(page_url, (markdown_path, ""))
        absolute_url = f"{site_url}/{page_url}"
        suffix = f" -- {description}" if description else ""
        lines.append(f"- [{title}]({absolute_url}){suffix}\n")

    walk(config.get("nav", []))
    output_path = Path(config["site_dir"]) / "llms.txt"
    output_path.write_text("".join(lines), encoding="utf-8")
