# Code signing policy — application preparation only

SignPath Foundation approval has not been granted. This project uses the MIT License. Existing customer releases are unsigned and must not be advertised as signed by SignPath Foundation. The GitHub Actions workflow builds unsigned artifacts only; signing will be configured after approval.

Proposed scope: sign only this project's own Windows `DDCodexInstallerHelper.exe`, built from reviewed repository source. Do not use the project's signing subscription to re-sign the official OpenAI client, its Node/native components, Microsoft programs or other upstream binaries.

The repository is maintained by [ITguicai](https://github.com/ITguicai), who controls repository changes and reviews. A signing approver has not yet been enrolled. Before application/submission, the maintainer must provide genuine project-reputation evidence and their application contact details, confirm MFA for repository and signing accounts, and enroll the required signing roles. Personal identity and MFA status are not inferred from the GitHub username.

Privacy and data handling: [PRIVACY.md](PRIVACY.md). Uninstall instructions: [UNINSTALL.md](UNINSTALL.md). Project contact: [GitHub issues](https://github.com/ITguicai/dd-codex-installer-helper/issues).

After provider acceptance, each signing release must retain the association to the reviewed source revision and automated build artifact and receive the required manual approval. The maintainer must verify the resulting Authenticode signature/trust/timestamp and publish the signed release hash. Signing is not a claim that antivirus software can never detect or block the application.

The provider requires acknowledgement text on accepted projects' public download/signing policy pages. Add the following only when the service has actually been approved and is being used:

> Free code signing provided by SignPath.io, certificate by SignPath Foundation.

References: https://signpath.org/terms.html and https://signpath.org/apply.html.
