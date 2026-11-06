# Windows transport parity verification - 2026-11-06

6.1.0 Polaris preserves installed Northstar hotfix 6f2af1f and its transport layout, Battle.net game target and gameplay behavior. Adds the same Solo/Levelling/Dungeon/Raid/Tank preset row as Forever. Existing preset settings persist locally and cannot override the addon's authoritative restrictions. The old console and duplicate desktop configuration menus are not mounted. Status, Details, calibration, release notes and rolling log remain.

Automated: `dotnet test tests/MaxDpsCompanion.Tests -c Release --no-restore --filter 'FullyQualifiedName~ReleaseNotes|FullyQualifiedName~ReleaseIdentity|FullyQualifiedName~ConsolePreset|FullyQualifiedName~NorthstarVendor' --verbosity quiet`: 19 passed, 0 failed. Lua syntax checked for all bridge Lua files. Real `MaxDpsCompanion.exe --ui-smoke-test` checks visible transport/preset controls and persistence of all five bundles. Screenshots are retained in local build artifacts. Deployment backs up and hash-checks the installed companion and matching addon, preserving settings.

Static/automated desktop checks do not establish live Retail acceptance. No game launch, restart, focus, input or reload was performed. Full DPI/accessibility matrix and native macOS UI parity remain unverified.
