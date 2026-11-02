### 2026-11-02: Admit serial lifecycle resources without relaxing its runner
**By:** Lead integration
**What:** Prepare the lifecycle project's SDK assets in a dedicated empty-feed
step, pass the explicit scratch `project.assets.json`, and set only this
workflow step to one mutation worker. Other worker settings, budgets,
permissions, triggers and all required controls remain unchanged.
**Why:** The global two-worker setting and an unprepared clean intermediate
directory contradicted this bounded graph's explicit single-worker/no-restore
contract. The exact additions reverse to the unchanged inherited workflow hash.

The declared captain inputs are preserved. Full queued M1 observations include
the original ball process's post-result LOS flag update using actual held-ball X
and side (`player.c:10458-10464`, `include/GAME.H:83`), not carrier origin.
Both receipt validation and runtime assertions enforce that exact predicate;
this is not a tolerant flag mask or a change to production behavior.

Actual baselines retain593/71 plus captain1778/72 (2371 assertions,143 cases).
All nine retained and four captain controls compile and fail their named
assertions. R2 compiled source, original input fixtures and all replay tapes stay
unchanged; no native-hardware equivalence is claimed.
