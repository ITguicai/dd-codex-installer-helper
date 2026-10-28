# DD Codex Installer Helper

Source for the Windows 2.1.17 helper, released under the [MIT License](LICENSE). SignPath Foundation signing is being prepared; it has not been approved. Existing releases are unsigned.

This independent utility installs/configures the official Codex Windows desktop client. It is not an official OpenAI application. Its interface discloses the default third-party API provider, https://api.dingdong.work/v1, and uses `gpt-6.1-sol` as the configured model.

Functions include Windows package detection and installation, API configuration, Simplified Chinese setup using resources already present in supported official clients, desktop shortcuts, optional Chinese-cache maintenance, targeted startup diagnostics and registration repair, organization-read diagnostics, an official CLI entry and shortcuts to download/configuration folders. Unknown language formats are refused rather than treated as successful.

See [privacy and data handling](PRIVACY.md), [uninstall instructions](UNINSTALL.md) and the [Code signing policy](CODE_SIGNING_POLICY.md).

## Download

Current unsigned Windows release: [2.1.17 ZIP](https://qnoss.dingdong.work/ddcodex/DDCodexInstallerHelper-v2.1.17-20261026.zip).

ZIP SHA256: `F2A9F0079633DE1DFC15525148338ED907F3CABD83FC7419410D8D4493F6B3F2`.

This historical download has not been signed by SignPath. Review the [Code signing policy](CODE_SIGNING_POLICY.md) for current signing status. A signature, once available, does not guarantee that antivirus or browser reputation systems will never block a release.

## Build

On Windows with .NET Framework 4 and Windows PowerShell:

```powershell
New-Item -ItemType Directory -Force .\out | Out-Null
.\src\BuildHelper.ps1 -OutputPath .\out\DDCodexInstallerHelper.exe
```

The build compiles the C# sources and embeds the local JavaScript/PowerShell resources. It does not install, launch or modify Codex, contact the API provider or require a real API key. The output is unsigned. The manual GitHub Actions workflow has the same scope; it must first be published and run in an owner-controlled repository before hosted-build provenance can be claimed.

No official Codex binaries, Node runtimes, customer configuration/authentication files, deployment credentials or local development history are included in this source candidate.
