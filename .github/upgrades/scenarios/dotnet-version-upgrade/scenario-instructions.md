# .NET Version Upgrade

## Upgrade Options
**Source**: .github/upgrades/scenarios/dotnet-version-upgrade/upgrade-options.md

### Strategy
- Upgrade Strategy: All-at-Once

### Project Structure
- Package Management: Per-Project (defer CPM to post-migration)

### Compatibility
- Unsupported API Handling: Fix Inline

## Preferences
- **Flow Mode**: Automatic
- **Target Framework**: net10.0

## Strategy
**Selected**: All-At-Once
**Rationale**: 3 modern SDK-style net8.0-windows projects with shallow dependency depth are well-suited to a single coordinated upgrade pass.

### Execution Constraints
- Upgrade all projects in one atomic pass without tier-based sequencing.
- Perform project file updates before package updates, then restore dependencies.
- Fix compilation issues in a single bounded stabilization pass.
- Run full solution build and test validation only after the atomic upgrade task completes.
- Keep deferred recommendations limited to post-upgrade cleanup (for example CPM adoption).

## Source Control
- **Source Branch**: dotNet10
- **Working Branch**: upgrade-dotnet-10
- **Commit Strategy**: Single Commit at End
