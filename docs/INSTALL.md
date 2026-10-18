# Installing F1

F1 is a self-contained Windows 11 x64 application. Nothing else has to be installed
first: the .NET runtime and the Windows App SDK travel inside the payload, and the
application does not use WebView2, so there is no external runtime dependency to resolve.

## Requirements

- Windows 11, build **22631** (23H2) or later, x64.
- Home, Pro, Enterprise or Education. Server and other non-client editions are refused
  twice: by the installer, and by the application's own `CompatibilityRules`.
- Windows 10 is **not** supported. The floor is `CompatibilityRules.MinimumBuild`, a
  technical requirement of the WinUI 3 / App SDK surface F1 builds on.

## Path A — the installer (recommended)

`artifacts/` is not tracked by Git, so build the artifacts locally first:

```
powershell -File scripts/Build.ps1
powershell -File scripts/Build-Installer.ps1 -Compiler <ISCC.exe 7.1.0>
```

That yields `artifacts/installer/F1-1.0.0-win-x64-setup.exe`.

What it does:

- Installs **per machine** to `%ProgramFiles%\F1`. The wizard's directory page is
  available, and `/DIR="..."` is honoured — the product spec deliberately does not pin a
  fixed location (the phase-0 fixture in `installer/SystemCore.Phase0.iss` still does;
  they are different artifacts with different AppIds).
- Places the application at `App\F1.exe` and the elevated helper at
  `App\ElevatedHost\SystemCore.ElevatedHost.exe`. **That nesting is a contract**, not a
  convention: `HelperLaunchLease` resolves the helper from `AppContext.BaseDirectory`,
  and `SecurityTests`, `CompatibilityTests` and `docs/CLEANER-PROTOCOL.md` all pin it. A
  helper placed beside `App` instead of inside it is never found, which silently disables
  elevated leftover deletion.
- Adds a Start-menu shortcut, an optional desktop shortcut, and an **Apps & features**
  entry named `F1 1.0.0`.
- Copies `THIRD-PARTY-NOTICES` and the dependency licences to `Notices\`.
- Requires administrator rights, because it writes to Program Files.
- Does **not** start anything elevated. F1 itself runs as the calling user (`asInvoker`);
  it requests elevation only when you ask it to remove leftovers outside your profile.

Silent install:

```
F1-1.0.0-win-x64-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

Uninstall from **Apps & features**, or run
`%ProgramFiles%\F1\Uninstall\unins000.exe`. User state under `%LOCALAPPDATA%\SystemCore`
(preferences, cache, startup state) is intentionally left alone by the uninstaller.

## Path B — the portable archive (no install, no admin)

```
powershell -File scripts/Build-Portable.ps1
```

Produces `artifacts/portable/F1-1.0.0-win-x64-portable.zip`. Extract it anywhere —
a USB stick is fine — and run `F1.exe`. Same nested layout, no registration, and no
administrator rights needed to start. F1 still asks for elevation only for leftover
deletion outside your profile.

Use this path on locked-down or shared machines where you cannot install to Program
Files. It shares the same `%LOCALAPPDATA%\SystemCore` state as an installed copy.

## Both paths are unsigned

There is no code-signing certificate. On another machine, a downloaded copy triggers
SmartScreen's "Windows protected your PC"; choose **More info**, then **Run anyway**. That
is a one-time, per-download warning, not a failure, and the application itself performs no
signature check of its own — an unsigned build runs normally. Real signing, the expected
publisher identity and post-sign verification are tracked in `docs/SIGNING.md` and remain
deferred release work.

## What is verified, and what is not

`scripts/Test-Shell.ps1` drives the real UI through UI Automation and asserts 118 checks.
Run against the **installed** artifact (`%ProgramFiles%\F1\App\F1.exe`) it reported
118 passed, 0 failed — including a real Cleaner scan and a relaunch with saved
preferences. The work-host install/uninstall lifecycle and the artifact hashes are
recorded in `installer/DECISION.md`.

That is work-host evidence. It does **not** stand in for a clean machine without SDKs, a
genuine Standard User account, real UAC accept/deny prompts, or a signed payload. Those
rows in `docs/RELEASE-QA.md` stay DEFERRED until disposable VMs and a certificate exist.
