## 0.8.8 preview — navigation feedback

- Acknowledge accepted page navigation before source layout verification/capture.
- Keep a click-through page HUD responsive on an independent STA while the agent works.
- Complete feedback only after both transaction success and renderer retirement; failures retire with an error cue.
- Widen the drag-edge cue from 3 px to 18 logical px, keeping hit zones, dwell timing, OLE data, renderer, page membership and saved layouts unchanged.
- Include feedback lifecycle/bridge tests in release validation.
- Preserve 0.8.7 public release assets; this update requires a separate preview build.

# 0.8.7 Alpha

- Keep DirectComposition animation on extended displays using independent monitor
  clips and local screen-width motion; no neighbour-screen pixels enter the slide.
- Add optional Main display only; secondary icon coordinates and visibility are
  shared view metadata, independent of permanent pins and file paths.
- Avoid capturing drag input at internal monitor boundaries.
- Add portable compositor/display-scope simulators, an opt-in native DWM simulator
  for one real screen, automated JSON test reports and Windows build/release CI.
- Refresh simple UI documentation, privacy wording, supported configurations,
  security reporting and reproducible prerelease assets/checksums.

# Changes

## 0.8.7 simplified Windows-style experience

- Replace the six-section dashboard with a small page switcher and an independent settings window. Remove search, rules, history and check dashboards from everyday UI. Preserve existing metadata.
- Use Windows 11-inspired neutral surfaces, Segoe UI, restrained blue accent, settings rows and toggles; request rounded system window corners when supported. Four basic toggles and language, with less-used options collapsed.
- Debounce automatic settings saving and atomically persist only validated preferences without a desktop visibility/layout transaction. Preserve advanced thresholds, system-pin state and other organization metadata.
- Provide a persistent manually saved layout separate from rolling history, and explicit restore that keeps newer files and current language/input preferences. Expose existing pin/unpin through the selected desktop icons tray menu.
- Add preference mapping, real file/membership/layout preservation, failed-write/invalid-destination, manual restore and selected-pin tests. Replace old dashboard UI fixtures with the compact-page/settings fixture. Windows device acceptance is not claimed.

## 0.8.5 workspace design refresh

- Replace native workspace tab headers with managed layout and six accessible section buttons. Mouse, keyboard arrows, focus and drag-over navigation share the same selected-panel state. No native tab item sizing or recreation on resize.
- Align rounded input fields and actions; rebuild page, search, recovery, rules, settings and checks layouts with clear section titles, consistent spacing and readable empty states. Keep page actions under More and remove the redundant large preview banner; page cards retain saved-layout previews.
- Restyle data tables with flat headers, taller alternating rows and selected-row colors; compact local history timestamps and translate known history operation labels. Preserve item IDs and backend command arguments.
- Use responsive settings cards and a persistent Save changes action. Preserve visibility-first deferred loading, membership-only card counts and the existing desktop/drag/compositor behavior.
- Exercise every section button and 60 resize/section/timer steps with seven opens per en/ar/he under Mono/Xvfb; inspect captured screens of all six sections. This is isolated UI evidence, not native Windows certification.

## 0.8.4 workspace opening reliability update

- Remove the Resize-to-ItemSize feedback path and custom tab painting; use native tab sizing. Keep the page cards and other theme controls.
- Set inherited UI font before building controls and suspend the form layout until its control tree is complete.
- Show the workspace before scheduling data refresh; coalesce repeated refresh requests and dispose the refresh timer with the form. Guard ordinary opening errors and log opening/data/error phases. Native process faults cannot be caught by this guard.
- Count tracked memberships in one pass without probing Desktop/cloud paths during page listing. Counts describe tracked membership, not an availability scan.
- Add a real-message-loop isolated UI harness and a production 10k/99-page no-filesystem-probe regression; include a read-only Windows event/log diagnostic.
- Reported Windows 0xC0000005 is NOT reproduced/certified fixed on the Linux host. Mono/Xvfb layout/paint/reopen/resize testing is separate evidence; Windows reproduction and crash-module evidence remain necessary if the fault persists.

## 0.8.3 interface and edge-slide update

- Modernize the compact workspace with an integrated brand header, rounded action buttons, native-state toggle switches, adaptive section strip, blue layout tiles and readable selected/current/archived/locked page cards with item counts. Apply a shared theme to the quick panel. Preserve en/ar/he, native keyboard focus and DPI scaling.
- Reverse only the edge-drag motion argument: dragging to the left exposes the left page while the outgoing page slides right, and vice versa. Keep destination order, wrapping, drag/drop placement, compositor and input timing unchanged.
- Add a production-entry-point regression for both edges, custom order and wrapping; extend Windows-only card binding, toggle and multilingual rendering checks.
- Include a feature-by-feature Arabic manual test guide. Windows raster, live OLE and physical animation acceptance are not claimed on the Linux build host.

## 0.8.2 PowerShell encoding hotfix

- Save every shipped PowerShell script as UTF-8 with BOM for Windows PowerShell 5.1.
- Keep the bootstrap compatibility probe ASCII-only; construct Unicode test strings from numeric code points.
- Validate actual script bytes in the compiled installer before starting PowerShell: reject Unicode without BOM, UTF-16, invalid UTF-8 and empty scripts.
- Add byte-level regression tests, including reproduction of the exact faulty 0.8.1 test file on the build host. Verify all scripts inside the final embedded installer payload.
- No desktop layout, file membership, compositor or OLE behavior changes.

## 0.8.1 compatibility hotfix

- Replace the array-valued generic PowerShell callback cast with a named C# delegate, in both the production agent and Windows UI fixture.
- Use explicit NotePropertyName/NotePropertyValue when initializing workspace metadata.
- Add a read-only gate that runs the actual Windows PowerShell 5.1 parser across the package and exercises the exact production delegate assignment, including array/Unicode/empty bindings.
- Require this native gate in Windows installer/build preflight before startup or Desktop changes. Cross-host runs explicitly report SKIP for the unavailable Windows 5.1 parser.
- Reproduce and reject the original reported cast in the compatibility guard; rebuild every managed runtime/installer binary and source fingerprint.
- Desktop membership, compositor, OLE handling and page layouts are unchanged by this fix.

## 0.8.0 engineering preview

- Add a compact six-section workspace with English, Arabic and Hebrew/RTL controls, keyboard access, scrollable review dialogs and schematic saved-layout page cards.
- Add cross-page managed-file search/reveal, item pinning, selected search-result transfers, page-card reorder, archive/restore, layout locks and protected empty-page deletion.
- Add durable metadata transactions, recoverable layout/manifest journal, 100-step undo/redo, manual organization checkpoints and recovery previews; keep new/replaced files visible on Main when restoring older metadata.
- Record completed Explorer layout changes and edge transfers in organization history. Defer edge-drop membership persistence until position/identity checks finish.
- Add reviewed extension/name rules, configurable incoming-file page and nonblocking input thresholds/edge dwell settings. Preserve original file paths and file contents.
- Add per-display-configuration saved layouts and collision-aware bounds conversion. Multi-monitor page switching falls back to verified non-animated switching.
- Add an independent Rescue EXE/script that does not start Agent/compositor/input, preserves page metadata and restores original system-icon settings.
- Keep the existing native compositor and nonblocking OLE lifecycle; no new synchronous wait/message pump in Drop.
- Add real-process crash tests at four workspace stages, real-file content-hash stress, 10k metadata search, 100k synthetic input samples, 10k malformed IPC packets, Windows UI/RTL suite and an opt-in empty-account real Explorer acceptance harness.
- Windows device/reboot/OLE/frame-pacing acceptance and trusted public signing are not claimed. See Release/ACCEPTANCE.md.

## 0.7.8 preview

- Retain the white edge cue through page preparation and the actual renderer lifetime; dismiss after the matching navigation's SETTLED signal, not when the command is sent or acknowledged.
- Publish bounded, request-ID-scoped visual state only when it changes. The host observes the real compositor visibility before its animation early return. Sharing contention retries without blocking input.
- Keep the cue as a separate window above the transition and below applications. Capture remains the original Explorer HWND; no cue is composited into captured page images. No frame compositor or placement behavior changed.
- Avoid immediately re-showing the cue on the latched edge after completion. Cancellation, errors and process exit still dismiss it.
- Retain nonblocking OLE Drop; no nested message pump, wait or Shell description writes added.
- Regression fixture covers command acknowledgement versus visual completion, matching/stale request IDs and retry after a blocked visual-state write. Actual Windows cue z-order and animation appearance require device acceptance.

## 0.7.7 preview

- Correct the device-reported 0.7.6 drag regression by restoring the user-confirmed 0.7.5 physical Drop callback and Shell image lifecycle. Remove the nested OLE wait/message pump and repeated Shell drop-description writes. No synchronous commit wait remains in the Drop callback.
- Restore the exact 0.7.5 source position capture, original grab offset and rigid-group placement/verification components. The page compositor binary is unchanged.
- Retain the separate white edge cue and 48px/320ms approach policy with pre-approval dwell and 80px rearm hysteresis. Cue painting uses no commit wait, and transport polling returns to 40ms.
- Native Windows Move text is restored with the original Shell feedback. Text suppression is not included in this correction.
- Existing group, native-result rollback, IPC contention, page safety and recovery fixtures pass on the build host. Actual Windows responsiveness and drop placement require device acceptance.

## 0.7.6 preview (superseded)

Introduced a nested physical-drop acknowledgement wait and Shell description suppression alongside earlier edge recognition and a white cue. User reported hanging, slowness and incorrect drop placement. 0.8.7 removes the wait and description suppression, retaining only the separate edge feedback improvements.

## 0.7.5 preview

- Preserve the physical spatial arrangement of a dragged selection. Capture keyed native source coordinates and the original mouse-down point before the first page transition; keep them across subsequent page switches.
- Replace independent per-file cell allocation with one rigid-group translation near the actual drop point. Retain the grab offset, respect the native grid origin and monitor bounds, and move the entire group together when avoiding existing icons. Refuse insufficient space instead of repacking the selection.
- Verify native resulting relative positions before committing membership. A distorted group, moved stationary icon, overlap or failed refresh rolls back through the existing journal safety path.
- Add rectangular/scrambled selection, hotspot, grid, collision, negative-monitor, edge, insufficient-space and 300 seeded placement cases; replay production source capture across two page transitions and reject a distorted native result.
- Retain the 0.7.4 drag transport fix and existing native compositor binary. This change addresses spatial order; it does not claim a new or Windows-verified visual drop handoff.

## 0.7.4 preview

- Fix device-reported drag transport contention: readers now share read/write/delete access, so atomic packet replacement can coexist with open Windows readers. Transient IO contention defers a timer tick instead of cancelling a physical OLE drag.
- Retain an unsent request for retry; never delete a peer request after a read failure. Malformed packets are rejected without racing a valid replacement.
- Retain the exact acknowledgement after a command completes. Retry only that acknowledgement, never redispatch an already-completed page navigation/drop. A transport timeout stops the optional broker instead of leaving an indefinitely pending cancel loop.
- Add cancellation reasons and broker-ready version to data-only diagnostics.
- New production-mailbox fixture reproduces an exclusive file lock, blocked acknowledgement write and concurrent cross-process atomic writes/shared reads. Full source regression suite passes on the build host. Windows-specific rename semantics and physical OLE acceptance still require device verification.
- Animation/compositor and drop layout mechanics are unchanged from 0.7.3.

## 0.7.3 preview

- Fix a reproduced drop rollback when Explorer rounds requested icon positions to its grid. Read the native Snap-to-grid preference, allocate distinct lattice cells only when enabled, accept bounded native rounding and save actual resulting coordinates.
- Preserve free placement when grid alignment is off. Verify existing target icons stayed put, reject overlapping dropped icons and recheck original file identities before committing membership.
- Record data-only drag request, response and commit/rollback diagnostics, without file names or contents.
- Keep the 0.7.2 native compositor binary unchanged. The submitted phone video shows the slide and carried icons; the remaining symptom is an empty target page after release. The grid rejection is reproduced with a mock Explorer adapter; the device log is still needed to confirm its exact failing branch.
- Add snapped/free placement, wrong position, stationary icon mutation and collapsed-cell regression cases. Actual Windows OLE drop acceptance remains required.

## 0.7.2 preview

- Forward real OLE drag-image events to the Windows Shell IDropTargetHelper, retaining the original data object across strip/canvas handoff and ignoring a late leave from the old target.
- Allow source pixel validation through the verified in-package drag broker's bounded-alpha surface. Check owner process marker, exact broker executable path, layered constant-alpha mode and alpha 1..8. Other application windows are still excluded. Actual image capture remains the original Explorer window only.
- Keep the existing 380 ms quintic animation, target paint/count fences, page membership commit-on-drop and no-filesystem-move semantics.
- Added drag image lifecycle and capture eligibility regression fixtures. Real Windows visual acceptance remains required.

## 0.7.1 preview

Startup hotfix for the device-reported 0.7.0 failure:
- Launch the normal-user broker using the original desktop Document.Application Shell object.
- Normalize PowerShell null/empty/invalid session tokens to disabled.
- Treat optional drag-mailbox initialization failure as nonfatal; core pages still start.
- Log actual host readiness after the event loop begins instead of calling desktop preparation "ready".
- Regression fixture replays the production startup block with both successful and failed Shell launch and actual PowerShell string binding.

## 0.7.0 preview

- Compact tray control panel with Pages, Settings and About, keyboard access and Escape to close.
- Brief, semi-transparent page-number HUD after successful page switches; fades out, does not take focus or receive clicks, hides before capture; optional in Settings.
- Drag physical desktop files/shortcuts to a horizontal edge, dwell 550 ms, then drop onto the next page without moving file paths. Normal-user OLE broker, validated metadata-only transfer, multiple-file placement, cancellation and rollback. Requires real Windows acceptance before stable publication.
- Named pages with validated Unicode names and atomic local persistence.
- Metadata-only settings/layout export with no overwrite of existing backups and no export during unfinished transitions.
- Cleaner installer copy, privacy and license access, author attribution and success confirmation.
- Privacy notice, MIT license, third-party notices, security guidance, source build and publication/signing checklist.
- Public release gate requires trusted, timestamped signatures on installer and embedded PE files.
- Existing desktop capture/compositor and page transition mechanics retained.

## 0.6.10

Validated startup guardian build and package preflight; rejected empty/truncated executables, incorrect architecture, CLR and assembly identity/version before modifying the installed version.
