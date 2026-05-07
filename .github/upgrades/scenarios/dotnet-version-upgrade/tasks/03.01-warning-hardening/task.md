# 03.01-warning-hardening: Remediate vendor warning debt

# 03.01-warning-hardening: Remediate vendor warning debt

## Objective
Reduce and, where practical, eliminate compiler/analyzer warnings in vendored AudioProcessor upstream code introduced or surfaced under net10.0-windows.

## Scope
- AudioProcessor/vendors/** sources implicated by current build warnings
- Keep functional behavior unchanged; prioritize safe refactors and API modernizations

## Steps
1. Capture warning inventory from non-incremental build
2. Apply targeted code fixes by warning family
3. Rebuild iteratively to confirm warning count reduction

**Done when**: Warning count is materially reduced with no new errors and all touched projects still build.

## Scope Inventory

- Project affected: AudioProcessor/AudioProcessor.csproj
- Primary area: vendored upstream sources under AudioProcessor/vendors/**
- Baseline warning count: 34 (non-incremental build)

## Warning Families Identified

- Obsolete APIs/serialization: CS0672, SYSLIB0014, SYSLIB0021, SYSLIB0051, SYSLIB0006, CS0618
- Nullability annotations and nullable value flow: CS8622, CS8629, CS8605, CS8607, CS8597
- Analyzer quality issues: CA2200, CA2265, CA2022
- Logic/type checks: CS0472

## Planned Remediation Strategy

1. Apply safe API modernizations (MD5.Create, HttpClient, obsolete-member handling).
2. Fix nullability mismatches and nullable value access in hot warning files.
3. Address analyzer-pattern fixes (throw semantics, span null checks, exact reads).
4. Rebuild and iterate until warning reduction plateaus or reaches zero.
