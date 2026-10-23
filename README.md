# Pet Tracker

A small overlay for **AION 2** that shows your pet ranks and how many souls you still need while you farm.

**[Download Pet Tracker for Windows](https://github.com/0Fox2Give/PetTracker/releases/download/updates/PetTracker.Desktop-win-stable-Setup.exe)**

Public beta · Windows x64 · Version 0.1.57

**Open source under GPLv3.** The application, capture worker, updater, tests and build scripts are here. [Build from source](docs/BUILDING.md) · [License](LICENSE)

[Setup and troubleshooting](SUPPORT.md) · [What's new](CHANGELOG.md) · [Report a problem](https://github.com/0Fox2Give/PetTracker/issues/new?template=bug_report.md)

## New in 0.1.57

- **Merchant status stays visible.** Settings → Tracking now shows whether shop
  stock was received even after your collection has synced.
- **More useful support reports.** Reports include collection sync and merchant
  stock counts, without your pet list, character names or packet data.

This update helps diagnose missing merchant recommendations. It does not fix
the reports still under investigation.

## Farming tools from 0.1.56

- **Pet details and maps in one window.** Open Find a pet, select a pet, then switch between **Details** and **Map**. Both use the same search and filters. Verteron and Altgard maps work offline, with pan, zoom and spawn markers.
- **Session totals.** **This session** shows observed soul gains and pets ranked up. Reset it without changing your collection.
- **Farming goals.** Choose a target rank in a pet's details to see souls remaining and receive a completion alert through your notification settings.
- **Saved filters.** Save up to ten searches and filter combinations. **Hide maxed** is now available in the shared rank filter.
- **Compact rows.** **Settings → Overlay → Compact pet rows** makes the tracker take up less space.

[Farming tools guide](docs/FARMING.md). Map dots are approximate spawn locations, not live mobs or your current position. Map alignment still needs in-game testing.

Already installed? Open **Settings → Updates**. After updating, return to character selection and log back in with Pet Tracker running. Your saved collection, settings and pins are preserved.

## What it does

- Shows recent soul gains and progress toward the next rank, with family tags and matching crystal icons.
- Lists nearby pet mobs the tracker has detected, with an option to hide maxed pets.
- Lets you search pets or mobs, filter your collection, check known sources, and open a farming map. Hover a pet row to see souls remaining when its progress is known.
- Lets you pin several pets in a stable order. Choose a limit from one to five in Settings → Tracking (three by default).
- Shows a Black Cloud Merchant shopping list for chest, merchant and dungeon pets you have not maxed, with rank, soul progress and price from recently observed offers. Open the shop after your collection synchronizes.

Nearby lists mobs detected in game traffic; it doesn't have a fixed detection radius. The Map tab includes Verteron and Altgard; other regions use external source links. There is no player-position dot, walking arrow or distance display.

## Install

1. If Pet Tracker is already running, quit it from the Windows tray.
2. Install [Npcap from its official website](https://npcap.com/#download) if it is not already installed. **Npcap is required for live tracking and is not included with Pet Tracker.** If capture is unavailable, the app opens connection help with a link to the official Npcap installer.
3. Download and run the Pet Tracker installer.
4. Open Pet Tracker, then log into your AION 2 character. Your first collection is created automatically.

A normal launch shows the tracker even if you hid it last time. If you enabled **Only show over Aion 2**, bring the game to the foreground.

Already in game? Return to character selection and log back in with the tracker open. Collection sync happens automatically at character login; there is no separate sync button. If ranks or totals are still missing, check **Settings → Help** for capture status and troubleshooting.

## Updates and feedback

Check **Settings → Updates** for new versions.

If something looks wrong, [report it here](https://github.com/0Fox2Give/PetTracker/issues/new?template=bug_report.md). Include your tracker version, region, and the pet or mob name. For a missing soul gain, mention whether the game showed a soul reward message.

[Troubleshooting, alts and privacy](SUPPORT.md) · [Release notes](https://github.com/0Fox2Give/PetTracker/releases/tag/updates)

## Known issues

- Built-in map locations use approximate calibration and still need in-game alignment checks. They do not distinguish separate floors.
- Some first unlocks can remain on **Rank increase pending**. Relogging with the tracker running reloads the collection.
- Some merchant lists remain empty or disappear after a purchase. These reports are still under investigation; 0.1.57 adds diagnostics.
- The Linux client is experimental. Game capture under Wine/Proton and overlay placement on KDE/Wayland still need live testing.

The capture changes have been tested in game, but they don't guarantee that every missing soul or sync issue is resolved. If totals pause again, include a support report from **Settings → Help** with your bug report.

## Before you use it

Pet Tracker is unofficial and has no approval from NC/NCSOFT. Using third-party tools can put your game account at risk. The Windows installer is unsigned and may trigger a security warning.

Your collection stays on your PC; the app doesn't automatically upload it. Updates come from GitHub. The source is available for inspection and local builds; source availability is not a security audit.

Read the [legal notice](LEGAL.md) and [third-party notices](THIRD-PARTY-NOTICES.txt) before testing. Game names and artwork belong to their respective owners.

## About this repository

An experimental [Linux source build](docs/LINUX.md) is available for development
and compatibility testing. Live AION 2/Wine support has not been verified; the
download above remains the Windows release.

This repository contains the full application source and official downloads. Contributions are welcome; maintainers review changes and control official releases.

Use the installer above to run the app. **Code → Download ZIP** downloads source for building it yourself. Older release-tag archives may predate source publication; [source-0.1.51](https://github.com/0Fox2Give/PetTracker/tree/source-0.1.51) is the initial full source snapshot. Versioned installers remain available for earlier releases.

| Looking for | Where to go |
| --- | --- |
| Install Pet Tracker | [Windows installer](https://github.com/0Fox2Give/PetTracker/releases/download/updates/PetTracker.Desktop-win-stable-Setup.exe) |
| Missing ranks, setup, alts or privacy | [Help](SUPPORT.md) |
| Changes and known issues | [Release notes](CHANGELOG.md) |
| Build and test | [Developer instructions](docs/BUILDING.md) |
| Update tools and repository structure | [Maintainer guide](docs/MAINTAINING.md) |
| Component licenses | [Third-party notices](THIRD-PARTY-NOTICES.txt) and [licenses](licenses/) |
