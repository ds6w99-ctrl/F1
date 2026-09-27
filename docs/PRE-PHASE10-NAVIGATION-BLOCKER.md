# Arabic navigation calibration: evidence and harness correction

## Root-cause finding and limits

The proven harness defect is a missing precondition: it looked for the realized
`NavMemoryTools` child without ensuring `NavPerformance` was expanded. Waiting
does not expand a collapsed navigation group. A controlled UIA collapse reproduces
the same missing-child assertion, including after English -> Arabic and returning
to Dashboard. No product modification was required to recover the child: calling
`ExpandCollapsePattern.Expand()` restores it.

**The earlier intermittent run did not record expansion state. Its initiating event
is not proven.** Natural diagnostic runs did not reproduce it. It would be incorrect
to claim language switching itself causes collapse: the captured runs show that
switching preserves both the expanded state and an explicitly collapsed state.

### Findings against the seven hypotheses

1. In the controlled collapsed case, Memory is actually hidden visually, not merely
   absent from UIA. The screenshot was opened and visually inspected.
2. In the expanded case, it is visible both in screenshots and the UIA tree.
3. Changing language alone did not collapse the parent in the observed runs.
4. Fresh-HWND and cached-root lookups agreed; RuntimeIds before/after language change
   remained identical. There is no evidence here of a rebuilt/stale navigation tree.
5. Lookup already uses `NavMemoryTools`, not localized Name. Name changes from Memory
   to Arabic while identity and ControlType.ListItem remain stable.
6. In the expanded case IsOffscreen=False, child rectangle `68,315,254,36`.
   In the collapsed case no child peer is returned, so child bounds/IsOffscreen are
   unavailable rather than falsely reported as offscreen. Parent remains visible.
7. Explicit parent expansion resolves the controlled missing-child failure.

## Evidence (gitignored local artifacts)

`artifacts/current/navigation-root-cause/` contains before/after PNG screenshots,
full UIA trees and navigation-only JSON. Every node records AutomationId, Name,
ControlType, IsOffscreen, BoundingRectangle, RuntimeId, parent id and expansion state.
Transient tree-snapshot errors are retained as records, not silently interpreted as
absent navigation elements.

- `run02/ar-before-language*`, `ar-after-language*`: expanded in both languages;
  Memory present; same RuntimeId. These screenshots were visually inspected.
- `controlled-collapse/ar-before-language*`, `ar-after-language*`,
  `ar-missing-child*`: collapsed before and after language change, Memory absent;
  test exit 1. The missing-child screenshot was visually inspected.
- `controlled-keyboard/`: the same explicit collapsed-state probe after the helper
  fix, exit 0. No longer depends on an already-expanded sidebar.

## Correction

`F1-NavigationHarness.ps1` resolves a fresh UIA root, identifies the parent by
AutomationId and ControlType, explicitly expands when collapsed, and waits for
Expanded plus a visible positive-size child with the correct parent ancestry.
Localized Name is only an additional assertion. The existing 20-second bound is
unchanged; 100ms sleeps are polling intervals, not fixed readiness delays.

The native calibration's previous mouse-coordinate heuristic was also unsuitable
for a no-guessing test: this WinUI composite exposes no reliable ClickablePoint and
the tested point's UIA hit-test returned only a Pane/Window chain. Rather than send
unchecked clicks, the harness now focuses the exact NavPerformance element,
verifies HasKeyboardFocus/foreground ownership, and sends native Enter. It then
asserts the localized Performance page title. **This is native keyboard activation,
not mouse hit-test coverage.** It does not claim the original mouse assertion passed.
Product UX is run unchanged; no product UX assertions were disabled.

## Regression matrix

`Test-F1NavigationTransitions.ps1`: **30/30 Memory found**, exit 0:

- English -> Arabic: 10/10.
- Arabic -> English: 10/10.
- Restart Arabic: 5/5.
- Restart English: 5/5.

Half the language transition cycles explicitly start from a collapsed group.
Each sample checks fixed identity, localized Name, parent identity, control type,
visibility and bounds; it then selects Memory and verifies its page/action.
Preference bytes are restored after each script. No soak or parallel tests.

Final calibration: **18/18**, including both languages and foreground prerequisites.
Product UX: **305/305**, exit 0, including Arabic and English page/title/active-nav
regressions across Light, Dark and System themes. `git diff --check`: exit 0.

UIA `TextFlowDirectionsAttribute` returns a COM sentinel rather than a typed value
from this WinUI text provider. Its automatic direction result is **NOT AVAILABLE**,
not PASS. `Test-F1NavigationLayout.ps1` records that limitation separately from
sidebar/localization assertions and captures English/Arabic Memory pages for direct
visual direction review. Both PNGs were opened and visually reviewed: English Memory
heading, controls and paragraphs run LTR; Arabic counterparts run RTL; Memory is visible
beneath expanded Performance and the sidebar stays on the left in both. This focused
visual direction check passes; it is not blanket visual approval of all pages.
The page was captured before metrics became available, so no telemetry claim is made.

Final evidence: `artifacts/current/navigation-final/{calibration,product-ux}`,
`artifacts/current/navigation-transitions/transitions.json`, and
`artifacts/current/navigation-layout/{layout.json,en-memory-layout.png,ar-memory-layout.png}`.

**PRE-PHASE10 VERIFICATION READY — WAITING FOR USER VISUAL REVIEW**
No Phase 10 or push. Visual acceptance of the product remains the user's decision.
