# Upgrade Options — PaDDY.sln

Assessment: 3 SDK-style net8.0-windows projects with significant API compatibility incidents and one recommended package upgrade.

## Strategy

### Upgrade Strategy
Three-project modern-to-modern upgrade with a shallow dependency graph; an atomic upgrade minimizes overhead.

| Value | Description |
|-------|-------------|
| **All-at-Once** (selected) | Upgrade all projects in a single coordinated pass and validate the full solution after changes. |
| Top-Down | Upgrade app projects first, then consolidate shared libraries in a second phase with temporary multi-targeting. |

## Project Structure

### Package Management
The solution is modern-to-modern and SDK-style, but it is small (3 projects) and does not require immediate CPM centralization.

| Value | Description |
|-------|-------------|
| **Per-Project (defer CPM to post-migration)** (selected) | Keep package versions in project files during migration; evaluate CPM after stabilization. |
| Central Package Management (CPM) | Introduce Directory.Packages.props and centralize package versions now. |

## Compatibility

### Unsupported API Handling
Assessment reports binary/source API incompatibilities; resolving them directly avoids introducing temporary stubs.

| Value | Description |
|-------|-------------|
| **Fix Inline** (selected) | Resolve API compatibility changes directly in the migration tasks, including complex changes. |
| Defer Complex Changes | Apply simple replacements now and defer complex API work using compile-safe stubs and follow-up tasks. |
