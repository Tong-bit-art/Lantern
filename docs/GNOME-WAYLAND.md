# GNOME Wayland

Lantern supports drag gestures on GNOME Wayland through the **Lantern donut at the cursor** GNOME Shell extension.

The extension provides the pointer state required by Lantern without access to `/dev/input`, membership in the `input` group, or running Lantern as root.

> This integration is experimental. Fedora 44 / GNOME Shell 50 has prior drag-to-conversion coverage.
> Fedora 45 / GNOME Shell 51.1 has extension loading, pointer-protocol and gesture-regression coverage;
> the manual Files/Nautilus checklist below remains to be confirmed after signing in again.

## Setup

Install Lantern normally:

```sh
./install.sh
```

On a GNOME session, `install.sh` puts the extension in `~/.local/share/gnome-shell/extensions/lantern-donut@lantern.sh` and asks GNOME to enable it. Then:

1. Open the **Extensions** application.
2. Enable **Lantern donut at the cursor**.
3. Log out and back in if the extension does not appear.
4. Start Lantern and make sure gesture detection is enabled under **Triggers**.

Do not run Lantern with `sudo`.

## Usage

With the default gesture:

1. Open **Files / Nautilus**.
2. Hold **Ctrl** and drag a file.
3. Move the file over the desired format in the Lantern wheel.
4. Release it to start the conversion.

The wheel stays in place while selecting a format.

Pausing Lantern, disabling the gesture or extension, excluding the application, or dropping outside the wheel leaves the normal file-manager behavior unchanged.

Custom modifiers and excluded applications continue to work through Lantern's existing gesture settings.

## Updates

Updating Lantern, in the app or with `./install.sh --upgrade`, also updates the extension when Lantern installed it. GNOME Shell keeps running the version it loaded at login, so log out and back in after an update. If the running extension and Lantern don't match, the **Triggers** page says so.

`install.sh` replaces only an extension folder that Lantern installed. If the folder came from somewhere else, a first install stops and an upgrade leaves it as it is. `uninstall.sh` removes the extension when Lantern installed it.

## Limitations

- GNOME integration is currently experimental.
- GNOME Shell 45–51 are declared as supported, but not every version has been tested.
- Global Tab and mouse-wheel state are not available for switching wheel faces during a drag.
- Global selection shortcuts and hold gestures are not available.
- Lantern runs through XWayland by default. With the experimental native Wayland backend (`LANTERN_USE_WAYLAND=1`), the extension recognizes the donut by Lantern's process; this combination has had less testing.
- Multi-monitor setups and fractional scaling may require additional testing.

KDE Plasma and Hyprland use a different setup; see `INSTALL.txt`.

## Troubleshooting

If the wheel does not appear:

- Make sure Lantern is not paused.
- Check that the drag gesture is enabled under **Triggers**.
- Make sure **Lantern donut at the cursor** is enabled. If Extensions reports **Out of date**, use an extension release that declares support for your GNOME major version, then sign out and sign in.
- Make sure Files / Nautilus is not excluded.
- Do not run Lantern with `sudo`.
- If the extension was installed or updated recently, log out and back in.

To check the extension:

```sh
gnome-extensions info lantern-donut@lantern.sh
```

To check the communication socket:

```sh
ls -l "$XDG_RUNTIME_DIR/lantern-gnome/pointer.sock"
```

When connected successfully, Lantern's log (`~/.config/Lantern/logs/`) contains:

```text
GNOME pointer extension connected
```

If the **Triggers** page says the extension belongs to another version of Lantern, log out and back in so GNOME Shell loads the installed extension. If the message stays, reinstall Lantern.


## Fedora 45 / GNOME 51 regression (2026-11-03)

After upgrading to GNOME Shell 51, the extension could remain enabled but report **OUT OF DATE**. The extension
metadata declared versions 45–50 only, so GNOME refused to run it. Lantern then waited for a socket that was never
created, and Ctrl + drag retained the file manager's normal copy behavior. This was a compatibility-declaration
bug, rather than evidence that GNOME had stopped providing the pointer API.

Extension version **3** adds GNOME **51** to `shell-version`, retaining all previously declared versions. The pointer
protocol stays at version **1**, using the same `pointer.sock` endpoint. The extension runtime JavaScript, Lantern
application, converters, X11, KDE and Hyprland paths are unchanged by this fix.

The matching metadata is installed both in the Shell extension folder and the bundled copy used by Lantern updates.
GNOME Shell 51.1 still reported the cached version 2 after the files were replaced, and its `ReloadExtension`
method returned `NotSupported`. **Sign out and sign in again**, then check the extension and Lantern:

```sh
gnome-extensions info lantern-donut@lantern.sh
ls -l "$XDG_RUNTIME_DIR/lantern-gnome/pointer.sock"
```

The extension should report version **3** and state **ACTIVE**. Lantern should show gesture detection running and
log `GNOME pointer extension connected`. A restart of Lantern alone does not refresh cached Shell metadata.
The fix retains normal GNOME extension-version validation.

### Automated checks

The existing `extension-smoke.js` tests real Gio sockets but mocks the Shell and does not load `metadata.json`.
It therefore cannot catch an unsupported `shell-version`. The additional `session-smoke.py` starts a separate
headless GNOME session with the production metadata, waits for the extension to become active and verifies the
private socket and protocol frames. It uses an isolated session bus, configuration, data and runtime directory;
it does not operate the user's desktop or installed Lantern.

Requirements: GNOME Shell with headless support, Xwayland, `dbus-run-session`, `gsettings`, Python 3 with GObject
bindings, and GJS/Mutter libraries for the existing socket test. Run from the repository root:

```sh
python3 tests/gnome/session-smoke.py
```

The command prints a directory containing `result.json`, `gnome-shell.log` and `runner.log`. To keep reports under
an ignored build folder, give a **new** output directory:

```sh
python3 tests/gnome/session-smoke.py --output artifacts/gnome51/session-check
```

To reproduce the failure using a saved original extension, pass `--extension /path/to/original/extension`.
On GNOME 51, metadata that still declares only 45–50 must fail with **OUT OF DATE**. The fixed extension must load
without changing the global version-validation setting. This checks actual compatibility gating rather than
asserting a particular JSON entry in a unit test.

On Fedora 45, the existing GJS test uses the installed Mutter 51 paths:

```sh
lantern_runtime_test=$(mktemp -d /tmp/lantern-gjs-XXXXXX)
XDG_RUNTIME_DIR="$lantern_runtime_test" \
GI_TYPELIB_PATH=/usr/lib64/mutter-51 \
LD_LIBRARY_PATH=/usr/lib64/mutter-51 \
gjs tests/gnome/extension-smoke.js "$PWD/installer/linux/gnome/lantern-donut@lantern.sh/extension.js"
```

Adjust the typelib and library paths for a different distribution or Mutter version.

### Validation results

- Original metadata: reproduced **OUT OF DATE** in a real isolated GNOME Shell 51.1 session.
- Fixed metadata: extension became **ACTIVE** and delivered protocol 1 pointer frames over a `0600` socket.
- Existing GJS/Gio smoke test: passed against Mutter 51 libraries, including button/modifier mapping, lock-screen
  suppression, fixed placement, monitor edges, native Wayland window identity and polling cleanup.
- A manual Ctrl + drag in a real Nautilus session has not been checked yet.

### Manual GNOME 51 checklist

Use disposable files in a temporary folder, after signing in again:

- [ ] Files/Nautilus: Ctrl + left drag opens the wheel; move onto a format and release to convert.
- [ ] The source file remains intact, and the drop does not create an extra source-file copy.
- [ ] The wheel stays fixed while reaching a format; cancelling or releasing in the center closes it.
- [ ] A drag without Ctrl keeps the normal file-manager behavior.
- [ ] Pause, disabled gestures and excluded applications prevent the wheel from opening.
- [ ] A configured alternate modifier, such as Ctrl+Shift, works.
- [ ] Disabling/re-enabling the extension disconnects/reconnects Lantern without restarting the app.
- [ ] Locking and unlocking the screen does not leave a gesture stuck.
- [ ] Repeat near screen edges, on a second monitor and with fractional scaling.
- [ ] Repeat separately with the optional native backend (`LANTERN_USE_WAYLAND=1`), keeping its validation distinct
      from the default XWayland backend.

Before declaring support for the next GNOME major version, review its porting guide, run both smoke tests on that
version, and repeat this manual checklist. Extending `shell-version` alone does not establish end-to-end support.
See the [GNOME 51 porting guide](https://gjs.guide/extensions/upgrading/gnome-shell-51.html) and the
[extension metadata specification](https://gjs.guide/extensions/overview/anatomy.html#shell-version).
