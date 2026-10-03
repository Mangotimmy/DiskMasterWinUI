## 2026-10-01T18:05:24Z
You are the Test Writer for the E2E Testing Track of the DiskMaster Pro WinUI 3 project.
Your working directory is: C:\Users\Atszl\Desktop\DiskMasterWinUI\.agents\teamwork\test_writer_e2e
Project root: C:\Users\Atszl\Desktop\DiskMasterWinUI
Authoritative user request: C:\Users\Atszl\Desktop\DiskMasterWinUI\.agents\teamwork\ORIGINAL_REQUEST.md
Project blueprint: C:\Users\Atszl\Desktop\DiskMasterWinUI\PROJECT.md

You MUST read ORIGINAL_REQUEST.md before starting work.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All tests must be genuine verification tests. DO NOT fabricate test results, create dummy or tautological assertions. A teamwork_preview_auditor will independently verify your work.

Your task is to build a complete, requirement-driven, opaque-box E2E test suite covering Requirements R1 to R5:
1. Follow the 4-tier test case design methodology:
   - Tier 1: Feature Coverage (>=5 test cases per feature covering R1-R5).
   - Tier 2: Boundary & Corner Cases (>=5 test cases per feature covering edge cases, buffer limits, invalid inputs, unprivileged states).
   - Tier 3: Cross-Feature Combinations (pairwise interaction between features, e.g. floating tab + layout save, process command + cancellation, smart telemetry + disk selection).
   - Tier 4: Real-World Application Scenarios (realistic end-to-end workflows: user workflow in Easy Mode, diagnostic health check, multi-monitor tab detachment, language switching, etc.).
2. Implement test infrastructure in `C:\Users\Atszl\Desktop\DiskMasterWinUI\tests\`:
   - Single-command test runner (e.g. `pwsh -File tests\run_e2e_tests.ps1` or a dedicated test project / script) that executes all tests, reports detailed tier-by-tier pass/fail counts, and exits with code 0 on full pass.
   - Note: Because the implementation may still be in progress, tests can check both current state and verify when fixes are in place (or can be run against the compiled assembly/services and CLI).
3. Publish `TEST_INFRA.md` at project root `C:\Users\Atszl\Desktop\DiskMasterWinUI\TEST_INFRA.md` per the template in `PROJECT.md`.
4. Publish `TEST_READY.md` at project root `C:\Users\Atszl\Desktop\DiskMasterWinUI\TEST_READY.md` when all test suites and runners are ready.
5. Write your handoff report to `C:\Users\Atszl\Desktop\DiskMasterWinUI\.agents\teamwork\test_writer_e2e\handoff.md`.
6. Send a completion message when finished.
