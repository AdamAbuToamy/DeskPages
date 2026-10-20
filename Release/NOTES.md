# DeskPages 0.8.12 Alpha — redesigned settings and hidden-items support

A new settings window with a web-app-inspired sidebar, four sections (General, Navigation, Appearance, Pages), grouped cards, blue switches and clearer typography. Native RTL parent mirroring, nested tables and automatic layout scaling are absent from the new settings form. One owner places every element in physical pixels from a single UI scale. Native combo boxes remain keyboard-accessible, and real buttons/toggles retain accessible names and descriptions.

Settings requests, debounced preference saving, the quick tour, layout save/restore and display-scope rollback are retained. The successful 0.8.10 Hidden/System visibility code, page transactions and saved layouts remain unchanged.

Local isolated evidence: 96 real form/section renderings, 240 rows, EN/AR/HE, explicit 100/125/150/200% geometry and normal/narrow windows; text rendering, control bounds, preference payload, replay/save actions and rejected scope rollback. The existing separate pages/settings auto-save fixture is also run. Rendering uses Mono/Xvfb; screenshots show actual controls with fixture data. Physical Windows monitor/DPI transitions and actual installer execution remain acceptance checks.

This Alpha is unsigned. Download the EXE from Releases after successful Windows validation and publication. Ordinary Hidden items may stay enabled; keep protected operating system files hidden. Use the updated Show all/Rescue before downgrading to versions before the hidden-items fix.
