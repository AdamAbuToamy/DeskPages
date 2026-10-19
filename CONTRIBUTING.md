# Contributing

Open an issue describing a reproducible problem and Windows build, number of screens, display scaling and touchpad model. Redact paths and names in logs. For code changes, make a focused pull request and run the applicable fixtures.

Never change hidden attributes, membership or saved layouts from UI code. Dispatch operations on the existing agent thread. Preserve crash recovery and deletion guards. Do not replace the compositor when changing panel or installer UI. A passing model test is not a physical reboot, Windows shell or smoothness test. Contributions use this repository's MIT license; keep attribution and third-party notices.
