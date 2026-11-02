#!/usr/bin/env python3
"""Capture the documented DeskPages UI simulation into reproducible README media.

This is a simulation, NOT a screen recording of Windows Explorer or the native
DeskPages installer. CI renders the exact interactive HTML shipped in docs/demo.
"""
from pathlib import Path
import shutil
import subprocess
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[2]
HTML = (ROOT / "docs/demo/index.html").read_text(encoding="utf-8")
OUT = ROOT / "docs/media"
OUT.mkdir(parents=True, exist_ok=True)
NAMES = [
    "01-work-desktop",
    "02-pages-panel",
    "03-university-page",
    "04-taskbar-groups",
    "05-group-menu",
    "06-apps-in-group",
    "07-group-deleted",
]
with sync_playwright() as playwright:
    # Browser provided by Playwright install chromium, or system chromium.
    chrome = shutil.which("chromium") or shutil.which("chromium-browser")
    kwargs = {"headless": True, "args": ["--no-sandbox", "--disable-dev-shm-usage"]}
    if chrome:
        kwargs["executable_path"] = chrome
    browser = playwright.chromium.launch(**kwargs)
    page = browser.new_page(viewport={"width": 1440, "height": 810}, device_scale_factor=1)
    errors = []
    page.on("pageerror", lambda e: errors.append(str(e)))
    page.set_content(HTML)
    page.wait_for_function("typeof window.demo === 'object'")
    def image(name):
        page.screenshot(path=str(OUT / (name + ".png")), animations="disabled")
    image(NAMES[0])
    page.evaluate("demo.togglePanel()")
    image(NAMES[1])
    page.evaluate("demo.setPage(1);demo.step('pages')")
    image(NAMES[2])
    page.evaluate("demo.togglePanel();demo.setPage(3);demo.step('groups');demo.addGroup();demo.addGroup()")
    image(NAMES[3])
    page.evaluate("demo.openGroup(0)")
    image(NAMES[4])
    page.evaluate("demo.addMember()")
    image(NAMES[5])
    page.evaluate("demo.deleteGroup();demo.step('finish')")
    image(NAMES[6])
    if errors:
        raise RuntimeError("JavaScript errors in interactive demo: " + str(errors))
    browser.close()

ffmpeg = shutil.which("ffmpeg")
if not ffmpeg:
    raise RuntimeError("FFmpeg is required; media must not be silently skipped.")
command = [ffmpeg, "-y", "-hide_banner", "-loglevel", "error"]
for name in NAMES:
    command += ["-loop", "1", "-t", "2.5", "-i", str(OUT / (name + ".png"))]
filters = []
for i in range(len(NAMES)):
    filters.append(f"[{i}:v]fps=24,scale=1280:720,format=yuv420p,setsar=1[v{i}]")
last = "v0"
for i in range(1, len(NAMES)):
    target = f"x{i}"
    filters.append(f"[{last}][v{i}]xfade=transition=fade:duration=0.5:offset={i * 2:.1f}[{target}]")
    last = target
video = OUT / "DeskPages-Simulation.mp4"
subprocess.run(
    command + ["-filter_complex", ";".join(filters), "-map", f"[{last}]",
               "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p",
               "-preset", "veryfast", "-crf", "24", "-movflags", "+faststart",
               "-t", "14.5", str(video)],
    check=True, timeout=180,
)
gif = OUT / "DeskPages-Preview.gif"
subprocess.run(
    [ffmpeg, "-y", "-hide_banner", "-loglevel", "error", "-i", str(video),
     "-vf", "fps=7,scale=720:-2:flags=lanczos,split[s0][s1];"
            "[s0]palettegen=max_colors=90[p];"
            "[s1][p]paletteuse=dither=bayer:bayer_scale=3",
     "-loop", "0", str(gif)],
    check=True, timeout=180,
)
for path in [video, gif] + [OUT / (name + ".png") for name in NAMES]:
    if path.stat().st_size == 0:
        raise RuntimeError(f"Empty demonstration media: {path}")
    print(f"Generated {path.relative_to(ROOT)} ({path.stat().st_size} bytes)")
