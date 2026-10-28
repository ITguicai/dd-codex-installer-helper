# Privacy and data handling

This document describes the Windows 2.1.17 helper source published on 2026-10-28. Project contact: [GitHub issues](https://github.com/ITguicai/dd-codex-installer-helper/issues). Do not post credentials or personal data there. This document does not claim that a signing provider has approved the program.

## Network operations

- On installation/configuration requested by the user, the helper validates the user-entered API key with `GET https://api.dingdong.work/v1/models`, sending that key as a Bearer credential. This is a third-party provider, not OpenAI's API endpoint. Opening the registration link navigates to that provider's signup site.
- Downloading the official client uses `https://persistent.oaistatic.com/codex-app-prod/ChatGPT-x64.msix`. The fallback invokes Microsoft's Windows Package Manager/Store service and is subject to its normal service behavior/terms.
- The configured official Codex client later connects to the selected API provider according to its configuration. Plugins and official background/CLI operations can use the official client's ordinary authentication and network behavior.
- The helper source contains no developer WeCom webhook or automatic upload of task-completion reports. Those local development tools are not part of this repository or customer EXE.

Third-party services have their own data practices. See [OpenAI's privacy policy](https://openai.com/policies/privacy-policy/) and [Microsoft's privacy statement](https://privacy.microsoft.com/privacystatement). The default DD API provider is operated separately; consult its registration site for applicable terms and privacy information before supplying a key. No reviewed standalone DD API privacy-policy URL has been verified for this signing application yet.

## Local data and changes

Requested configuration writes can update the current user's `.codex/config.toml`, `.codex/auth.json` and `OPENAI_API_KEY` user environment variable. Keys are intentionally stored locally for the official client to use; this is sensitive information, and configuration backups may also contain it. The helper preserves backups for supported changes. Some operations respect `CODEX_HOME`; the main API configuration routine currently targets the default user `.codex` directory.

Offline Chinese setup reads supported client code/resources, preserves per-profile startup identity, backs up an existing local database and changes the language feature-cache records. A separate language preference is saved in the desktop configuration. It does not download a translation pack or copy another customer's identity/cache.

Language maintenance copies a helper launcher to `%LOCALAPPDATA%\DDCodexInstallerHelper\chinese-maintenance` and registers `DDCodexChineseMaintenance` under the current-user Windows Run key. It checks once at login and before opening through the helper-created desktop shortcut. The UI provides a command to disable this maintenance. The helper does not run as a persistent resident service.

Requested diagnostics inspect limited package/process/system-event and client-log information. The helper's diagnostic modules aim to report allowlisted status/category summaries instead of raw credentials, prompts, complete process command lines or raw client logs. Visible installation subprocess output and API error responses are separate output paths; users should still review all logs before sharing.

The organization probe starts one new official backend with package context, sends initialization and configuration/organization-read requests, and stops that test backend. The helper does not write authentication/configuration in that probe, but the official backend can normally log, update caches or refresh existing authentication. Its sanitized result is stored in the helper's `organization-probes` directory. This is not a model request or chat creation.

The customer can copy diagnostic logs to the clipboard and choose to share them. Do not send API keys, `auth.json`, configuration backups or session contents to the maintainer as troubleshooting attachments.
