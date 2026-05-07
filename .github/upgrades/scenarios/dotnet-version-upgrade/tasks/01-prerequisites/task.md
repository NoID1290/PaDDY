# 01-prerequisites: Validate SDK And Baseline

Validate the local .NET SDK/toolchain for net10.0 and ensure solution-level build inputs are aligned before modifying target frameworks. This includes confirming global SDK compatibility and capturing a clean build baseline that can be used to verify migration progress.

This task establishes a stable starting point for the all-at-once migration and prevents false failures caused by environment drift.

**Done when**: The repository has a validated .NET 10-capable SDK baseline, and a pre-upgrade solution build has been executed successfully on the working branch.

## Scope Inventory

- Projects affected: PaDDY.csproj (solution-level baseline build includes AudioProcessor and EffectProcessor dependencies)
- Distinct concerns: SDK readiness, global.json compatibility, baseline build and test execution
- Change signals: No code or project file changes required for prerequisites

## Research Findings

- .NET SDK validation: net10.0-compatible SDK is installed (`Compatible SDK found`).
- global.json validation: no global.json file exists in repository; no SDK pinning conflicts.
- Baseline build: `dotnet build PaDDY.csproj` succeeded on `upgrade-dotnet-10` with 26 warnings and 0 errors.
- Baseline tests: `dotnet test PaDDY.sln` ran successfully (no failing tests reported).
