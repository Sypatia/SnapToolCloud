# Owner policy: local CI and deployment-only Actions (2026-11-18)

This owner instruction supersedes older hosted-CI instructions in this repository.
Before every push, run all existing applicable CI, test, lint, type-check and
build commands locally on the exact source being pushed, after the final edits
and any rebase. Do not bypass hooks with `--no-verify`, fabricate successful
results, or treat blocked, skipped or failed checks as passes; report them
explicitly. Keep existing tests and independent evidence intact.

GitHub Actions may run deployment workflows only. Do not add or retain test,
CI, benchmark, cache-maintenance or pull-request-bot workflows, including manual
or reusable workflows. Deployment-specific builds, guards and readiness checks
are allowed as part of a deployment. Apply this policy to future workflows.
This policy change does not authorize any deployment.
