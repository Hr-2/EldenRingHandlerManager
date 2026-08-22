# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0] - 2026-08-22

### Added
- Search box to filter mods by name.
- Expand all / collapse all buttons for the mod tree.
- Per-mod on-disk size shown in the mod list.
- Right-click a mod to copy it to the other engine (ME2 &harr; ME3).
- Import and export profiles as `.json` files.
- Deploy ETA shown next to the progress bar.
- Deploy and backup logs are now written to `%APPDATA%\ERHandlerManager\deploy.log`.
- A default profile is created automatically, so there is always at least one profile.

### Changed
- "+ Add mod" now uses the active engine tab instead of always creating ME3 mods.
- Duplicate mod names get an automatic suffix (e.g. `Mod (2)`) instead of silently overwriting the deployed folder.
- Generated config files only reference DLLs that will actually be copied (disabled subfolders are excluded).
- The deploy confirm dialog now clearly warns that the current handler is replaced and not backed up.
- Cancelling a deploy removes the partial handler so Nucleus never runs a broken build.
- Mod load order can now be adjusted with the move up/down buttons.
- Expanded/collapsed tree state is saved and restored between sessions.
- Settings saves are debounced to avoid writing to disk on every toggle.
- Full mod path shown as a tooltip when it is truncated.
- Window, page, and engine-tab state are remembered between sessions.
- Safer settings file writes (temp file + swap) so a crash can't corrupt your mods.

### Fixed
- Closing the window during a deploy no longer crashes.
- Removed unused code.

## [1.0.8] - 2026-08-21

### Changed
- Add quality-of-life improvements: safe names, accurate deploy progress, atomic settings save, UI state persistence, deploy confirm, open deployed folder.

## [1.0.7] - 2026-08-21

### Changed
- Add auto-update feature (check GitHub releases, download, apply via batch helper).

## [1.0.5] - 2026-08-21

### Added
- Open a mod's source folder in Explorer from the mods list.
- Open the Nucleus handlers folder in Explorer from the Handlers page.

### Changed
- Restore README emojis and polish.

### Fixed
- Corrected changelog wording: enabling a mod on one tab disables mods on the other tab (not "engine").

## [1.0.0] - 2026-08-21

### Added
- Initial release of the Elden Ring Handler Manager.
- Manage two Nucleus Co-Op handlers: Mod Engine 2 (legacy) and Mod Engine 3.
- Drag & drop mods (folders and DLLs) with automatic detection and tree view.
- Toggle individual DLLs and sub-folders; mark folders as mods with the star.
- Separate ME2 / ME3 tabs; enabling a mod on one tab auto-disables mods on the other.
- Profiles: save, load, and delete named mod configurations with auto-save.
- Auto-configure toggle for the generated `config_eldenring.toml` / `me3.toml`.
- Custom handler `.js` override per mod (e.g. Elden Vins).
- Background deploy with live progress bar, byte counter, and cancel support.
- Manual backup of the current deployed handler.
- GitHub Actions CI: auto-build and publish a release on every code push.
