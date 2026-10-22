# Release notes

## Unreleased

- Windows farming map for Verteron and Altgard, with pet search, family filters,
  hide maxed, pan, zoom and fit-to-spawns. Bundled map data works offline. Dots
  show approximate static spawn locations; they do not show your position or
  currently alive mobs.
- Session summary of observed soul rewards and pets ranked up. Capture gaps
  mark the summary incomplete. Resetting it leaves collection progress alone.
- Account-specific farming goals, remaining souls and a completion notification.
  Unknown or stale balances wait for sync; estimates stay labelled.
- Up to ten saved combinations of collection filters.
- Compact pet rows in Settings → Overlay.

These changes are in development and are not in the public 0.1.54 installer.
They do not resolve the unconfirmed first-unlock or merchant reports. The new
windows are Windows-only; shared collection data remains Linux-compatible.
See [farming tools](docs/FARMING.md) for behavior and map-source limitations.

## 0.1.54 — October 21, 2026

- Improved soul collection reliability across pets by reducing capture overhead and increasing the buffer for traffic bursts. This addresses capture pressure that could interrupt collection sync and leave totals stuck.
- Added a **Totals paused** notice when collection sync is lost, including when pets are already displayed. Last-known progress bars turn gray; return to character selection and log back in with the tracker open to reload the collection. The tracker does not guess missing gains.
- Windows Find a pet now has collection totals by family, combined family/rank/region filters, and sorting by souls remaining to the next rank. Unknown ranks stay separate from uncollected pets; stale, estimated and unverified balances do not receive next-rank recommendations. The selected farming region appears first in the source details.
- Replaced the three Windows tracker-shortcut presets with a key recorder. Choose a key alone or combine Ctrl, Alt and Shift, clear the binding, or cancel without changing it. Existing Ctrl+Alt shortcuts are preserved. Conflicting shortcuts show an error in Settings.

Includes the collection and shortcut changes tested locally in 0.1.53. After updating, relog with Pet Tracker running. Saved collections, settings and pins are preserved.

Live testing confirmed soul gains updating after a fresh login with no capture drops or stream gaps during the check. This does not close every collection issue: first-unlock recognition remains open, and the reported merchant-list cases and Linux overlay placement still need live confirmation.

## 0.1.52 — October 20, 2026

- Added the five catalogued dungeon pets to merchant recommendations and the shopping-list search, including Phantasmal Lakshmi and Black Smoke Murute. Ordinary world drops and maxed pets remain excluded.
- Stopped clearing all merchant offers merely because an adjacent, unrecognized shop message arrived. Stock still expires after two minutes and clears on tab-open, invalid replacement, capture loss or character changes. Live confirmation of the reported purchase case is still needed.
- Help now distinguishes receiving game traffic from having a synchronized collection.
- Split catalog, collection models, settings, storage and worker contracts into focused files; normalized source and build-script formatting without changing their logic.
- Added a Linux preview client and process-scoped libpcap capture, with separate builds and tests. Live AION 2/Wine compatibility is not yet verified. See [Linux preview](docs/LINUX.md).
- Shared family labels between desktop clients and enabled NuGet vulnerability auditing for direct and transitive dependencies.

The first-unlock recognition bug and Linux overlay placement remain open. The merchant changes pass regression checks, but the reported purchase and blank-list cases still need testing in game. Saved collections and settings are preserved.

## 0.1.51 — October 19, 2026

- Prevented soul deductions from showing a fresh 0/5 or 0/25 counter while a rank change is still unconfirmed. The completed stage stays pending, and later soul gains are preserved until the rank is confirmed.
- Fixed overlay placement in gaps between monitors and improved recovery from disconnected screens and window resizing.

The first-unlock recognition issue is still being investigated. If the tracker remains on Rank increase pending, return to character selection and log back in with Pet Tracker running to reload your collection. This release does not claim to resolve every hidden-overlay report.

Your saved collection and settings are preserved.

## 0.1.50 — October 19, 2026

- Pin several pets at once. The default is three; choose a limit from one to five in Settings → Tracking. Pins keep their order in Recent and Nearby, and existing pins are preserved.
- Reorganized settings into Tracking, Overlay, Account, Updates and Help. Removed Windows startup, start-hidden and redundant appearance switches; advanced options stay out of the way.
- Opening Pet Tracker normally now shows the overlay even if you hid it in the previous session. An empty list stays visible until activity arrives. The optional game-only setting still applies.
- Updated collection-sync guidance and merchant help to match the new settings pages.

Your collection and pinned pets are preserved. The first-unlock “Rank increase pending” issue remains under investigation. Walking arrows are not included.


## 0.1.49 — October 19, 2026

- Refreshed Find a pet with a smaller search bar, clearer selection, grouped farming locations and soul progress that updates while the window stays open.
- Added souls-remaining tooltips to pet and merchant rows. Unknown or stale balances do not produce a remaining-souls estimate.
- Added character-login instructions when collection sync is needed. Sync is automatic; there is no separate sync button.
- Added merchant status in Settings → General to distinguish missing capture, collection sync, missing fresh stock and no current recommendations.
- Changed the merchant heading to “Pets only from Chests or Black Cloud Merchants” and removed the extra stock reminder beneath the list.

Includes the update-restart fix, family crystal tags and merchant purchase-progress feedback from previous versions.

The reported first unlock getting stuck on “Rank increase pending” remains under investigation. Relogging with Pet Tracker running reloads the collection as a workaround. Walking arrows and measured distances are not included.


## 0.1.48 — October 19, 2026

- Fixes “Could not safely close capture” when Restart to update incorrectly treats a successful capture shutdown as a failure.
- Keeps the requirement that capture saves and exits successfully before installation begins.

If an older copy still shows this error, quit Pet Tracker from its Windows tray icon, then run the current installer. Collections and preferences are stored separately and are preserved.


## 0.1.47 — October 19, 2026

- Pet rows now show their family and matching crystal: Cogni, Fera, Natura, Varian or Special. Tags appear in Recent, Nearby, merchant recommendations and Find a pet.
- When a recommended merchant pet receives souls, its row shows the updated total for 20 seconds, then leaves the shopping list. This works in both Recent and Nearby. The price disappears during that brief feedback period.
- Removed the "No pet activity in the last ... seconds" message from Recent. No countdown text is shown on merchant rows.

Merchant feedback follows observed soul rewards after an offer was shown. It does not automate purchases or independently confirm a purchase receipt. Capture interruptions still require a fresh collection sync. The reported unlock-to-Rank-1 issue remains under investigation; walking arrows are not included.


## 0.1.46 — October 19, 2026

- Merchant recommendations now show your current rank and soul total (for example, Rank 1 · 10/25 souls), alongside the observed Kina price and shop tab.
- Soul totals refresh when your balance changes without a rank change. Unknown and estimated totals retain their labels.

Includes the merchant shopping list and capture setup help from 0.1.45. Recommendations still require a synchronized collection and fresh observed stock. No purchases are automated.

The reported unlock-to-Rank-1 issue remains under investigation. Genus labels and walking arrows are not included in this update.

## 0.1.45 — October 19, 2026

- Black Cloud Merchant recommendations now appear in the overlay with pet rank, tab and observed Kina price. Open the merchant with your collection synchronized. The list excludes maxed pets and pets with known mob sources; random-chest alternatives are included.
- Capture setup help opens automatically when no capture adapters are available. The Npcap download link is visible directly in Help, and refreshing connection status gives a visible result.
- Corrected setup instructions: Npcap is required and installed separately, not bundled.

Merchant stock expires after two minutes or at shop refresh and is cleared when the connection changes. Confirm stock before buying. The tracker does not purchase or refresh for you.

Known issues: some players report an unlock-to-Rank-1 transition being missed; its live message is still under investigation. Genus labels and souls-remaining text are not included in this update. Walking arrows and metre distances remain unavailable. Abyss soul-reward coverage still needs live confirmation.


## 0.1.43 — October 18, 2026

Previous public beta for Windows x64. Use the current download linked in the README.

- Removed the confusing account-selection prompt on a first installation. The tracker waits for character login and creates the collection automatically.
- Added 32 Lower Reshanta mob mappings for Nearby.
- Expanded Find a pet with information and portraits for 208 discoverable pets, regional mob names, dungeon sources and soul-chest alternatives.
- Corrected farming links and added the Halloween pets listed in the October 14 shop announcement.

### Known limitations

- Nearby has no fixed detection radius; coverage is incomplete.
- Walking arrows and metre distances are not available in this release.
- The reported Abyss soul-reward issue still needs live confirmation. Adding mob mappings does not establish that reward tracking is fixed.
- Acquisition records were crosschecked, but every source and drop has not been verified in game. Shop and event availability can change.
- The Windows installer is unsigned. Core, UI and updater checks, package inspection and isolated startup testing passed; these checks are not publisher approval.

For missing ranks or totals, start the tracker before character login. If already in game, return to character selection and log back in once. See [Help](SUPPORT.md).

Earlier versioned assets remain on the release page for compatibility. They are not the recommended download. This changelog begins with 0.1.43.

[Back to Pet Tracker](README.md)
