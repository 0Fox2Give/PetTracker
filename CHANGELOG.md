# Release notes

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
