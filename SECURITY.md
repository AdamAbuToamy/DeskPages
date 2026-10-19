# Security

0.8.7 is a preview. Do not post private file paths, desktop captures, logs, account identifiers, certificates or signing keys in a public issue. The program uses elevated access for shared Desktop files and startup management. This makes a protected installation directory and review of all shipped executable/scripts important.

The publishing script enables GitHub private vulnerability reporting before dispatching the prerelease build. Once enabled, use the repository Security tab → Report a vulnerability. This package has no configured private support endpoint; do not invent or use an unverified contact.

We aim to investigate reports for the latest release. There is no promised response SLA. Do not describe a source hash as an authenticity guarantee: an unsigned package's hashes detect accidental corruption, not a publisher identity. Distribute releases from the official repository only, sign artifacts when a trusted certificate is available, and verify signatures after download.
