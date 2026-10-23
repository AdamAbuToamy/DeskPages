# DeskPages 0.8.32 · Trial

- Retains the 0.8.31 primary snapshot/cache changes and 0.8.30 Desktop restoration/secondary arrangement fixes.
- Installer packaging now accepts reviewed Git source plus explicitly named runtime outputs. Build objects, untracked scripts/binaries, CI workflows and unexpected generated files are excluded. Every payload file has a SHA-256 inventory. This packaging correction is not a confirmed explanation for the reported 0.8.31 Defender alert.
- New builds are scanned with updated Microsoft Defender, real-time protection enabled and cloud connectivity checked. Automatic sample submission stays disabled, respecting the user's instruction not to submit the executable to Microsoft. A successful isolated scan does not guarantee the user's browser/cloud verdict.
- The previous 0.8.31 setup was reported as Trojan:Win32/MalUri.A!cl on Windows 11. Its reviewed text files match the committed source, and its separate local-only Defender scan found no detection; the cause remains unconfirmed.

Trial only. Do not publish without user acceptance.
