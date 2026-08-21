# Elden Ring Handler Manager

A Windows tool for managing your Elden Ring [Nucleus Co-Op](https://splitscreen.me) handlers and mods across two mod engines — **Mod Engine 2** (legacy, for mods like Elden Vins) and **Mod Engine 3** (modern).

It builds and maintains the Nucleus handler folder so you don't have to hand-edit configs or copy files manually.

## Features

- **Two engine tabs (ME2 / ME3)** — keep mods organized per engine. Enabling a mod on one tab auto-disables the other tab's mods so you can never mix them.
- **Drag & drop mods** — drop folders or `.dll` files onto a tab; they're auto-detected (folder mod vs DLL mod) and named for you.
- **Mod tree** — expand mods to see their DLLs and sub-folders, toggle each individually, and mark folders as actual mods with the star.
- **Profiles** — save/load named mod configurations. Auto-save keeps the active profile in sync.
- **One-click Deploy** — pick ME2 or ME3; the app copies the base handler, drops in enabled mods, and writes `config_eldenring.toml` (ME2) or `me3.toml` (ME3).
- **Live progress** — large mods deploy in the background with a progress bar, byte counter, and a Cancel button.
- **Custom handler `.js`** — big mods (e.g. Elden Vins) that ship their own handler can override the deployed one.
- **Backup** — save a copy of the currently deployed handler.

## Requirements

- Windows 10/11
- [Nucleus Co-Op](https://splitscreen.me) installed
- Mod Engine 2 (for ME2 mods) and/or Mod Engine 3 (for ME3 mods)
- [.NET 6 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/6.0) (the app is built for `net6.0-windows`)

## Getting started

1. Download the latest build from [Releases](https://github.com/Hr-2/EldenRingHandlerManager/releases).
2. Extract the zip and run `EldenRingHandlerManager.exe`.
3. In **Handlers**, set your Nucleus handlers folder (e.g. `D:\nucleus\handlers`) and the two base handler templates:
   - ME2 UNOFFICIAL HANDLER (vlxst.)
   - ME3 UNOFFICIAL HANDLER (vlxst.)
   > Handlers can be found in vlxst.'s Elden Ring thread on the Nucleus Co-Op Discord server.
4. In **Mods**, switch to the ME2 or ME3 tab and drop your mod folders / DLLs in.
5. Go to **Deploy** and hit **Deploy ME2** or **Deploy ME3**.

## How it works

The app only touches the Nucleus handlers folder — no game files. Deploy:

1. Copies the base handler (`.js` + folder) into `handlers\Elden Ring`.
2. Drops enabled mods into `ModEngine\`.
3. Writes the config file (`config_eldenring.toml` / `me3.toml`) when auto-configure is on.
4. Uses a mod's custom handler `.js` if set.

## Development

```powershell
dotnet build EldenRingHandlerManager.csproj
```

The repo has a GitHub Actions workflow (`.github/workflows/build-release.yml`) that builds the app on every push and publishes a "Latest build" release. Tag a release (e.g. `v1.0.0`) for a versioned release with changelog notes.

## License

This project is provided as-is for personal use.
