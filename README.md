# Pet Tracker

A small overlay for **AION 2** that shows your pet ranks and how many souls you still need while you farm.

**[Download Pet Tracker for Windows](https://github.com/0Fox2Give/PetTracker/releases/download/updates/PetTracker.Desktop-win-stable-Setup.exe)**

Public beta · Windows x64 · Version 0.1.49

**The app is currently closed-source.** This repository hosts downloads, documentation and update tooling; it does not contain the full Pet Tracker application source.

[Setup and troubleshooting](SUPPORT.md) · [What's new](CHANGELOG.md) · [Report a problem](https://github.com/0Fox2Give/PetTracker/issues/new?template=bug_report.md)

Use the installer linked above. GitHub's **Code → Download ZIP** and **Source code** downloads contain this repository, not the installed app. The other release assets support automatic updates and older versions.

## What it does

- Shows recent soul gains and progress toward the next rank, with family tags and matching crystal icons.
- Lists nearby pet mobs the tracker has detected, with an option to hide maxed pets.
- Lets you search for a pet, check its known sources, and open a farming map. Hover a pet row to see souls remaining when its progress is known.
- Lets you pin a pet so it stays at the top of the overlay.
- Shows a Black Cloud Merchant shopping list for chest/merchant pets you have not maxed, with rank, soul progress and price from recently observed offers. Open the shop after your collection synchronizes.

Nearby doesn't have a fixed detection radius, and some mobs aren't covered yet. Map links show spawn locations. Walking arrows and metre distances are still being worked on.

## Install

1. If Pet Tracker is already running, quit it from the Windows tray.
2. Install [Npcap from its official website](https://npcap.com/#download) if it is not already installed. **Npcap is required for live tracking and is not included with Pet Tracker.** If capture is unavailable, the app opens connection help with a link to the official Npcap installer.
3. Download and run the Pet Tracker installer.
4. Open Pet Tracker, then log into your AION 2 character. Your first collection is created automatically.

Already in game? Return to character selection and log back in with the tracker open. Collection sync happens automatically at character login; there is no separate sync button. If ranks or totals are still missing, check **Settings → Help** for capture status and troubleshooting.

## Updates and feedback

Check **Settings → Updates** for new versions.

If something looks wrong, [report it here](https://github.com/0Fox2Give/PetTracker/issues/new?template=bug_report.md). Include your tracker version, region, and the pet or mob name. For a missing soul gain, mention whether the game showed a soul reward message.

[Troubleshooting, alts and privacy](SUPPORT.md) · [Release notes](https://github.com/0Fox2Give/PetTracker/releases/tag/updates)

## Before you use it

Pet Tracker is unofficial and has no approval from NC/NCSOFT. Using third-party tools can put your game account at risk. The Windows installer is unsigned and may trigger a security warning.

Your collection stays on your PC; the app doesn't automatically upload it. Updates come from GitHub. This repository hosts downloads and update tools, rather than the full app source.

Read the [legal notice](LEGAL.md) and [third-party notices](THIRD-PARTY-NOTICES.txt) before testing. Game names and artwork belong to their respective owners.

## About this repository

This is the public download and update repository. The full tracker source is not included.

| Looking for | Where to go |
| --- | --- |
| Install Pet Tracker | [Windows installer](https://github.com/0Fox2Give/PetTracker/releases/download/updates/PetTracker.Desktop-win-stable-Setup.exe) |
| Missing ranks, setup, alts or privacy | [Help](SUPPORT.md) |
| Changes and known issues | [Release notes](CHANGELOG.md) |
| Update tools and repository structure | [Maintainer guide](docs/MAINTAINING.md) |
| Component licenses | [Third-party notices](THIRD-PARTY-NOTICES.txt) and [licenses](licenses/) |
