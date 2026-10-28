# Uninstall and rollback instructions

These are reviewable instructions, not an uninstall script. They do not run automatically.

1. In the helper, right-click the “启用离线中文” button and choose “关闭中文自动检查” to remove its current-user login-maintenance registration. Confirm the operation in the helper log. This disables future automatic repair; it preserves the chosen language, existing cache and configuration.
2. Remove the desktop shortcut created by this helper if you no longer use it. Verify its target belongs to `%LOCALAPPDATA%\DDCodexInstallerHelper` before removing a shortcut; preserve unrelated same-name shortcuts.
3. Once no helper maintenance/diagnostic process is using it, remove only the `%LOCALAPPDATA%\DDCodexInstallerHelper` directory if you want to remove the helper's local launcher, copied public runtime, icons and diagnostics. The source/downloaded helper EXE and its ZIP may also be removed. Do not delete `%LOCALAPPDATA%` itself.
4. The official Codex client is a separate installed product. If desired, uninstall it through Windows Settings → Apps → Installed apps. Removing this helper does not automatically uninstall Codex.
5. To stop using the configured API provider, review the `.codex` configuration/authentication and the current-user `OPENAI_API_KEY` environment variable. Preserve session data and any credentials you still use. Revoke an unwanted key with its issuing provider. Do not delete the whole `.codex` directory as a shortcut for removing this helper.
6. Language preference/cache and backup files remain after maintenance is disabled. Change language through the official client when available. If restoring a backup, fully exit Codex first, verify the backup belongs to the correct profile and preserve the current state before restoring. Ask for targeted help if unsure; never delete a live LevelDB/LOCK file to force access.

If the helper itself can no longer run, the user's Windows administrator can inspect the specific `DDCodexChineseMaintenance` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` and remove that one verified helper-owned value. Do not remove the entire Run key or disable antivirus/system security services.
