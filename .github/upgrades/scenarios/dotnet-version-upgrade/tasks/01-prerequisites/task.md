# 01-prerequisites: Validate SDK And Baseline

Validate the local .NET SDK/toolchain for net10.0 and ensure solution-level build inputs are aligned before modifying target frameworks. This includes confirming global SDK compatibility and capturing a clean build baseline that can be used to verify migration progress.

This task establishes a stable starting point for the all-at-once migration and prevents false failures caused by environment drift.

**Done when**: The repository has a validated .NET 10-capable SDK baseline, and a pre-upgrade solution build has been executed successfully on the working branch.
