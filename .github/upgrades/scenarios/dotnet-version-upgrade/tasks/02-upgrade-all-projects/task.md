# 02-upgrade-all-projects: Migrate Projects To Net10

Upgrade all project files in one coordinated pass by moving target frameworks to net10.0-windows, applying required package updates, and resolving compatibility-related compile issues surfaced by the assessment. Scope includes EffectProcessor, AudioProcessor, and the PaDDY WPF app together to preserve coherent dependency alignment.

Assessment signals indicate API compatibility issues concentrated in PaDDY and AudioProcessor, with one recommended package update for Microsoft.Data.Sqlite. This task includes the required code-level fixes to restore build compatibility under .NET 10.

**Done when**: All projects target net10.0-windows, package updates are applied as required, and the full solution builds successfully with zero warnings and zero errors.

## Scope Inventory

- Projects affected: EffectProcessor/EffectProcessor.csproj, AudioProcessor/AudioProcessor.csproj, PaDDY.csproj
- Distinct concerns:
	- Retarget all projects from net8.0-windows to net10.0-windows
	- Apply recommended package update for Microsoft.Data.Sqlite in PaDDY.csproj
	- Resolve compile-level issues surfaced after retargeting
- Change signals from assessment:
	- PaDDY.csproj: 3526 issues (Project.0002, NuGet.0002, Api.0001/0002/0003), WPF-heavy impact
	- AudioProcessor/AudioProcessor.csproj: 534 issues (Project.0002, Api.0001/0002/0003), WinForms/System.Drawing and legacy crypto usage in vendored code
	- EffectProcessor/EffectProcessor.csproj: 1 issue (Project.0002 only)

## Research Findings

- Current TFM definitions are project-local in each csproj; no Directory.Build.props override was found.
- Package action required by assessment:
	- PaDDY.csproj: Microsoft.Data.Sqlite 8.0.15 -> 10.0.7
- Existing baseline warning debt predates this task (notably vendored AudioProcessor code and two nullability warnings in Services/ProcessLoopbackCapture.cs).
- Project dependency direction remains: PaDDY -> AudioProcessor -> EffectProcessor.
