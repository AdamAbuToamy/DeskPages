# 0.8.16 build validation scope

Built on Linux for Windows 11 x64 with MinGW-w64 and Roslyn against .NET Framework 4.8 reference assemblies. No experimental source or performance claim is published to public GitHub.

Portable checks: wheel duplicate/inertia handling, 1,000 short successive wheel gestures, 1,000 HID contact sequences without explicit lift reports, HID packet framing, snapshot-key identity and cache LRU/epoch/preview lifecycle. Managed/native builds and package PE/CLR, source fingerprint and embedded-payload hash are verified.

The encrypted-store fixture is compiled for Windows. It is not executed here. Physical Windows DPAPI, compression, file open/delete protection, restart retention, desktop invalidation, hardware touchpad timing, Explorer rendering and resource consumption require Windows acceptance. No real Windows CPU/RAM/latency figure is claimed.
