# Android Delivery Tools

These PowerShell 7 scripts support the sole `production` Android path.

| Script | Purpose |
| --- | --- |
| `Test-AndroidRelease.ps1` | Validates the one-profile contract, package, live API URL, EAS Update/native runtime agreement, Android architecture agreement, type-check, lint, Node tests, Expo config, and tooling self-test. |
| `Register-NodeTestAssets.cjs` | Supplies inert static-image modules so Node can execute pure mobile logic tests that share a module with React Native artwork. |
| `Test-AndroidTooling.ps1` | Exercises EAS JSON parsing, APK retrieval, archive validation, checksum, and manifest generation using an isolated synthetic artifact. |
| `Invoke-AndroidBuild.ps1` | Verifies EAS access, starts and waits for the production cloud build, downloads the APK, and removes the temporary response containing its signed URL. |
| `Save-EasBuildArtifact.ps1` | Validates an EAS build response and writes the APK, SHA-256 file, and redacted metadata manifest. |
| `Install-AndroidApk.ps1` | Selects an authorized phone and installs or upgrades the APK with `adb install -r`; it can launch the package afterward. |

Generated artifacts live under ignored `local-builds`. EAS response JSON is
deleted after packaging because its artifact URL may be temporary and signed.
Keystores, passwords, Expo tokens, provider secrets, and user data must never be
added to this folder.

The GitHub workflow invokes the same check/build helpers used locally. A mobile
push produces a GitHub Actions artifact; an API-only push does not rebuild an
unchanged binary. When one push contains both kinds of work, Android delivery
waits for the tested migration/API deployment and live health check.
