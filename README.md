# Pet Tracker

An independent Windows companion for tracking pet souls and farming progress in **AION 2**.

**Public beta · Windows x64 · Current public installer: 0.1.40**

[Download the Windows installer](https://github.com/0Fox2Give/PetTracker/releases/download/updates/PetTracker.Desktop-win-stable-Setup.exe) · [Release details](https://github.com/0Fox2Give/PetTracker/releases/tag/updates) · [Report a bug](https://github.com/0Fox2Give/PetTracker/issues/new?template=bug_report.md)

> Pet Tracker is unofficial and is not affiliated with, endorsed by, or supported by NC/NCSOFT. Publisher approval has not been established. Third-party tools may carry account-enforcement risks; read the [testing and legal notice](LEGAL.md) before using it.

## What you can test

- **Recent:** pet soul activity, ranks, progress toward the next rank, and MAX status.
- **Nearby:** supported pet mobs observed by the game client, matched to your collection.
- **Find a pet:** search by pet or mob name, view known acquisition sources, and open farming maps.
- **Pin a pet:** keep a farming goal visible. Hide completed pets and adjust the overlay's appearance.
- **Updates:** check for new versions and download them from inside the app.

Nearby is an observation list, not a measured distance radius. Coverage is incomplete, and sightings can expire. Maps show known spawn locations, not live availability. Walking arrows and accurate metre distances are not promised features of this public build.

## Get started

1. Download the installer above. If an older copy is running, choose **Quit Pet Tracker** from its Windows tray menu first.
2. Run the installer and open Pet Tracker from its shortcut. [Npcap](https://npcap.com/#download) is required for local capture and is installed separately under its own terms; the app provides setup guidance if it is missing.
3. **Start Pet Tracker before logging into your character.** The first detected character creates the local collection automatically. No website registration or game password is required.
4. If you started the tracker while already playing, return to character selection and log back in once. The tracker needs the login collection message to load current ranks and soul totals.

In 0.1.40, a new installation may display **Choose your account** before the first character is detected. You do not need to create one manually: log into your character with the tracker running. Clearer first-run wording and additional Abyss mappings are in local testing and are not yet included in this download.

For one game account, **Settings → General → Share progress across all characters** connects new alts to the selected collection. Turn it off before using a separate game account, and use **Manage accounts** to keep collections separate.

## Updates and troubleshooting

Open **Settings → Updates** to check for updates, download, and restart when convenient. Existing collections and preferences are stored separately from the program files.

If ranks or totals remain missing after logging in, open **Settings → Help** and save a support report. When reporting a bug, include the app version, region, exact mob/pet name, what you expected, and what appeared in the tracker. For missing soul updates, say whether the game actually displayed a soul reward; a kill alone is not evidence of a reward.

Please redact character names and chat from screenshots. Do not post your collection folder, raw captures, passwords, or other personal information. Reports submitted through GitHub Issues are public.

## Privacy and download integrity

The app processes captured game traffic locally and saves collection progress, preferences, backups, and diagnostics on your PC. Standard installed copies use `%LocalAppData%\PetTracker`; custom portable launches may use another folder. Local diagnostics may contain character information. The shareable support report excludes account/character names and packet samples; review it before posting.

Update checks and downloads contact GitHub. Links you open for maps or support contact their respective websites and are subject to those services' privacy policies. The app does not automatically upload your collection or support reports.

The Windows installer is **not Authenticode-signed**. In-app updates verify a separate publisher signature and package hashes; this verifies update integrity, not NC approval or a guarantee of safety. Download from this repository and do not bypass a security warning you cannot verify.

## Project and notices

This repository hosts downloads, update-delivery tools, and public verification metadata. It is not the complete application source repository. Game names, artwork, and other third-party material remain the property of their respective rights holders.

See [Legal and testing notice](LEGAL.md) and [Third-party notices](THIRD-PARTY-NOTICES.txt). Included dependency licenses remain applicable. Bug reports and testing feedback are welcome; accuracy, compatibility, and account safety are not guaranteed.