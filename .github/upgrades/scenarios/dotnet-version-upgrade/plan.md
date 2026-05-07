# .NET Version Upgrade Plan

## Overview

**Target**: Upgrade the PaDDY solution from .NET 8 to .NET 10 on Windows TFMs.
**Scope**: 3 SDK-style projects (2 class libraries + 1 WPF app) with significant API compatibility churn and one package version update recommendation.

### Selected Strategy
**All-At-Once** - All projects upgraded simultaneously in a single operation.
**Rationale**: 3 projects, all on modern .NET, and a clear dependency structure (EffectProcessor <- AudioProcessor <- PaDDY) make an atomic upgrade practical.

## Tasks

### 01-prerequisites: Validate SDK And Baseline

Validate the local .NET SDK/toolchain for net10.0 and ensure solution-level build inputs are aligned before modifying target frameworks. This includes confirming global SDK compatibility and capturing a clean build baseline that can be used to verify migration progress.

This task establishes a stable starting point for the all-at-once migration and prevents false failures caused by environment drift.

**Done when**: The repository has a validated .NET 10-capable SDK baseline, and a pre-upgrade solution build has been executed successfully on the working branch.

---

### 02-upgrade-all-projects: Migrate Projects To Net10

Upgrade all project files in one coordinated pass by moving target frameworks to net10.0-windows, applying required package updates, and resolving compatibility-related compile issues surfaced by the assessment. Scope includes EffectProcessor, AudioProcessor, and the PaDDY WPF app together to preserve coherent dependency alignment.

Assessment signals indicate API compatibility issues concentrated in PaDDY and AudioProcessor, with one recommended package update for Microsoft.Data.Sqlite. This task includes the required code-level fixes to restore build compatibility under .NET 10.

**Done when**: All projects target net10.0-windows, package updates are applied as required, and the full solution builds successfully with zero warnings and zero errors.

---

### 03-final-validation: Run Full Verification

Execute full post-upgrade validation for the upgraded solution, including solution build/test checks and final review of deferred recommendations. This confirms that the atomic upgrade is complete and production-ready from a compile and regression perspective.

This task also records any post-upgrade follow-ups that are intentionally deferred (for example, optional future CPM centralization).

**Done when**: Build and tests complete successfully on the upgraded solution, and upgrade outcomes plus deferred recommendations are documented in task progress details.
