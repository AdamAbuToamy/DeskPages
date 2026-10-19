# Code signing policy

Current preview status: **unsigned**. Copyright and CompanyName show Adam Abu Toamy, but those fields do not authenticate a publisher. No signing credentials are bundled. Never distribute a self-signed root or ask people to disable SmartScreen.

For a recognized publisher outside the Store, obtain a publicly trusted code-signing certificate after identity validation (a CA's supported individual/organization certificate, or an eligible managed signing service). The name shown in the Windows signature/UAC dialog comes from the certificate's validated identity, not a string chosen in the installer. Verify provider eligibility before paying. Current Microsoft Artifact Signing quickstart limits individual developers to US/Canada; eligible organizations include Israel and other listed countries. The author must complete validation; build scripts cannot do this for them.

On a Windows build machine with the trusted certificate and its signing key available in `Cert:\CurrentUser\My`:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File Installer\Build-Installer.ps1 -CertificateThumbprint YOUR_THUMBPRINT -RequireSignature

The build signs payload executables and libraries, then the containing installer, checks trust and timestamps, and finally verifies the exact embedded bytes. Do not modify anything after signing. Hardware/cloud signing providers must expose a compatible Windows certificate key provider; otherwise adapt the signing function to the provider's documented signing tool, then run the same release verification gate. Private keys must remain in the provider/token; never commit PFX files or secrets.

A trusted signature establishes a verified publisher. It does **not guarantee immediate SmartScreen reputation** for a new download. EV is not an instant bypass either. Store MSIX distribution is another route, but the current elevated startup/Explorer integration must be assessed and adapted before claiming Store compatibility; an EXE submitted to the Store still needs publisher signing.

Free open-source option: apply to SignPath Foundation after publishing a maintained, documented MIT project and building provenance. Acceptance is not automatic. Under their Foundation certificate the verified publisher is SignPath Foundation, while Adam remains the program author. Do not claim sponsorship before acceptance. A real integration and manual signing approval are still required.

Sources checked 18 October 2026:
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options
- https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart
- https://signpath.org/terms
