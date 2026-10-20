# Linux preview

The repository includes a native Linux client and a libpcap capture worker.
This is an **experimental source build**, not a supported game-tested release.
The Windows installer and Windows automatic updates do not work on Linux.

The Linux client shares collection accounting, Recent/Nearby selection, multiple
pins, pet families, finder data and merchant recommendations with Windows. It
uses Avalonia for the desktop window. The Windows WPF client remains separate.

## Requirements

- x86-64 Linux with glibc. CI uses Ubuntu 24.04.
- X11 or an XWayland session. Native Wayland integration is not enabled.
- libpcap, fontconfig and X11 client libraries.
- AION 2 must already run on your Linux setup. This project does not install
  Wine/Proton or establish that the game or its anti-cheat supports Linux.

Always-on-top placement depends on your desktop/compositor. Game-only visibility,
global hotkeys, click-through, tray integration and the Windows updater are not
implemented in this preview. Closing the tracker requests a clean worker exit.
Use your desktop's window rules if you want it on a particular workspace.

## Build and run

Install the .NET SDK selected by `global.json`. On Ubuntu/Debian, install native
dependencies with:

```sh
sudo apt-get install libpcap-dev libcap2-bin libfontconfig1 libx11-6 libice6 libsm6
bash tools/Build-Linux.sh
```

The self-contained output is `build/linux-x64/`; it does not require .NET on
the machine where you run it. Keep the entire directory together.

Grant raw-packet capture permission **only to the packaged capture executable**:

```sh
sudo setcap cap_net_raw=ep build/linux-x64/capture/PetTracker.Capture
./build/linux-x64/PetTracker.Linux
```

Run the tracker as your normal desktop user. Do not run the GUI as root, grant
capabilities to a shared `dotnet` executable, or install Npcap through Wine.
Reapply the capability after replacing the capture binary. A filesystem mounted
with `nosuid`, or one without extended attributes, may not allow file capabilities.
To remove the permission, use `sudo setcap -r` on the same capture executable.

Start the tracker before character login. Collections synchronize automatically
at login, as on Windows. Data is stored in the user's local application-data
directory (`~/.local/share/PetTracker` on a typical Linux desktop); `--data PATH`
selects a separate collection root. Windows collections are not auto-imported.

## Capture boundaries

The worker matches the configured game executable and reads its socket inode
links in `/proc/<pid>/fd`, then resolves those sockets through `/proc/net/tcp`
and `tcp6`. It accepts only incoming traffic for those connections. It does not
fall back to all processes, all Wine processes, or all TCP connections when
ownership cannot be established.

Wine/Proton setups that keep the socket solely in a shared wineserver process,
hide process descriptors, or put the game in another network namespace may need
additional work. They are not yet verified. If capture opens but no game traffic
is detected, report your distro, desktop/session, Wine/Proton version and the
connection status. Do not post raw packets, login data or collection files.

## Verification

```sh
dotnet run --project tests/LinuxTests/LinuxTests.csproj -- test-results/linux
dotnet run --project tests/LinuxUiTests/LinuxUiTests.csproj -- test-results/linux-ui
```

Linux CI checks real procfs socket ownership, libpcap startup, same-user worker
IPC and orderly exit. UI tests render fixtures and check merchant updates in
Nearby mode. These checks do not prove live AION 2/Wine compatibility or compositor
overlay behavior. See the build workflow for results for the exact commit.

References: [Avalonia Linux platform](https://docs.avaloniaui.net/docs/platform-specific-guides/linux),
[SharpPcap](https://github.com/dotpcap/sharppcap),
[Linux TCP table format](https://www.kernel.org/doc/html/latest/networking/proc_net_tcp.html).
