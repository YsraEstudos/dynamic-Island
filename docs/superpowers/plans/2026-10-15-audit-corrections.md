# Audit corrections

Goal: resolve the verified defects in the reviewed audit without changing the portable distribution or losing user data.

Design: apply individual setting changes to current settings; reset only preferences exposed by Settings while retaining phone credentials. Prepare updates before freezing note editing and flushing; launch only after successful flush. Isolate coordinator subscribers with observable errors. Preserve unreadable stores, quarantine corruption, and share durable atomic writes without sharing recovery policy. Keep calculator operands separate from display formatting. Extract filesystem adapters and common Win32 mechanics without merging feature policies. Serialize real clipboard tests. Keep reusable views reloadable.

Constraints: preserve existing dirty files, public behavior and persisted schemas. No commits, publishing, installation, dependency upgrades, or changes to real user credentials/data. Native execution; review-only delegation at completion. Hypotheses require evidence, not speculative changes.

Tasks (tests first for behavior changes):
- [x] Settings regression tests, current-state commits, scoped reset, normalization.
- [x] Update preparation/launch protocol, final-save/edit gate, repository and SHA-256 verification.
- [x] Coordinator subscriber isolation, ordered delivery and diagnostics.
- [x] Durable JSON helper, budget recovery, move counter/schedule filesystem code to Windows.
- [x] Calculator small-value formatting without changing pending arithmetic.
- [x] Common window/process interop, MVVM consolidation, reusable-view cleanup, mouse-hook isolation.
- [x] Clipboard isolation and checked writes; credential storage protection compatible with plaintext import.
- [x] Targeted checks, full solution tests, build, independent review and final scope ledger.

Review focus: failed loads must never enable destructive writes; failed final save must never launch an installer; repeated update requests must not shut down early; percentage operations must retain their pending left operand; unloaded/reloaded widgets must still receive changes. Media extrapolation must preserve timing contracts.

Verification: dotnet test DynamicIsland.slnx --no-restore --maxcpucount:1 --verbosity minimal; dotnet build DynamicIsland.slnx --no-restore --verbosity minimal; git diff --check. Record actual results and limitations below.

Progress: implementation, complete solution verification, and independent review complete. The user subsequently authorized committing, pushing and publishing the next release (v0.4.28) on main.

Evidence (2026-10-15): the first six regression tests failed on the original behavior, then passed. Additional failing regressions demonstrated plaintext credentials, destructive counter fallback, blocked calculator entry after a tiny generated operand, and hidden media progress notifications. Final full suite: 1124 Core + 376 Windows tests passed (1500 total). Final solution build succeeded, 0 errors. Existing compilation warnings observed during rebuild: QuickCaptureWindow Margin/Content hiding inherited members and xUnit2029 in RingBufferTests. No dependency upgrades, commits, publication, real installer launch or access to real credentials.

Rulings: keep default state-equivalence semantics in the coordinator; suppress only hidden timeline dispatch in the presenter while retaining receipt timestamps. Keep existing portable unsigned distribution; verify GitHub digest when supplied and retain legacy releases without it. The alleged WPF shutdown/window-ownership defect was refuted, so no indiscriminate window closure was added. DPAPI changes are tested against temporary fake credentials; moving to another Windows account requires importing credentials again. JSON mechanics are shared, failure policy remains per store. Unknown real-player timeline behavior and a genuine installed-version update/relaunch still require external validation; no claim of full desktop E2E coverage is made.


Independent review: four important follow-ups were found and corrected. Nine targeted failures reproduced before the fix pass: reset of the phone-block preference, replacement of an unreadable DPAPI token, and seven invalid JSON structures. Read errors still block unsafe writes, but explicit credential replacement can recover a token copied from another account. Save-error recovery reopens notes after editing is re-enabled. The reviewer confirmed all four issues addressed, with no important issue remaining in that re-review. The final Angry guard is rechecked after saving. Final logs: `.superpowers/sdd/2026-10-15-audit-corrections/verified-tests.log` and `verified-build.log` (ignored local diagnostics).

Release preparation: exclude .claude/ and .superpowers/ from automatic staging, keep the smoke-test helper hidden, rerun all tests and use release.ps1 for publish, startup smoke test, ZIP, commit, tag, push and GitHub Release.
