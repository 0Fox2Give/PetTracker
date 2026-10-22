# Build and test

For the native Linux preview, see [LINUX.md](LINUX.md). The instructions below
build the Windows application. Both clients share the core and capture worker;
neither client is a compatibility wrapper around the other.

Requirements: Windows x64, PowerShell 7, .NET SDK 10.0.401 (or a compatible patch),
and network access to NuGet on first restore. `global.json` pins the SDK family.
Npcap is required for live capture, but not these offline regression checks.

Open `PetTracker.slnx` in your editor, or use PowerShell from the repository root:

```powershell
$env:NUGET_PACKAGES = Join-Path $env:USERPROFILE '.nuget/packages'
./tools/Test.ps1
./Build.ps1
```

`Test.ps1` runs the core, WPF and updater suites sequentially with isolated data
under `test-results`. It does not launch the capture worker or touch your installed
collection. The UI suite renders fixtures offscreen; it is not a live game test.

`Build.ps1` produces a self-contained Windows runtime under `build/runtime-*`, a
portable ZIP and an offline installer under `release`. The final build receipt is
`package-verification.json`. Use the runtime path in that receipt, rather than
guessing which output is newest. Do not run a second tracker against your live
collection when testing a build; use a separate `--data` directory.

Plain `Build.ps1` does not embed the official updater configuration. To reproduce
the official updater configuration, use `./Build.ps1 -UpdatePublisherFile ./publisher.json`.
That file contains only a public verification key and public endpoints. It does
not let you sign or publish official updates. For an independent fork, generate
your own publisher configuration and use your own download channel and keys.

The installed update-enabled edition uses Velopack 1.2.161. Install that pinned
tool with `dotnet tool install vpk --version 1.2.161 --tool-path ./build/vpk`, then
use `Pack-Updates.ps1` with its required arguments to create a candidate. Consult
the script parameters and [MAINTAINING.md](MAINTAINING.md) before publishing.

## Source and release relationship

The `source-0.1.51` tag identifies the initial public source for the 0.1.51 application. Public tests replace
recorded fixture identifiers/opaque fields with sanitized inputs. Formatting, fixture sanitization, removal of an excluded unused file, and
build/docs/license packaging changes are listed in the source publication commit. The
existing 0.1.51 installer is unchanged and was built locally before publication;
we do not claim a bit-for-bit reproducible or CI-built historical binary.

The **Build and test source** workflow runs on pushes and pull requests, with
read-only repository access and no release secrets. It tests the source and builds
an unsigned portable/installer artifact for that exact commit. These CI artifacts
are developer builds, not automatic updates. Publishing to the official signed
update channel remains a separate maintainer action.

## Offline farming map data

`python tools/import-farming-maps.py` regenerates the map bundle from the pinned
upstream commit in the script. It downloads only the two supported regions,
filters spawn points through `FarmingLinks.json`, and records source SHA-256
hashes in `assets/maps/provenance.json`. Review coordinate, source and rights
changes before updating that pin. It is an explicit maintenance step, not part
of builds or app startup. See [map limitations](FARMING.md).

For a focused UI iteration, run the WPF test project with an output directory
followed by `--farming-only`. The normal `tools/Test.ps1` still runs all suites.

## Layout

- `src/Core`: collection accounting, protocol decoding, catalog and settings.
- `src/Worker`: passive capture, connection selection and local worker service.
- `src/App`: overlay, finder, settings and update UI.
- `src/Linux`: experimental Avalonia desktop client; no Windows updater dependency.
- `src/Setup`: offline installer and uninstall handling.
- `src/Updates`: signed metadata and package validation, update lifecycle.
- `tests`: core, UI, update and package checks.
- `assets`: shipped catalog and artwork; see its separate rights notice.
- `tools`: build verification and release tooling.

Historical local handoffs, account data, recordings, signing keys and build
outputs are not part of the source checkout. Obsolete delivery-only bootstrap
scripts and audits requiring private reference snapshots are omitted. Disabled
navigation code remains with its regression tests because it is compiled into
0.1.51; it is not a working walking-directions feature.
