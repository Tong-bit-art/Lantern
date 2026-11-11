#!/bin/sh
set -eu

source_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
install_dir="$HOME/Applications/Lantern.app"
bin_dir="$HOME/.local/bin"
if [ -e "$install_dir" ] || [ -L "$install_dir" ]; then
    printf '%s\n' "An installation already exists at $install_dir. Run uninstall.sh first."
    exit 1
fi
# A lantern link left by an earlier Lantern whose app is gone (moved to the Trash instead of uninstall.sh) is replaced.
# Anything else at that path isn't ours and stays.
stale_link=false
if [ -L "$bin_dir/lantern" ] && [ ! -e "$bin_dir/lantern" ]; then
    case $(readlink "$bin_dir/lantern") in
        */Lantern.app/Contents/MacOS/lantern-cli) stale_link=true ;;
    esac
fi
if [ "$stale_link" = false ] && { [ -e "$bin_dir/lantern" ] || [ -L "$bin_dir/lantern" ]; }; then
    printf '%s\n' "$bin_dir/lantern already exists. Remove or rename it before installing Lantern."
    exit 1
fi
test -x "$source_dir/Lantern.app/Contents/MacOS/Lantern"
mkdir -p "$HOME/Applications" "$bin_dir"
/usr/bin/ditto "$source_dir/Lantern.app" "$install_dir"
# ditto keeps the download's quarantine flag: macOS would then start Lantern from a translocated copy and could stop
# the lantern command without a word (#316).
/usr/bin/xattr -dr com.apple.quarantine "$install_dir" 2>/dev/null || true
if [ "$stale_link" = true ]; then
    rm -f -- "$bin_dir/lantern"
    printf '%s\n' "Replaced the lantern link left by an earlier installation."
fi
ln -s "$install_dir/Contents/MacOS/lantern-cli" "$bin_dir/lantern"
printf '%s\n' "Installed $install_dir" "CLI: $bin_dir/lantern (add $bin_dir to PATH if needed)."
printf '%s\n' 'Open Lantern from Applications. Grant Accessibility and Finder Automation access when requested.'
printf '%s\n' 'If Lantern is already listed and switched on under Accessibility, remove it with the - button and add it again with +.'
