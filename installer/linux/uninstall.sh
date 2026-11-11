#!/bin/sh
set -eu

data_dir=${XDG_DATA_HOME:-"$HOME/.local/share"}
install_dir="$data_dir/lantern/application"
desktop_file="$data_dir/applications/com.lantern.app.desktop"
gnome_extension_id="lantern-donut@lantern.sh"
gnome_extension_dir="$data_dir/gnome-shell/extensions/$gnome_extension_id"
python3 - "$data_dir" "$install_dir" <<'PY'
import pathlib
import sys

root, target = map(pathlib.Path, sys.argv[1:])
expected = root.resolve() / 'lantern' / 'application'
if target.is_symlink() or target.resolve() != expected:
    raise SystemExit('Refusing to remove an installation outside its expected directory.')
if (target / '.lantern-package').read_text(encoding='utf-8').strip() != 'com.lantern.app':
    raise SystemExit('The directory is not an installation created by Lantern.')
PY
"$install_dir/Lantern" --uninstall-cleanup
if [ -f "$gnome_extension_dir/.lantern-package" ] && [ "$(cat "$gnome_extension_dir/.lantern-package")" = 'com.lantern.app' ]; then
    if command -v gnome-extensions >/dev/null 2>&1; then
        gnome-extensions disable "$gnome_extension_id" >/dev/null 2>&1 || true
    fi
    rm -rf -- "$gnome_extension_dir"
fi
if [ -L "$HOME/.local/bin/lantern" ] && [ "$(readlink "$HOME/.local/bin/lantern")" = "$install_dir/lantern-cli" ]; then
    rm -- "$HOME/.local/bin/lantern"
fi
if [ -f "$desktop_file" ] && grep -q '^X-Lantern-Managed=true$' "$desktop_file"; then
    rm -- "$desktop_file"
fi
rm -rf -- "$install_dir"
printf '%s\n' 'Lantern was removed. Your settings and conversion results were kept.'
