# 02-upgrade-all-projects: Migrate Projects To Net10

Upgrade all project files in one coordinated pass by moving target frameworks to net10.0-windows, applying required package updates, and resolving compatibility-related compile issues surfaced by the assessment. Scope includes EffectProcessor, AudioProcessor, and the PaDDY WPF app together to preserve coherent dependency alignment.

Assessment signals indicate API compatibility issues concentrated in PaDDY and AudioProcessor, with one recommended package update for Microsoft.Data.Sqlite. This task includes the required code-level fixes to restore build compatibility under .NET 10.

**Done when**: All projects target net10.0-windows, package updates are applied as required, and the full solution builds successfully with zero warnings and zero errors.
