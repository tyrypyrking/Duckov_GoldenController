#!/usr/bin/env bash
# Package a shareable, manual-install release of Golden Controller — a single zip
# anyone can drop into Escape from Duckov's Duckov_Data/Mods/ folder. No Steam
# Workshop subscription is needed: 0Harmony.dll is bundled.
#
# Output: dist/release/GoldenController_<date>.zip with this layout:
#   INSTALL.txt                     (read-me; stays OUT of the game)
#   GoldenController/               (copy THIS folder into Duckov_Data/Mods/)
#     DuckovController.dll           (the compiled mod assembly)
#     0Harmony.dll
#     info.ini
#     Settings.json                  (release defaults: AutoAim=Standard, diagnostics off)
#     assets/glyphs/...
#
# Usage:
#   dist/package_release.sh
#
# Environment overrides:
#   RELEASE_AIM_TIER   default Standard. Set Off/Light/Standard/Aggressive/Cheat/Custom.
#   HARMONY_DLL        path to 0Harmony.dll to bundle (default: reference/HarmonyLib_workshop/0Harmony.dll)
#
# This is also the script the GitHub Actions release workflow runs on a tag push.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DATE="$(date +%Y-%m-%d)"
NAME="GoldenController"
ASSEMBLY="DuckovController"                        # compiled DLL name (internal assembly id)
RELEASE_AIM_TIER="${RELEASE_AIM_TIER:-Standard}"   # public default = Standard (never ship Cheat)
HARMONY_DLL="${HARMONY_DLL:-$ROOT/reference/HarmonyLib_workshop/0Harmony.dll}"
OUTDIR="$ROOT/dist/release"
ZIP="$OUTDIR/${NAME}_${DATE}.zip"

echo "==> Building Release..."
dotnet build "$ROOT/src/$ASSEMBLY/$ASSEMBLY.csproj" -c Release > /dev/null
test -f "$ROOT/build/$ASSEMBLY.dll" || { echo "build/$ASSEMBLY.dll missing" >&2; exit 1; }
test -f "$HARMONY_DLL" || { echo "0Harmony.dll not found at $HARMONY_DLL (set HARMONY_DLL=...)" >&2; exit 1; }

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
MOD="$STAGE/$NAME"
mkdir -p "$MOD"

echo "==> Staging mod folder..."
cp "$ROOT/build/$ASSEMBLY.dll" "$MOD/"
cp "$HARMONY_DLL" "$MOD/0Harmony.dll"
# Glyph assets loaded at runtime by GlyphProvider.
if [ -d "$ROOT/dist/assets" ]; then
  mkdir -p "$MOD/assets"
  cp -r "$ROOT/dist/assets/." "$MOD/assets/"
fi
cp "$ROOT/dist/info.ini" "$MOD/info.ini"
# Mod tile shown in the in-game Mods menu (game loads <mod>/preview.png).
[ -f "$ROOT/dist/preview.png" ] && cp "$ROOT/dist/preview.png" "$MOD/preview.png"

# Release Settings.json — derived from dist defaults; force the public aim tier and
# make sure all diagnostics are off in a shipped build.
sed -E "s/(\"Tier\": )\"[A-Za-z]+\"/\1\"$RELEASE_AIM_TIER\"/" \
    "$ROOT/dist/Settings.json" > "$MOD/Settings.json"
sed -i -E \
  -e 's/("DevMode": )true/\1false/' \
  -e 's/("DebugLog": )true/\1false/' \
  -e 's/("UIDumperEnabled": )true/\1false/' \
  "$MOD/Settings.json"
echo "    Release Settings.json:"
grep -E '"(Tier|DevMode|DebugLog|UIDumperEnabled)":' "$MOD/Settings.json" | sed 's/^/      /'

# User-facing install guide (sits at the zip root, not inside the mod folder).
cat > "$STAGE/INSTALL.txt" <<'EOF'
Golden Controller - Full controller support for Escape from Duckov
==================================================================

A drop-in mod. No Steam Workshop subscription needed (Harmony is bundled).

INSTALL
-------
1. Find your game folder:
   - In Steam: right-click "Escape from Duckov" -> Manage -> Browse local files.
   - Typical Windows path:
       C:\Program Files (x86)\Steam\steamapps\common\Escape from Duckov\
   - Steam Deck / Linux (Proton):
       ~/.local/share/Steam/steamapps/common/Escape from Duckov/

2. Inside it, open  Duckov_Data\Mods
   (If the "Mods" folder doesn't exist, create it.)

3. Copy the whole "GoldenController" folder from this zip into "Mods".
   It should look like:
       Escape from Duckov\Duckov_Data\Mods\GoldenController\DuckovController.dll
       ...\GoldenController\0Harmony.dll
       ...\GoldenController\info.ini
       ...\GoldenController\Settings.json
       ...\GoldenController\assets\...

4. Launch the game and plug in a controller.
   If the mod isn't active, enable it in the in-game Mods menu, then restart.

UPDATE:    delete the old GoldenController folder, drop in the new one, restart.
UNINSTALL: delete the GoldenController folder.

CONFIGURATION  (Settings.json, next to the DLL - hot-reloads when you save)
---------------------------------------------------------------------------
Aim assist:  Aim.* knobs, or pick a preset with  AutoAim.Tier :
    Off        - no assist (raw stick aim)
    Light      - gentle aim magnetism, no lock-on
    Standard   - magnetism + slowdown near targets   (default)
    Aggressive - soft lock-on
    Cheat      - hard lock-on incl. through walls
    Custom     - leave individual Aim.*/AutoAim.* values untouched
Rebinding:   every entry under "Bindings" is a Unity control-path string.
UI feel:     Ui.NavRepeatDelaySec / Ui.NavRepeatRateSec / Ui.DragHoldThresholdSec.

CONTROLS  (Xbox layout; on-screen glyphs show the exact button per screen)
-------------------------------------------------------------------------
In-game:   Left stick move - Right stick aim - RT fire - LT aim - RB sprint
           A dash - X reload - Y interact - B put away/cancel
           D-pad up/down switch weapon - D-pad left/right switch ammo/interact
           LS night vision - RS toggle view - Start inventory - Select map
In menus:  D-pad / Left stick move focus - A select/confirm - B back/close
           X context action - Y item menu (hold = details)
           LT/RT switch pane - LB/RB previous/next page or tab

REQUIREMENTS
------------
Escape from Duckov V1.x (Unity 2022.3, Mono). No other mods required.

Both controller and keyboard/mouse work at the same time - whichever you
touched last wins.
EOF

echo "==> Zipping..."
mkdir -p "$OUTDIR"
rm -f "$ZIP"
( cd "$STAGE" && zip -rq "$ZIP" "$NAME" INSTALL.txt )

echo
echo "==> Release ready:"
echo "    $ZIP"
ls -lh "$ZIP" | awk '{print "    size: "$5}'
( cd "$OUTDIR" && sha256sum "$(basename "$ZIP")" | sed 's/^/    sha256: /' )
echo "    contents:"
unzip -l "$ZIP" | sed 's/^/      /'
