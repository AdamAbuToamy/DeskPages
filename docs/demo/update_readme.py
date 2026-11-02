#!/usr/bin/env python3
"""Maintain the README demo section without changing the release download block."""
from pathlib import Path

root=Path(__file__).resolve().parents[2]
readme=root/'README.md'
text=readme.read_text(encoding='utf-8-sig')
start='<!-- deskpages-demo:start -->'
end='<!-- deskpages-demo:end -->'
block="""<!-- deskpages-demo:start -->
## See DeskPages in action

[![DeskPages interactive desktop pages and taskbar groups walkthrough — click for MP4](docs/media/DeskPages-Preview.gif)](docs/media/DeskPages-Simulation.mp4)

[**Watch the MP4 walkthrough**](docs/media/DeskPages-Simulation.mp4) · [**Explore the interactive Windows-style simulator**](docs/demo/index.html)

> **Demonstration, not native footage:** The video and screenshots above were generated from an interactive Windows-style **UI simulation** to explain DeskPages' core idea. They are not recordings of DeskPages controlling Windows Explorer. The native Windows app is provided separately in the installer under Download.

| Desktop pages | Taskbar groups |
| --- | --- |
| ![Simulated desktop page selector](docs/media/02-pages-panel.png) | ![Simulated taskbar grouping menu](docs/media/05-group-menu.png) |

The simulation walks through four independent desktop pages, numbered groups for open apps, adding applications to groups, and deleting a group. The actual Windows app still requires validation on a physical Windows 11 desktop.
<!-- deskpages-demo:end -->"""
if start in text and end in text:
    left=text.split(start,1)[0]
    right=text.split(end,1)[1]
    text=left+block+right
else:
    marker='<!-- deskpages-download:end -->'
    if marker not in text:
        raise RuntimeError('README Download section anchor missing')
    text=text.replace(marker,marker+'\n\n'+block,1)
readme.write_text(text,encoding='utf-8')
print('README video and screenshot links installed')
