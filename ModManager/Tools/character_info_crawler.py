"""Read character names from the official, JavaScript-rendered game wikis.

The application intentionally invokes this script with the Python standard
library only. Chrome or Edge is used in headless mode so the script can read
the same rendered DOM that a user sees in a normal browser, without adding a
large browser automation runtime to the application package.
"""

from __future__ import annotations

import argparse
import html
import json
import os
import re
import shutil
import subprocess
import sys
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urljoin, urlsplit, urlunsplit


SOURCES = {
    "GI": "https://baike.mihoyo.com/ys/obc/channel/map/189/25?bbs_presentation_style=no_header&visit_device=pc",
    "WW": "https://wiki.kurobbs.com/mc/catalogue/list?fid=1099&sid=1105",
}

# These are explanatory links on the source page, not playable characters.
IGNORED_CHARACTER_NAMES = {"如何成为观测者"}


class AnchorParser(HTMLParser):
    """Collect anchor hrefs, visible text, and image URLs nested inside each anchor."""

    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self._current: dict[str, object] | None = None
        self.anchors: list[tuple[str, str, str]] = []

    def handle_startendtag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        self.handle_starttag(tag, attrs)
        self.handle_endtag(tag)

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if self._current is not None:
            attributes = dict(attrs)
            image = (
                attributes.get("data-src")
                or attributes.get("src")
                or attributes.get("data-original")
                or ""
            )
            if not image:
                style = attributes.get("style") or ""
                match = re.search(r"url\(\s*[\"']?([^\"')\s]+)", style, re.I)
                image = match.group(1) if match else ""
            if image:
                self._current["image"] = image
            if tag.lower() == "img":
                return
        if tag.lower() != "a" or self._current is not None:
            return
        attributes = dict(attrs)
        self._current = {"href": attributes.get("href") or "", "parts": [], "image": ""}

    def handle_data(self, data: str) -> None:
        if self._current is not None:
            self._current["parts"].append(data)  # type: ignore[union-attr]

    def handle_endtag(self, tag: str) -> None:
        if tag.lower() != "a" or self._current is None:
            return
        href = str(self._current["href"])
        text = "".join(self._current["parts"])  # type: ignore[arg-type]
        self.anchors.append((href, text, str(self._current["image"])))
        self._current = None


def find_browser() -> str | None:
    configured = os.environ.get("MODMANAGER_BROWSER", "").strip().strip('"')
    if configured and Path(configured).is_file():
        return configured

    local_app_data = os.environ.get("LOCALAPPDATA", "")
    program_files = os.environ.get("ProgramFiles", "")
    program_files_x86 = os.environ.get("ProgramFiles(x86)", "")
    candidates = [
        Path(program_files) / "Google/Chrome/Application/chrome.exe",
        Path(program_files_x86) / "Google/Chrome/Application/chrome.exe",
        Path(local_app_data) / "Google/Chrome/Application/chrome.exe",
        Path(program_files) / "Microsoft/Edge/Application/msedge.exe",
        Path(program_files_x86) / "Microsoft/Edge/Application/msedge.exe",
        Path(local_app_data) / "Microsoft/Edge/Application/msedge.exe",
    ]
    for command in ("chrome.exe", "msedge.exe", "chrome", "msedge"):
        resolved = shutil.which(command)
        if resolved:
            candidates.append(Path(resolved))

    for candidate in candidates:
        if candidate.is_file():
            return str(candidate)
    return None


def load_rendered_html(url: str, timeout_seconds: int) -> str:
    browser = find_browser()
    if browser is None:
        raise RuntimeError(
            "未找到 Chrome 或 Edge。请安装任一浏览器，或设置 MODMANAGER_BROWSER 环境变量。"
        )

    command = [
        browser,
        "--headless=new",
        "--disable-gpu",
        "--no-sandbox",
        "--disable-extensions",
        "--no-first-run",
        "--no-default-browser-check",
        "--dump-dom",
        f"--virtual-time-budget={max(10000, timeout_seconds * 1000 // 2)}",
        url,
    ]
    try:
        completed = subprocess.run(
            command,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=max(15, timeout_seconds),
            check=False,
            text=True,
            encoding="utf-8",
            errors="replace",
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError("浏览器加载官方图鉴超时，请检查网络连接后重试。") from exc

    if completed.returncode != 0 and not completed.stdout.strip():
        detail = completed.stderr.strip()
        raise RuntimeError(f"浏览器抓取失败{(': ' + detail) if detail else '。'}")
    if not completed.stdout.strip():
        raise RuntimeError("浏览器没有返回图鉴页面内容。")
    return completed.stdout


def clean_name(value: str) -> str:
    value = html.unescape(value)
    return re.sub(r"\s+", " ", value).strip()


def parse_characters(game_id: str, rendered_html: str) -> list[dict[str, str]]:
    parser = AnchorParser()
    parser.feed(rendered_html)
    characters: list[dict[str, str]] = []
    seen: set[str] = set()

    for href, raw_text, image in parser.anchors:
        decoded_href = html.unescape(href)
        if game_id == "GI":
            is_character = bool(
                re.search(r"/ys/obc/content/\d+/detail(?:[/?#]|$)", decoded_href, re.I)
            )
        else:
            is_character = bool(
                re.search(r"/mc/item/\d+", decoded_href, re.I)
                and "wkFrom=catalog" in decoded_href
            )
        if not is_character:
            continue

        name = clean_name(raw_text)
        if name in IGNORED_CHARACTER_NAMES:
            continue
        if name and name.casefold() not in seen:
            seen.add(name.casefold())
            image_url = ""
            if image.strip():
                image_url = urljoin(SOURCES[game_id], html.unescape(image).strip())
                image_parts = urlsplit(image_url)
                image_url = urlunsplit(
                    (image_parts.scheme, image_parts.netloc, image_parts.path, "", "")
                )
            characters.append({
                "name": name,
                "image_url": image_url,
            })
    return characters


def main() -> int:
    # Windows may use the active code page for redirected Python streams.
    # The C# caller consumes JSON as UTF-8, so make that contract explicit.
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")

    parser = argparse.ArgumentParser()
    parser.add_argument("--game", choices=sorted(SOURCES), required=True)
    parser.add_argument("--timeout-seconds", type=int, default=90)
    args = parser.parse_args()

    try:
        rendered_html = load_rendered_html(SOURCES[args.game], args.timeout_seconds)
        characters = parse_characters(args.game, rendered_html)
        if not characters:
            raise RuntimeError("图鉴页面未返回可识别的角色信息，可能是页面结构已变化。")
        json.dump({"characters": characters}, sys.stdout, ensure_ascii=False)
        sys.stdout.write("\n")
        return 0
    except Exception as exc:
        print(str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
