#!/usr/bin/env bash
# venue_check.sh - the four-check suite for a staged venue GLB, as one pass/fail report.
#
#   Tools/venue_check.sh <file.glb> <expected-md5> [pose options]
#
#   pose options (optional, passed to VenueSnapshot for a second, posed render):
#     --camPos x,y,z  --camLookAt x,y,z  [--fov deg]  [--space gltf|unity]  [--probe name]
#
# Order matters, and each guard below exists because a real failure got past it once:
#   0. md5 FIRST     - a corrected scanner still measures the wrong bytes if the file is stale.
#   *. license check - a Unity run that dies on licensing exits before compiling, so
#                      "0 compile errors" from that run is vacuous. Every step checks for it.
#   *. no silent pass - a step whose expected marker line is missing is a FAIL, never a pass.
#   5. orientation   - always reported UNVERIFIED. A global mirror is self-consistent, so no
#                      automated check here can detect it; that needs external ground truth.

set -u
GLB="${1:-}"; WANT_MD5="${2:-}"; shift 2 2>/dev/null || true
POSE_ARGS=(); PROBE=""
while [ $# -gt 0 ]; do
  case "$1" in
    --camPos|--camLookAt|--fov|--space) POSE_ARGS+=("-${1#--}" "$2"); shift 2 ;;
    --probe) PROBE="$2"; POSE_ARGS+=("-probe" "$2"); shift 2 ;;
    *) echo "unknown option: $1"; exit 2 ;;
  esac
done

PROJECT="H:/Yarg Modded"
UNITY="C:/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Unity.exe"
[ -n "$GLB" ] && [ -f "$GLB" ] || { echo "usage: $0 <file.glb> <expected-md5> [pose options]"; exit 2; }

NAME="$(basename "$GLB" .glb)"
STAMP="$(date +%Y%m%d-%H%M%S)"
WORK="H:/venue-check/${NAME}-${STAMP}"
mkdir -p "$WORK/in" "$WORK/out"
REPORT="$WORK/REPORT.txt"
OVERALL=PASS

log()  { echo "$*" | tee -a "$REPORT"; }
fail() { log "  FAIL  $*"; OVERALL=FAIL; }
pass() { log "  PASS  $*"; }

unity() { # unity <logname> <nographics:yes|no> <args...>
  local L="$WORK/$1.log"; local ng="$2"; shift 2
  local extra=(); [ "$ng" = yes ] && extra=(-nographics)
  "$UNITY" -batchmode "${extra[@]}" -quit -projectPath "$PROJECT" "$@" -logFile "$L" >/dev/null 2>&1
  if grep -aq "No valid Unity Editor license" "$L"; then
    fail "$1: Unity LICENSE failure - run did not execute (is Unity Hub running?). Aborting."
    log ""; log "OVERALL: FAIL (license)"; exit 1
  fi
  local cs; cs=$(grep -ac "error CS" "$L")
  if [ "$cs" -gt 0 ]; then
    fail "$1: $cs compile error(s)"; grep -a "error CS" "$L" | head -3 | sed 's/^/        /' | tee -a "$REPORT"
  fi
}

log "venue_check  $NAME  $STAMP"
log "work dir: $WORK"
log ""

# ---- 0. file identity ------------------------------------------------------
log "[0] file identity"
GOT_MD5=$(md5sum "$GLB" | awk '{print $1}')
if [ -z "$WANT_MD5" ]; then
  fail "no expected md5 given - refusing to certify unidentified bytes (got $GOT_MD5)"
elif [ "$GOT_MD5" = "$WANT_MD5" ]; then
  pass "md5 $GOT_MD5"
else
  fail "md5 MISMATCH: expected $WANT_MD5, got $GOT_MD5 - checking the wrong file. Aborting."
  log ""; log "OVERALL: FAIL (identity)"; exit 1
fi
cp -f "$GLB" "$WORK/in/"

# ---- 0b. sliver test -------------------------------------------------------
# v4 passed every render check while drawing nothing but long thin shards. A sliver is a
# triangle whose longest edge is >10x the mesh's median and whose height is <5% of that
# edge (Tools/glb_slivers.py), median taken over PROPER triangles only. Threshold 2% was set
# AFTER measuring - not pre-registered. Calibration after the median fix (k=10): synthetics
# 0%, v5 0.05%, v6=v7 0.63%, full2 8.0%, v4 14.5%.
# KNOWN FRAGILITY: v6/v7 drop from 6.0% at k=8 to 0.63% at k=10 - a cluster of thin triangles
# sits just under the cutoff. A pass near a cliff is weak evidence; check k=8 before trusting.
log ""; log "[0b] sliver test"
SL=$(python "$(dirname "$0")/glb_slivers.py" "$GLB" 2>&1)
log "        $(echo "$SL" | grep "^SLIVERS")"
FRAC=$(echo "$SL" | awk '/^SLIVERS/{print $2}')
if [ -z "$FRAC" ]; then
  fail "sliver test produced no result - GLB likely unparseable: $(echo "$SL" | tail -1)"
elif awk "BEGIN{exit !($FRAC > 0.02)}"; then
  fail "sliver fraction $FRAC > 0.02 - cross-bank index slivers, not venue geometry"
else
  pass "sliver fraction $FRAC <= 0.02"
fi

# ---- 0c. vertex usage ------------------------------------------------------
# v5 passed every other check while drawing 784 of 11,319 vertices (6.9%): a small corner
# of the bank rendered and the rest never referenced. Complements [0b]: slivers catch
# "wrong geometry drawn big", this catches "most of the geometry not drawn at all".
# 25% was proposed before measurement; calibration set: synthetics 100%, full2 99.4%,
# v4 100%, v5 6.9%.
log ""; log "[0c] vertex usage"
VU=$(echo "$SL" | awk '/^VERTUSE/{print $2}')
log "        $(echo "$SL" | grep '^VERTUSE')"
if [ -z "$VU" ]; then
  fail "vertex-usage test produced no result"
elif awk "BEGIN{exit !($VU < 0.25)}"; then
  fail "only $VU of vertices referenced by triangles (<0.25) - index/vertex mapping wrong"
else
  pass "vertex usage $VU >= 0.25"
fi

# ---- 0d. duplicate triangles -----------------------------------------------
# v7 emitted 5,741 triangles of which 3,078 (54%) repeat an earlier triangle's exact corner
# positions. Duplicates render identically, so the image looks solid, and they inflate
# shared-edge counts - v7 passed [0b] and [0c] regardless. Threshold 5% was set by the
# decode side BEFORE measuring anything against it.
log ""; log "[0d] duplicate triangles"
DT=$(echo "$SL" | awk '/^DUPTRIS/{print $2}')
log "        $(echo "$SL" | grep '^DUPTRIS')   $(echo "$SL" | grep '^DUPSPLIT')"
if [ -z "$DT" ]; then
  fail "duplicate-triangle test produced no result"
elif awk "BEGIN{exit !($DT > 0.05)}"; then
  fail "duplicate triangles $DT > 0.05 - fans emitted more than once over the same surface"
else
  pass "duplicate triangles $DT <= 0.05"
fi

# ---- 1. build --------------------------------------------------------------
log ""; log "[1] VenueBundleBuilder"
unity build yes -executeMethod YARG.Editor.VenueBundleBuilder.BuildFromCliArgs \
  -glbDir "$WORK/in" -outDir "$WORK/out" -materials unlit
grep -a "\[VenueBundleBuilder\] \(Imported\|${NAME}:\|Built\|DONE\|No imported\)" "$WORK/build.log" \
  | grep -v "^YARG\|at Assets" | sed 's/.*\[VenueBundleBuilder\] /        /' | tee -a "$REPORT"
BUNDLE=$(ls "$WORK/out"/*.yarground 2>/dev/null | head -1)
if grep -aq "\[VenueBundleBuilder\] DONE ok=1 fail=0" "$WORK/build.log" && [ -n "$BUNDLE" ]; then
  pass "bundle $(basename "$BUNDLE") ($(stat -c%s "$BUNDLE") bytes)"
else
  fail "build did not report DONE ok=1 fail=0, or produced no bundle. Aborting."
  log ""; log "OVERALL: FAIL (build)"; exit 1
fi

# ---- 2. node dump ----------------------------------------------------------
log ""; log "[2] VenueNodeDump"
unity nodedump yes -executeMethod YARG.Editor.VenueNodeDump.Run -file "$BUNDLE"
if grep -aq "\[NodeDump\] renderers=" "$WORK/nodedump.log"; then
  grep -a "\[NodeDump\]" "$WORK/nodedump.log" | head -2 | sed 's/.*\[NodeDump\] /        /' | tee -a "$REPORT"
  grep -aq "allOnVenueLayer=True" "$WORK/nodedump.log" && pass "all renderers on Venue layer" \
    || fail "renderers off the Venue layer - the venue camera will cull them"
else
  fail "no NodeDump output (step did not run)"
fi

# ---- 3. venue check --------------------------------------------------------
log ""; log "[3] VenueVerifier"
mkdir -p "$WORK/gate" && cp -f "$BUNDLE" "$WORK/gate/"
unity gate yes -executeMethod YARG.Editor.VenueVerifier.VerifyAll -dir "$WORK/gate"
grep -a "\[VenueVerify\]" "$WORK/gate.log" | sed 's/.*\[VenueVerify\] /        /' | tee -a "$REPORT"
if grep -aq "\[VenueVerify\] RESULT: 1 passed, 0 failed" "$WORK/gate.log"; then
  pass "venue check"
else
  fail "venue check did not pass (or did not run)"
fi

# ---- 4. snapshot(s) --------------------------------------------------------
snap() { # snap <label> <args...>
  local label="$1"; shift
  local png="$WORK/snapshot_${label}.png"
  unity "snap_$label" no -executeMethod YARG.Editor.VenueSnapshot.Run -file "$BUNDLE" -out "$png" "$@"
  local L="$WORK/snap_$label.log"
  grep -a "\[Snapshot\]" "$L" | sed 's/.*\[Snapshot\] /        /' | tee -a "$REPORT"
  local drawn; drawn=$(grep -aoE "drawn pixels=[0-9]+" "$L" | grep -oE "[0-9]+$" | head -1)
  if [ -z "$drawn" ]; then
    fail "snapshot $label: no output (step did not run)"
  elif [ "$drawn" -eq 0 ]; then
    fail "snapshot $label: 0 pixels drawn - nothing visible from this camera"
  elif [ "$drawn" -ge $((2073600 * 98 / 100)) ]; then
    # A near-full screen is not "rendered", it is junk geometry (or the camera inside a
    # surface) covering everything. riothouse_placed passed "pixels > 0" at 100% fill.
    fail "snapshot $label: $drawn/2073600 pixels (>=98%) - screen-filling, not a venue view"
  else
    pass "snapshot $label: $drawn pixels drawn -> $png"
  fi
  # Sanity bound on geometry extent: a venue is tens of units, not tens of thousands.
  local size; size=$(grep -aoE "geometry center=.* size=\([^)]*\)" "$L" | grep -oE "size=\([^)]*\)" | tr -d 'size=()' | head -1)
  if [ -n "$size" ]; then
    local maxdim; maxdim=$(echo "$size" | tr ',' '\n' | awk '{v=($1<0)?-$1:$1; if(v>m)m=v} END{print int(m)}')
    if [ "$maxdim" -gt 1000 ]; then
      fail "snapshot $label: geometry extent $maxdim units (>1000) - outlier vertices in the mesh"
    fi
  fi
  if [ -n "$PROBE" ] && [ "$label" = posed ]; then
    grep -aq "probe .* CENTRED" "$L" && ! grep -aq "NOT CENTRED" "$L" \
      && pass "probe $PROBE centred" || fail "probe $PROBE NOT centred"
  fi
}

log ""; log "[4] VenueSnapshot (builder default camera)"
snap default
if [ ${#POSE_ARGS[@]} -gt 0 ]; then
  log ""; log "[4b] VenueSnapshot (posed: ${POSE_ARGS[*]})"
  snap posed "${POSE_ARGS[@]}"
fi

# ---- 5. orientation --------------------------------------------------------
log ""; log "[5] orientation"
log "  UNVERIFIED  (a global mirror passes every check above; confirm against external"
log "              ground truth - opening-shot footage, known left/right layout, or texture text)"

log ""; log "OVERALL: $OVERALL   report: $REPORT"
[ "$OVERALL" = PASS ]
