# DeskPages 0.8.7 acceptance contract

Status: engineering preview. Cross-compilation and fixture PASS are not Windows-device certification.

## Executable suites

Run `Validate.cmd` from the installed directory. It runs in child processes to
isolate fixture types and writes a transcript under LocalAppData/DeskPages/Verification.
It uses temporary test files, not the user's actual Desktop. `SKIP` means untested,
never passed. The default stress case uses 500 generated files and 400 seeded
durable operations, plus undo/redo, independent content hashes and four process-death
stages. The 10,000-item search test measures metadata search on the executing host,
not Windows frame rate or GUI response latency. Input pressure is synthetic.

For real Explorer integration use a NEW disposable Windows VM/account with an
empty Desktop and empty Public Desktop, and no existing DeskPages profile:

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File Tests\Test-WindowsShellAcceptance.ps1 -DisposableDesktop -Cycles 200
```

This opt-in test refuses pre-existing physical items/profile, creates only its own
12 sentinel files, checks actual Explorer COM/NTFS visibility and layout through
four pages, pinning, undo, and hashes, then removes only its exact generated files.
Run it as administrator in the VM. It does not run the production host and cannot
prove OLE dragging, compositor frame pacing, real reboot or touchpad behavior.

## Device release gate — NOT run on the Linux build host

| Scenario | Required evidence | Current status |
| --- | --- | --- |
| Windows 11 24H2 and 25H2 x64 clean installation | Installer completion + ready host + no previous config | Not run |
| Upgrade from 0.7.8 with three populated pages | Membership, system flags, file IDs/hashes, saved coordinates retained | Not run |
| Sign out, restart, Fast Startup, Explorer restart | Starts on Main; visibility/layout verified; no mixed first usable frame | Not run |
| Forced agent termination at every transition stage | Correct committed state restored after actual Windows process restart | Fixture coverage only |
| Forced VM power-off and interrupted upgrade | Sentinel hashes and metadata recovery; successful or clearly blocked upgrade | Not run |
| Physical two-finger swipes on at least two drivers | Contact/wheel diagnostics + one navigation per gesture + no app interception | Not run |
| OLE edge drag, multi-select, Escape, rapid reversal | Carried drag visual retained, relative positions retained, cue stays through slide | Prior logic fixtures pass; new device run required |
| DWM animation and app windows | Present/frame-time trace and visual check; no open app captured or source-page leak | Not run |
| Monitor plug/unplug, negative origins, mixed scaling | Correct profile selected, no overlap/offscreen placement | Geometry fixtures only |
| 100/125/150/200/250% UI in English/Arabic/Hebrew | Windows raster/UI suite + keyboard and screen-reader check | Suite provided; not run |
| OneDrive Desktop and online-only placeholders | Sync completed, no content/path changes and correct exclusions | Not run; placeholders excluded |
| Non-admin account, elevation cancellation, security software | Clear failure before unsafe changes; validated helper privilege split | Policy fixtures only |
| Delete/archive/undo/restore/uninstall | No file content operation; original system settings restored; layout verified | Fixture coverage; device run required |

## Explicit boundaries

- Windows 11 x64 is the build target. Windows 10 and native ARM64 are not certified.
- Extended displays use one clipped subtree per monitor, each moving by its own
  width. Primary-only mode keeps secondary-icon visibility and coordinates shared
  across pages. Internal monitor boundaries are excluded from edge navigation.
  These paths have portable simulation coverage; native hardware remains unverified.
- Lock restores saved positions after a completed change; it is not a filesystem
  permission or a replacement for Windows access controls.
- Incoming-file assignment waits for a safe idle checkpoint/finished label edit;
  it does not interrupt Explorer's New-file rename interaction.
- Archives/undo/recovery contain metadata only. Actual deleted file content needs
  an external file backup. Newer files are retained on Main when restoring older
  organization. Windows file IDs distinguish a replaced file at the same path.
- 100 undo steps and 200 recent recovery records are retained, plus currently
  reachable redo records. Older metadata can expire; user files never expire.
- If there is not enough screen space, group placement/profile adaptation refuses
  the operation instead of claiming success with overlapping/missing icons.
- Stable-release signing/device acceptance remain outstanding. This explicitly
  labeled unsigned Alpha may be shared for limited testing. The EXE is unsigned. No security setting or certificate trust store is bypassed.

No finite test suite proves compatibility with all Windows builds, display
drivers, shells, cloud clients, policies or future updates. Publish supported
configurations only after recording the evidence above for the exact release.

## Automated display simulators

`Test-MonitorCompositor.ps1` compiles the exact production MonitorPlan.h plus easing
math into a portable software compositor test: 1006 geometries, both directions,
all/primary modes, 121 frame samples and atlas-source isolation checks. It runs in
Validate/CI when a C++17 compiler exists; installed users need no compiler.
`Test-MonitorScope.ps1` uses production visibility/layout policy and real temporary
file hashes with a simulated Explorer geometry adapter, including negative
coordinates, five pixel scales, durable reload, unplug/replug and scope disable.
`Test-MonitorPolicy.ps1` checks external/internal edges and Primary-only rejection.
`Simulate-Monitors.cmd` runs the same DWM visual tree on synthetic monitor regions
inside a single real display. It is opt-in and requires interactive Windows/GPU;
a Linux PASS is never substituted for this test. It does not change display modes.

`Release/Start-SandboxAcceptance.ps1` can launch the disposable real-Explorer suite
in Windows Sandbox, mapping source read-only and only a dedicated reports folder
writable, with networking/clipboard disabled. It requires Sandbox already enabled.
Power loss/reboot/hardware acceptance still need a persistent disposable Windows VM.
GitHub Windows CI automates builds, parser, file-attribute and isolated UI/logic
suites; hosted runners do not certify physical touchpads, real logon or GPU pacing.


## 0.8.10 hidden-items acceptance

- Enable Explorer Show > Hidden items; keep Hide protected operating system files enabled. Ordinary user-hidden files should appear, while other pages' managed files/folders do not. Switch repeatedly using keys, touchpad and edge drag, including pinned and secondary-monitor icons.
- Upgrade with hidden 0.8.9 files, restart Explorer and reboot. Verify current membership and icon positions, then Show all, Rescue and uninstall. Confirm app-added System bits are removed and original System/ReadOnly bits remain.
- Enable protected operating system files temporarily: verify the actionable switching error and that Explorer preferences are not rewritten. Restore the protected-files setting afterward.
- Inject a second-file attribute failure and a process termination after ownership persistence but before page commit. Verify exact Hidden/System rollback and fresh-process recovery, including uncommitted drag membership.
