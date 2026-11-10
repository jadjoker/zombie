#!/usr/bin/env bash
# Builds the Godot game for Windows, checks it runs, and pushes it to itch.io with butler.
# Players with the itch app get the update automatically (butler uploads only what changed).
#
#   Tools/godot/release.sh            build, test, push
#   Tools/godot/release.sh --dry-run  build and test only (no upload)
#   Tools/godot/release.sh --zip      also put a zip in Proton Drive
#
# The itch page is set in Tools/godot/itch_target.txt as "user/game" (for example "jadjoker/risk-of-copyright").
# butler must be logged in once: C:/Users/smitt/Tools/butler/butler.exe login
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
GODOT="/c/Users/smitt/Projects/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
BUTLER="/c/Users/smitt/Tools/butler/butler.exe"
OUT="$ROOT/build/windows"
DRIVE="/c/Users/smitt/Proton Drive/jderolf/My files/Game Projects/Risk of Copyright"
DRY=0; ZIP=0
for arg in "$@"; do
	case "$arg" in
		--dry-run) DRY=1 ;;
		--zip) ZIP=1 ;;
		*) echo "unknown option: $arg"; exit 2 ;;
	esac
done

TARGET="$( { tr -d ' \r\n' < "$ROOT/Tools/godot/itch_target.txt"; } 2>/dev/null || true)"
if [ "$DRY" = 0 ] && { [ -z "$TARGET" ] || [[ "$TARGET" != */* ]]; }; then
	echo "Set the itch page in Tools/godot/itch_target.txt as user/game first."; exit 1
fi
VERSION="$(date +%Y.%m.%d)-$(git -C "$ROOT" rev-parse --short HEAD)"
echo "== Risk of Copyright $VERSION"

echo "== build"
dotnet build "$ROOT/godot/RiskOfCopyright.csproj" -nologo -v q | grep -E "error|Error\(s\)"

echo "== export"
rm -rf "$OUT"; mkdir -p "$OUT"
( cd "$ROOT/godot" && "$GODOT" --headless --path . --export-release "Windows Desktop" "$OUT/RiskOfCopyright.exe" > "$ROOT/build/export.log" 2>&1 ) || true
if grep -qiE "^ERROR" "$ROOT/build/export.log" || [ ! -f "$OUT/RiskOfCopyright.pck" ]; then
	grep -iE "^ERROR" "$ROOT/build/export.log" | head; echo "export failed (see build/export.log)"; exit 1
fi
cp "$ROOT/godot/Tools/PLAYTEST_README.txt" "$OUT/README.txt"
# Tells the itch app which file to launch.
printf '[[actions]]\nname = "play"\npath = "RiskOfCopyright.exe"\n' > "$OUT/.itch.toml"

echo "== smoke test (the exported game plays the opening dive and a wave, then quits)"
LOG="$ROOT/build/smoke.log"; rm -f "$LOG"
timeout 300 "$OUT/RiskOfCopyright.exe" --log-file "$(cygpath -w "$LOG")" -- --autotest > /dev/null 2>&1 || true
if ! grep -q "\[autotest\] done" "$LOG" || grep -qE "ERROR|\[autotest\] FAIL" "$LOG"; then
	grep -E "ERROR|FAIL" "$LOG" | head; echo "smoke test failed (see build/smoke.log)"; exit 1
fi
# The real start of a run, nothing helped along: dive in off the board and enemies must come.
LOG2="$ROOT/build/smoke_waveflow.log"; rm -f "$LOG2"
timeout 120 "$OUT/RiskOfCopyright.exe" --headless --log-file "$(cygpath -w "$LOG2")" -- --autotest --waves --waveflow > /dev/null 2>&1 || true
if ! grep -q "\[autotest\] done" "$LOG2" || grep -qE "ERROR|\[autotest\] FAIL" "$LOG2"; then
	grep -E "ERROR|FAIL|waveflow" "$LOG2" | head; echo "wave flow test failed (see build/smoke_waveflow.log)"; exit 1
fi
echo "   passed"

if [ "$ZIP" = 1 ]; then
	echo "== zip to Proton Drive"
	mkdir -p "$DRIVE"
	( cd "$ROOT/build" && python -I - "$VERSION" <<'EOF'
import os, sys, zipfile
name = f"RiskOfCopyright-windows-{sys.argv[1]}.zip"
with zipfile.ZipFile(name, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for d, _, files in os.walk("windows"):
        for f in files:
            full = os.path.join(d, f)
            z.write(full, "RiskOfCopyright/" + os.path.relpath(full, "windows").replace(os.sep, "/"))
print("  ", name)
EOF
	)
	cp "$ROOT/build/RiskOfCopyright-windows-$VERSION.zip" "$DRIVE/"
fi

if [ "$DRY" = 1 ]; then
	echo "== dry run: would push $OUT to ${TARGET:-<user/game>}:windows as $VERSION"
	exit 0
fi

echo "== push to itch: $TARGET:windows"
"$BUTLER" push "$OUT" "$TARGET:windows" --userversion "$VERSION"
"$BUTLER" status "$TARGET:windows"
