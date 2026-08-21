# Elden Ring Handler Manager

A Windows tool for managing your Elden Ring [Nucleus Co-Op](https://splitscreen.me) setup across two mod engines: **Mod Engine 2** (legacy, for mods like Elden Vins) and **Mod Engine 3** (modern).

It builds and maintains the Nucleus handler folder for you, so you don't have to hand-edit configs or copy mod files around manually.

## Features

- **Two engine tabs (ME2 / ME3)**. Keep your mods organized per engine. Enabling a mod on one tab auto-disables the other tab's mods, so you can never accidentally mix engines.
- **Drag & drop mods**. Drop folders or `.dll` files onto a tab. They're auto-detected (folder mod vs DLL mod) and named for you.
- **Mod tree**. Expand mods to inspect their DLLs and sub-folders, toggle individual parts, and mark folders as real mods with a simple star.
- **Profiles**. Save, load, and delete named mod configurations. Optional auto-save keeps your active profile in sync as you change mods.
- **One-click Deploy**. Pick **ME2** or **ME3**; the app copies the base handler, drops in your enabled mods, and writes `config_eldenring.toml` (ME2) or `me3.toml` (ME3).
- **Live progress**. Large mods (7GB+) deploy in the background with a progress bar, byte counter, and a **Cancel** button.
- **Custom handler `.js`**. Big mods (like Elden Vins) that ship their own handler can override the deployed one automatically.
- **Manual backup**. Save a copy of the currently deployed handler whenever you want.

## Requirements

- **Windows 10 or 11**
- **[Nucleus Co-Op](https://splitscreen.me)** installed
- **Unofficial Elden Ring handlers by vlxst.**, which are required. They're shared in the **Nucleus Co-Op Discord server**, in the **"WIP Handler + Testing" forum**, in **vlxst.'s Elden Ring thread**. You'll need both:
  - the **ME2 UNOFFICIAL HANDLER** (vlxst.)
  - the **ME3 UNOFFICIAL HANDLER** (vlxst.)
- **Mod Engine 2** (for ME2 mods) and/or **Mod Engine 3** (for ME3 mods)
- **[.NET 6 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/6.0)**. The app is built for `net6.0-windows`.

## Getting started

1. Download the latest build from the [Releases](https://github.com/Hr-2/EldenRingHandlerManager/releases) page.
2. Extract the zip and run `EldenRingHandlerManager.exe`.
3. Go to **Handlers** and set:
   - Your Nucleus handlers folder (e.g. `D:\nucleus\handlers`)
   - The **ME2 UNOFFICIAL HANDLER (vlxst.)** base folder
   - The **ME3 UNOFFICIAL HANDLER (vlxst.)** base folder
4. Go to **Mods**, pick the **ME2** or **ME3** tab, and drop your mod folders / DLLs in.
5. Go to **Deploy** and hit **Deploy ME2** or **Deploy ME3**.

## How it works

The app only touches the Nucleus handlers folder, never your game files. Deploy:

1. Copies the base handler (`.js` + folder) into `handlers\Elden Ring`.
2. Drops enabled mods into `ModEngine\`.
3. Writes the config file (`config_eldenring.toml` / `me3.toml`) when auto-configure is on.
4. Uses a mod's custom handler `.js` if one is set.

## Development

```powershell
dotnet build EldenRingHandlerManager.csproj
```

The repo uses a GitHub Actions workflow (`.github/workflows/build-release.yml`). Every push to `master` bumps the patch version, updates `CHANGELOG.md` from commit messages, tags it (e.g. `v1.1.0`), builds the `.exe`, and publishes a versioned release with changelog notes.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full version history.

## License

This project is provided as-is for personal use.

## Credits / AI disclosure

This project was built with the assistance of an AI coding assistant (opencode, powered by an LLM). I designed, iterated on, and verified everything with the AI writing much of the code, and I reviewed, tested, and directed each step.

I'm open about it: if you're wondering whether AI was used, yes it was. I'd rather say it plainly than have anyone guess.
