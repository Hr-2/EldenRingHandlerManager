# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.7] - 2026-08-21

### Changed
- Add auto-update feature (check GitHub releases, download, apply via batch helper)

## [1.0.6] - 2026-08-21

### Changed
- Resolve merge conflict in changelog

## [1.0.5] - 2026-08-21

### Added
- Open a mod's source folder in Explorer from the mods list.
- Open the Nucleus handlers folder in Explorer from the Handlers page.

### Changed
- Restore README emojis and polish

### Fixed
- Corrected changelog wording: enabling a mod on one tab disables mods on the other tab (not "engine").

## [1.0.4] - 2026-08-21

### Changed
- Clean up README: remove dashes, use 'I' not 'We'

## [1.0.3] - 2026-08-21

### Changed
- Fix release job dependency on bump outputs

## [1.0.2] - 2026-08-21

### Changed
- Restructure workflow: bump + build + release in one run

## [1.0.1] - 2026-08-21

### Changed
- Auto version bump workflow, improved README
- Add AI disclosure note to README
- Bump upload-artifact to latest v7.0.1

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
- GitHub Actions CI: auto-build and publish a "Latest build" release on every push.
