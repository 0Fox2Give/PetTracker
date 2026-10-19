# Pet Tracker help

## Capture unavailable / empty overlay

Live tracking requires [Npcap](https://npcap.com/#download), installed separately. Pet Tracker 0.1.43 does not automatically ask you to install it. Close Pet Tracker, install Npcap from its official website if missing, then reopen Pet Tracker before logging into your character. Follow any restart instructions from the Npcap installer.

If Npcap is already installed, capture can also fail because of Windows permissions or adapter access. Open **Settings → Help** and save a support report. In 0.1.43, **Run connection check** refreshes the displayed status; it does not install or repair the driver. Please include that report in your bug report instead of repeatedly reinstalling.

## Missing ranks or soul totals

Start the tracker before logging into your character. If it was opened after you were already playing, return to character selection and log back in once. Pet Tracker needs the collection data sent during login.

If that doesn't help, open **Settings → Help** and save a support report. [Open a bug report](https://github.com/0Fox2Give/PetTracker/issues/new?template=bug_report.md) with your tracker version, region, exact pet or mob name, and what went wrong. If a soul gain is missing, say whether the game displayed a soul reward message.

GitHub issues are public. Review your report before posting, and remove character names and chat from screenshots. Don't upload your collection folder, raw captures, passwords, or other personal information.

## Playing alts

For characters on one game account, enable **Settings → General → Share progress across all characters**. New alts will use the selected collection.

Before switching to a separate game account, turn sharing off and use **Manage accounts** to keep its collection separate.

## Updating

Open **Settings → Updates**, check for an update, and download it. Restart the tracker when you're ready. Collections and preferences are saved separately from the program files.

The public installer is currently **0.1.43**. Downloads and updates are hosted here; the old PetTracker-Testing repository has been retired.

## Data stored on your PC

The tracker processes captured game traffic locally. It saves collection progress, preferences, backups, and diagnostics on your PC. Installed copies normally use `%LocalAppData%\PetTracker`; a custom portable launch can use another folder.

Local diagnostics can contain character information. The shareable support report excludes account names, character names, and packet samples. The app doesn't automatically upload collections or support reports.

Update checks and downloads contact GitHub. Opening a map or support link connects to that website under its own privacy policy. Pet Tracker doesn't require a website account or your game password.

## Download warnings

The installer isn't Authenticode-signed. In-app updates use a separate publisher signature and package hashes to check the download. Those checks don't mean NC/NCSOFT approves the app or that using it is risk-free.

Use the download linked from this repository. Don't bypass a security warning you can't verify. Npcap is installed separately under [its publisher's terms](https://npcap.com/).

[Back to the README](README.md) · [Legal notice](LEGAL.md)
