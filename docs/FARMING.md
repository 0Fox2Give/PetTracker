# Farming tools

These features are included in the Windows client from version 0.1.56.
The experimental Linux client does not yet include these windows.

## Farming map

Open **Find a pet**, select a pet, then choose **Map** beside **Details**.
Both views share the collection list, search and filters. The overlay's
**Find on map** menu reuses this window. Supported location links also open
the Map tab; other regions retain their external source links.

Search accepts pet and mob names. Choose a region or family and use **Hide maxed**
in the rank filter to exclude confirmed maxed pets. Saved filters apply to both
views. In short windows, the **Filters** button contains family, rank, region
and sorting controls. Selecting a pet frames its spawn points. Drag or use the arrow keys to
pan, scroll or use the buttons to zoom, and hover a dot for the mob name.
**Fit spawns** frames the selected pet again; **Reset** shows the region.

Dots are approximate static spawn locations. The map does not track player
position, measure distances or show which mobs are alive. Height differences
and separate floors are not represented. Other regions retain their existing
external map links.

The bundled tiles, spawn data and calibration come from
[SkeeveAN/Aion-DPS-Meter](https://github.com/SkeeveAN/Aion-DPS-Meter/tree/e16d35d4f19e19b03b0cb05ab4a1658bb51102a6).
The coordinate transform is an approximate upstream manual fit, not an in-game
calibration verified by Pet Tracker. Only NPCs matching this project's audited
regional soul-source list are included. The map needs no network connection at
runtime. Source hashes and the revision are recorded in
`assets/maps/provenance.json`; licenses and artwork notices are included.

## This session

**Find a pet → This session** shows soul rewards received for the connected
character and the pets whose rank increased after a reward was observed. The
timer starts at the first reward. Login balances and kills do not add souls.
Repeated announcements of the same rank do not add another rank increase.

A capture gap marks the session incomplete. Received rewards still appear,
but the tracker cannot reconstruct missed rewards. The session starts over on
reconnect, character/account switch or app restart. **Reset session** clears only
this summary; it preserves your collection, farming goals, pins and Recent rows.

## Goals and saved filters

In a pet's details, choose a **Farming goal** from Rank 1 through Rank 3. Goals
are saved with the connected account. Remaining souls include intervening
stages. The tracker labels estimated balances and waits for sync when a total is
stale or unknown. A synced rank reaching the target produces one notification,
using the existing notification settings. Already completed goals do not notify
again when you log in. Choose **None** to remove a goal.

Use **Saved filters** in Find a pet to name the current search, family, rank,
region, sort and source choices. Choose a saved name to restore them. Saving
with an existing name replaces that filter; **Delete** removes the selected
filter. Up to ten filters are stored in local app settings, independently of
collection data.

**Settings → Overlay → Compact pet rows** uses smaller portraits and less row
spacing while keeping pet names, ranks, soul totals and status notices visible.

## Validation limits

Regression fixtures cover reward-only session accounting, capture gaps, reset
behavior, goal costs and stale-state guards. WPF tests exercise the controls and
render normal and small windows. These checks do not establish live map accuracy
or resolve the outstanding first-unlock and merchant reports.
