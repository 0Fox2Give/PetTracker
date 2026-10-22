# Pet Tracker help

## Tracker window is hidden

Version 0.1.50 shows the overlay on a normal launch, even if it was hidden in the previous session. If the app is already running, right-click its Windows tray paw icon and choose **Show / hide tracker**. If **Only show over Aion 2** is enabled, bring the game to the foreground.

Version 0.1.51 also corrects saved positions that land in the gap between monitors. If the window is still missing, open **Settings → Overlay**, temporarily turn off **Only show over Aion 2**, and click **Reposition tracker**. If that works, tell us whether turning the game-only option back on hides it again; this helps distinguish placement from focus detection.

If the window opens but contains no pets, check Settings → Help for capture status.

## Capture unavailable / empty overlay

Live tracking requires [Npcap](https://npcap.com/#download), installed separately. Version 0.1.45 opens connection help when capture is unavailable, with a **Download Npcap** button. Older 0.1.43 copies do not automatically ask you to install it. Close Pet Tracker, install Npcap from its official website if missing, then reopen Pet Tracker before logging into your character. Follow any restart instructions from the Npcap installer.

If Npcap is already installed, capture can also fail because of Windows permissions or adapter access. Open **Settings → Help** and save a support report. In 0.1.43, **Run connection check** refreshes the displayed status; it does not install or repair the driver. Please include that report in your bug report instead of repeatedly reinstalling.

## Missing ranks or soul totals

Start the tracker before logging into your character. If it was opened after you were already playing, return to character selection and log back in once. Pet Tracker needs the collection data sent during login.

There is no sync button. Synchronization happens automatically when the tracker receives that collection data. If capture is unavailable, fix that connection first; logging in again cannot fix a missing capture driver.

Settings → Help reports collection sync separately from incoming game traffic. A connected capture session does not by itself mean your collection has loaded.

Version 0.1.54 reduces capture overhead and increases buffering to make soul collection tracking more consistent across pets. If game traffic is still lost, the overlay shows **Totals paused**, turns the progress bars gray and keeps the last known totals. Return to character selection and log back in with Pet Tracker open. Fresh collection data clears the notice and restores the bars.

If totals pause repeatedly, save a support report from **Settings → Help**. Include your version, region and the pet involved, and mention whether Recent and Nearby both stopped updating. The tracker does not guess missing soul gains.

The first unlock can still leave a pet showing **Rank increase pending**. Version 0.1.51 prevents a soul deduction from falsely restarting the counter at 0/5 while rank confirmation is missing, but first-unlock recognition is still an open issue. Logging back in with the tracker running reloads the collection as a workaround. Please report the exact pet, version and region if it happens.

If that doesn't help, open **Settings → Help** and save a support report. [Open a bug report](https://github.com/0Fox2Give/PetTracker/issues/new?template=bug_report.md) with your tracker version, region, exact pet or mob name, and what went wrong. If a soul gain is missing, say whether the game displayed a soul reward message.

GitHub issues are public. Review your report before posting, and remove character names and chat from screenshots. Don't upload your collection folder, raw captures, passwords, or other personal information.

## Black Cloud Merchant

Keep Pet Tracker running, log in to synchronize your collection, then open the merchant. Recently seen chest, merchant and dungeon pets that you have not maxed appear in the overlay with your rank, current soul total, shop tab and Kina price. Ordinary world drops are excluded. **Settings → Tracking → Black Cloud Merchant** controls this list.

Version 0.1.52 adds Phantasmal Lakshmi, Black Smoke Murute, Harcon, Red Spark Ignus and Predator Saraswati. It also changes how incoming shop messages affect the list. If buying one pet still makes the other recommendations disappear, please report it; that case and the blank-list report still need in-game confirmation.

An empty list may mean none of the observed offers qualify. Recommendations also stay hidden until the collection is synchronized. Stock expires after two minutes or at shop refresh, and clears when the connection changes. Confirm the offer in game before purchasing; the tracker does not buy or refresh anything.

Settings → Tracking → Black Cloud Merchant shows guidance when capture or collection sync is missing.

If the list is missing, enable **Show merchant shopping list in overlay**, check collection sync, then open both **Special** and **Pets** in game. Both tabs are supported for the recorded stock format. Other regions or changed stock formats may need investigation. If an eligible pet is still missing, include its name, shop tab, region and tracker version with a support report. An empty list alone does not establish that tracking worked.

When a recommended pet receives souls, its merchant row shows the updated total for 20 seconds, then disappears. This works in Recent and Nearby. The price disappears during that feedback period, and the same stock cycle does not recommend that pet again. A fresh shop refresh can recommend it again if needed. This follows observed rewards, not an independently decoded purchase receipt.

## Playing alts

For characters on one game account, enable **Settings → Account → Share progress across all characters**. New alts will use the selected collection.

Before switching to a separate game account, turn sharing off and use **Manage accounts** to keep its collection separate.

## Pinned pets and settings

Pin pets from Find a pet, or right-click an overlay row. Pins stay in the order added in both Recent and Nearby. Settings → Tracking → Maximum pinned pets allows one to five, with three by default. Lowering the limit keeps existing pins; unpin some before adding more.

Settings are grouped into Tracking, Overlay, Account, Updates and Help. Windows startup and start-hidden options have been removed. Reset this section on Tracking or Overlay preserves your collection, accounts and pinned pets. Older cosmetic preferences are preserved until you reset Overlay.

## Collection filters and shortcuts

Find a pet shows collection totals by family. Combine family, rank, region and source filters to narrow the list, or sort by souls remaining to the next rank. Unknown ranks stay separate from uncollected pets. Stale or estimated totals are excluded from next-rank recommendations until the tracker has confirmed progress.

**Details** and **Map** share the selected pet and all filters. Choose **Hide maxed** in the rank filter to exclude confirmed maxed pets, or save a combination under **Saved filters**. Short windows put family, rank, region and sorting options under **Filters**.

The Map tab includes offline maps for Verteron and Altgard. Dots show approximate static spawn locations, not live sightings or player position. If a pet or region has no built-in map, check Details or use the available external source link. See the [farming tools guide](docs/FARMING.md) for controls and map limitations.

**This session** counts observed soul rewards; login balances and kills do not add souls. Capture interruptions mark it incomplete, and reconnecting or restarting begins a new session. **Reset session** leaves collection progress alone.

Choose a **Farming goal** in pet details to track a target rank. Unknown or stale totals wait for sync, and estimates stay labelled. Completion alerts use the notification settings under **Settings → Overlay**. **Compact pet rows** is also on that page.

Open **Settings → Overlay → More overlay options** and select **Tracker shortcut** to record the key or combination you want. You can combine Ctrl, Alt and Shift with a key, clear the shortcut, or cancel without changing it. If that combination cannot be registered, Settings shows an error so you can choose another.

## Updating

Open **Settings → Updates**, check for an update, and download it. Restart the tracker when you're ready. Collections and preferences are saved separately from the program files.

The public installer is currently **0.1.56**. After updating, return to character selection and log back in with the tracker running to load your collection. Downloads and updates are hosted here; the old PetTracker-Testing repository has been retired.

If Restart to update says **Could not safely close capture**, an older version may have missed the successful shutdown. Version 0.1.48 fixes that check. To get past it on an older copy, choose **Quit Pet Tracker** from its Windows tray icon, then download and run the current installer. Do not delete your collection folder. If Quit itself reports a save error, leave the tracker running and save a support report instead.

## Data stored on your PC

The tracker processes captured game traffic locally. It saves collection progress, preferences, backups, and diagnostics on your PC. Installed copies normally use `%LocalAppData%\PetTracker`; a custom portable launch can use another folder.

Local diagnostics can contain character information. The shareable support report excludes account names, character names, and packet samples. The app doesn't automatically upload collections or support reports.

Update checks and downloads contact GitHub. Built-in maps work offline; opening an external map or support link connects to that website under its own privacy policy. Pet Tracker doesn't require a website account or your game password.

## Download warnings

The installer isn't Authenticode-signed. In-app updates use a separate publisher signature and package hashes to check the download. Those checks don't mean NC/NCSOFT approves the app or that using it is risk-free.

Use the download linked from this repository. Don't bypass a security warning you can't verify. Npcap is installed separately under [its publisher's terms](https://npcap.com/).

[Back to the README](README.md) · [Legal notice](LEGAL.md)
