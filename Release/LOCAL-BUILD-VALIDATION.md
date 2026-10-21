# 0.8.16 build validation scope

Built on Linux for Windows 11 x64 with MinGW-w64 and Roslyn against .NET Framework 4.8 reference assemblies. This records the local cross-build scope before publication. Public Windows CI validation is reported separately in GitHub Actions; no performance claim is established.

Portable checks: wheel duplicate/inertia handling, 1,000 short successive wheel gestures, 1,000 HID contact sequences without explicit lift reports, HID packet framing, snapshot-key identity and cache LRU/epoch/preview lifecycle. Managed/native builds and package PE/CLR, source fingerprint and embedded-payload hash are verified.

The encrypted-store fixture is compiled for Windows. It is not executed here. Physical Windows DPAPI, compression, file open/delete protection, restart retention, desktop invalidation, hardware touchpad timing, Explorer rendering and resource consumption require Windows acceptance. No real Windows CPU/RAM/latency figure is claimed.

## Windows CI publication check

[GitHub Actions run 39595262996](https://github.com/AdamAbuToamy/DeskPages/actions/runs/39595262996) builds the published 0.8.16 installer with MSVC and .NET Framework on Windows Server 2022. The build/isolated acceptance step passed, including the actual Windows snapshot-store fixture (compression, DPAPI, sharing locks, restart restoration and corrupted-record rejection) and Windows PowerShell 5.1 parsing. This does not certify physical Windows 11 desktop animation, hardware touchpad behavior, or CPU/RAM/latency measurements. Consult the linked run for the final stress-validation and publication status.
