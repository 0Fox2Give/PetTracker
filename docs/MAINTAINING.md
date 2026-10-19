# Maintaining the download repository

Players should start with the [README](../README.md). This repository contains public documentation, update-client code and release tools; it is not a complete tracker source checkout.

## File layout

| Path | Purpose |
| --- | --- |
| `README.md` | Main download and setup page |
| `SUPPORT.md` | Troubleshooting, account sharing and privacy |
| `CHANGELOG.md` | Public release changes and known limitations |
| `LEGAL.md`, `THIRD-PARTY-NOTICES.txt`, `licenses/` | Project notices and dependency licenses |
| `docs/` | Maintainer documentation and plain-text installation reference |
| `.github/` | Bug-report template and publishing/renewal workflows |
| `src/Updates/` | Update verification and lifecycle code |
| `tools/` | Release verification, promotion and feed renewal |
| `Directory.Build.props` | Shared build properties for these tools |
| `publisher.json` | Pinned feed address and public verification key; not a private signing key |
| `feed-status.json` | Last recorded successful feed renewal |

The publisher and feed-status files stay at the root because scripts and workflows use those paths.

## Release assets

The `updates` tag is a permanent delivery address used by installed clients. Do not rename it or move its assets to another release as a cosmetic cleanup.

| Asset | Purpose |
| --- | --- |
| `PetTracker.Desktop-win-stable-Setup.exe` | Fixed download address for the current public installer |
| `PetTracker.Desktop-<version>-win-stable-Setup.exe` | Installer for a specific version |
| `PetTracker.Desktop-<version>-win-stable-full.nupkg` | Package downloaded by the updater |
| `releases.win-stable.signed.json` | Current authenticated update feed |
| `candidate-<version>-win-stable-<hash>.signed.json` | Staged signed metadata used for promotion and release evidence |

Labels describe these files without changing their download names. Keep versioned files available: previously downloaded metadata may still reference them. GitHub's generated source archives are not player installation packages.

## Updating the public page

1. Publish only a verified candidate through the existing **Publish update** workflow. A documentation change must not promote an unreleased build.
2. Verify the signed public feed, package hashes and fixed installer against the approved candidate.
3. Update the version in README and SUPPORT, add release notes to CHANGELOG, and update the `updates` release title and body.
4. Label the fixed installer as the recommended download, identify the current versioned installer, and label older assets as previous versions. Keep all filenames intact.
5. Check documentation links and inspect the rendered README and release page.

**Renew update feeds** refreshes signed metadata and records `feed-status.json`. Its private signing key belongs in the configured GitHub Actions secret, never in source, logs, screenshots or release downloads. Keep the publisher identity unchanged during documentation cleanup.

App data, account collections, captures, local experiments and private build evidence do not belong in this repository or player downloads.
