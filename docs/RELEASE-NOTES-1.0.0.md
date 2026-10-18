# F1 1.0.0 — release notes

Unsigned Windows 11 x64 release. Installer (`installer/F1.iss`) and portable archive,
both described in `docs/INSTALL.md`.

## Version

- Assembly, About page, setup metadata and artifact names now report `1.0.0`
  (previously `0.9.0-dev`). No `-dev` label is displayed anywhere in the product UI.

## Cleaner policy (V1)

- Browser caches (Edge/Chrome/Firefox) and user Windows Error Reporting are unsupported
  in V1 and can no longer be scanned, selected or deleted. `WindowsCleanerTargets` no
  longer produces targets for them and `IsAllowed` rejects them outright (F1-002).
- All other supported Cleaner categories and their safety gates are unchanged.

## Uninstaller leftover scan (F1-003)

- The "touched directory" observation now requires proof of an actual deletion: a
  directory anchors a leftover review only when its last-write time moved **and** its
  direct entry count decreased between the pre/post-uninstall snapshots. Active caches
  (shader caches, browsers) rewrite or grow, so they can never flood the list again.
- Exactly two directory levels below the shared roots are observed (depth off-by-one fixed).
- Each unverified anchor is capped at 300 items and truncation is reported honestly;
  a truncated session still deletes the individually verified items it shows, so the
  delete button is no longer disabled by unrelated noise.
- The confirmation dialog and the truncated-result message now describe the actual
  behavior in both languages.

## Verified for this build

- Release solution build: 0 warnings, 0 errors.
- Cleaner runners: scanner/targets, executor, completion and scan-performance all pass.
- Unit/orchestration checks pass; compatibility checks pass on the build host.
- Product UX harness: 306/306 checks against the published `F1.exe` (en/ar, themes,
  navigation, About version copy, restart persistence).
- Portable payload verified byte-identical to the publication and launches/closes
  normally.

## Known limitations

- No code-signing certificate: SmartScreen may warn once per download
  (`docs/SIGNING.md`). The package is otherwise complete and runnable.
- Clean-machine, Standard User/UAC and full edition matrix validation remain deferred
  (`docs/GATES.md`, `docs/RELEASE-QA.md`).
- Diagnostic export, memory optimization no-op behavior and other documented scope
  limits of previous versions still apply.
