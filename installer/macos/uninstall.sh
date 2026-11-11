#!/bin/sh
set -eu

install_dir="$HOME/Applications/Lantern.app"
if [ -L "$install_dir" ] || [ ! -d "$install_dir" ]; then
    printf '%s\n' "No ordinary Lantern app bundle was found at $install_dir."
    exit 1
fi
resolved_parent=$(CDPATH= cd -- "$HOME/Applications" && pwd -P)
resolved_app=$(CDPATH= cd -- "$install_dir" && pwd -P)
if [ "$resolved_app" != "$resolved_parent/Lantern.app" ]; then
    printf '%s\n' 'Refusing to remove an app outside the expected Applications directory.'
    exit 1
fi
bundle_id=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$install_dir/Contents/Info.plist")
if [ "$bundle_id" != 'com.lantern.app' ]; then
    printf '%s\n' 'The bundle is not Lantern.'
    exit 1
fi
"$install_dir/Contents/MacOS/Lantern" --uninstall-cleanup
if [ -L "$HOME/.local/bin/lantern" ] && [ "$(readlink "$HOME/.local/bin/lantern")" = "$install_dir/Contents/MacOS/lantern-cli" ]; then
    rm -- "$HOME/.local/bin/lantern"
fi
rm -rf -- "$install_dir"
printf '%s\n' 'Lantern was removed. Your settings and conversion results were kept.'
