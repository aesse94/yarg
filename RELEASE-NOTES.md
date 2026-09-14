# YARG custom build — release notes

Built on Windows from `aesse94/yarg`, branch `feature/custom-guitar-mounting`.

| | |
|---|---|
| Repo commit | `223fa74c` — "Add burning hands at full multiplier" |
| On top of | `c72c5666` — "Export characters with per-character type and gender" |
| YARG.Core submodule | `3beb94e5` |
| Unity | 6000.3.5f2 (changeset `3fa8bc678cb0`) |
| Target | StandaloneWindows64 |
| Build define | `YARG_TEST_BUILD` |

---

## Custom content paths on Windows

This is a **test build** (`YARG_TEST_BUILD`), so it reads from the `dev` subfolder,
**not** `release`. Content placed in the release folder will not be seen.

Base path:

```
%USERPROFILE%\AppData\LocalLow\YARC\YARG\dev\custom\
```

| Content | Folder | Extension |
|---|---|---|
| Characters | `...\dev\custom\characters\` | `.yargchar` |
| Guitars | `...\dev\custom\guitars\` | `.glb` |

Both folders are created automatically on first access if missing. The first
launch creating `LocalLow\YARC\YARG\dev` is expected behaviour, not a failure.

Resolved in code as `PathHelper.PersistentDataPath` + `custom`
(`CustomContentManager.CustomizationDirectory`), where `PersistentDataPath` picks
`dev` / `nightly` / `release` from the build define
(`Assets/Script/Helpers/PathHelper.cs`).

Guitars must be plain **glTF `.glb`** — loaded through UniGLTF. `.vrm` files go
down a different path (Vrm10Data) and are not what the guitar loader accepts.

---

## Character → slot mapping

```
ozzy, sting, billy, hayley, rockbot, skeleton  ->  Vocals
hendrix, zakk, nugent                          ->  Guitar
travisbarker                                   ->  Drums
```

**Important caveat:** these names appear nowhere in the codebase — verified by
search. The slot is not keyed off the filename. Each `.yargchar` carries its own
character type, baked in at export time by `c72c5666` ("Export characters with
per-character type and gender"), and the game reads that type out of the bundle.

So the mapping above is a description of *how these particular files were
exported*, not a rule the build enforces. If a character lands in the wrong slot,
the fix is re-exporting it with the right type, not renaming the file.

The engine's slots are `Bass`, `Guitar`, `Drums`, `Vocals`, `Keys`
(`VenueCharacter.CharacterType`). Note **`Bass` and `Keys` are also valid** and
unused by the set above.

Vocalist selection is additionally filtered by the song's tagged vocal gender
(`a521bcce`).

---

## Known limitations

- **Play-mode call sites are compile-verified only.** The code paths that run
  during an actual song were confirmed to compile and pass headless checks. They
  have not been exercised in a real play session.
- **Visual guitar position is unverified.** Mounting resolves and the model
  loads, but whether the guitar *looks* right in the character's hands — offset,
  rotation, scale — has not been eyeballed in-game. Expect to tune.
- **Fire-hands is fresh code.** `BurningHands` is new and lightly tested.
  - It triggers on the **base** multiplier cap, before Star Power doubling.
    Guitar caps at 4x, so flames appear at 4x and again at 8x under Star Power.
    Bass caps at 6x → burns at 6x and 12x.
  - It attaches **by bone name** (`leftHand` / `rightHand`), because the humanoid
    avatar maps on these rigs are scrambled — `HumanBodyBones.LeftHand` resolves
    to the right forearm. A rig using different bone names will not light up.
- An empty guitar selection is a normal state meaning "leave the character's own
  guitar alone" — there is deliberately no "pick the first file" fallback.

---

## Build notes (deviations from the original build plan)

Four things in the handover notes did not match the repo:

1. **`Assets/Packages/` is not committed.** It does not exist in a fresh clone
   and is not in LFS. The NuGet DLLs are restored with
   `dotnet tool install --global NuGetForUnity.Cli && nugetforunity restore`,
   which is what CI does.
2. **ManagedBass is 3.1.1**, not 4.0.2 (`Assets/packages.config`).
3. **`YARG.Core` is a git submodule and a fresh `git clone` leaves it empty.**
   `Packages/manifest.json` depends on it via `file:../YARG.Core/YARG.Core`, so
   the project cannot compile without
   `git submodule update --init --recursive`.
4. **`File > Make Test Build` cannot run headless** — it goes through
   `BuildPlayerWindow.DefaultBuildMethods.GetBuildPlayerOptions()`, which opens
   an interactive folder picker. `Assets/Editor/BatchBuild.cs` was added as a
   batchmode entry point; it replicates the `YARG_TEST_BUILD` define.

Also note the repo requires `git config core.longpaths true` on Windows.

### Reproducing

```bash
git clone --recurse-submodules -b feature/custom-guitar-mounting \
  https://github.com/aesse94/yarg.git
cd yarg
dotnet tool install --global NuGetForUnity.Cli
nugetforunity restore
"C:\Program Files\Unity\Hub\Editor\6000.3.5f2\Editor\Unity.exe" \
  -batchmode -nographics -quit -projectPath . \
  -executeMethod Editor.BatchBuild.BuildWindows64 -logFile build.log
```

---

## Build result

`Result=Succeeded` — 542,530,945 bytes, 6m44s, 0 compile errors, 116 warnings.

Smoke test: launched once, reached the loading/menu stage.
`Player.log` confirms `Initialize engine version: 6000.3.5f2 (3fa8bc678cb0)` and
**no duplicate-asset errors**. `LocalLow\YARC\YARG\dev` was created on first run,
as expected for a `YARG_TEST_BUILD`.

Benign first-run log noise, none of it blocking:

- `settings.json` not found — first run.
- Three `Curl error 28` timeouts — online fetches for genre mappings and song
  sources. The code handles this ("Failed to get newest song genre version.
  Skipping.").
- `DBufferClear shader is not supported on this GPU` — the one counted build
  error; a headless/GPU-less context artifact.
- Missing `StreamingAssets\sources\OpenSource-master\extra\index.json` —
  that source index is not committed to the repo. Pre-existing, unrelated to the
  custom-content commits.

### Guitar mount tests

`YARG.Editor.GuitarMountTests.RunAll` reports **0 passed, 4 failed** on this
machine — but every failure is a missing fixture at a hardcoded Linux path
(`/root/instrument_samples/testbed/...`, left over from the homelab attempt),
which on Windows resolves to `H:\root\...`. The suite exercises nothing here, so
it neither confirms nor contradicts the mounting logic. The "compile-verified
only" caveat above stands.

---

## v5 — custom band + venue selection

### Root cause of "characters never showed up"

The `.yargchar` files were built by an **older exporter**. Three things were wrong
inside them, none of which any amount of rebuilding the player could fix:

1. The prefab was bundled at `assets/vrmimport/<name>_yargchar.prefab`, but every
   loader asks for the fixed path `Assets/_Character.prefab`. The lookup returned
   null and the code silently did `bundle.Unload(true); continue;` — no error
   logged anywhere. This hit the settings dropdowns, `BackgroundManager` (so no
   character in-game) and the preview builder alike.
2. `VenueCharacter.Type` was never stamped, so all ten defaulted to `Bass`.
3. The prefabs carried no `Vrm10Instance`, which the loader requires.

Fixed by re-exporting all ten from source `.vrm` via `VrmBatchBuilder` with an
export map. Stale originals are kept in `custom/characters/_stale-backup/`.

`BundleBackgroundManager.LoadCharacterPrefab()` was also added: it falls back to
the bundle's first GameObject when the canonical path misses, so old bundles
degrade gracefully instead of vanishing silently.

### Any character in any slot

The `venueCharacter.Type == _characterType` filter was removed, so every dropdown
offers all characters. The **slot decides the role**, not the exported type -
`BackgroundManager` replaces the venue member for the chosen slot and re-stamps
the spawned instance's `Type`, so the microphone, animation defaults and guitar
mount all follow the slot. A vocalist put on guitar gets the guitar, not a mic.

New settings: `CustomGuitarCharacter`, `CustomBassCharacter`,
`CustomDrumsCharacter`, `CustomKeysCharacter`.

### Venue selection

`CustomVenue` lists every venue in `dev/venue`. Empty means **Random**, which is
YARG's existing behaviour, so the default changes nothing. Picking a file pins it
for every song. A pinned file that has since been deleted falls back to random.

Note venues only apply when the song does not ship its own - enable
**Disable Per-Song Backgrounds** to force your folder.

### Verification

`YARG.Editor.SmokeTest.Run` - **21 passed, 0 failed**: every slot populated,
cross-slot assignment, serialization of all five new settings, survival across a
save/load round trip, pinned venue loads, random still works. Build
`Result=Succeeded`, launched clean, no errors beyond the known first-run network
timeouts.

---

## v6 — stage audio integrated (items 1-3). Item 4 deliberately deferred.

### Assets

Staged assets were transferred to the gaming PC at `H:\Staged-Assets-Anim-Audio\`
(1,370 files: 40 UI MP3s + README, 227 bank MP3s, 1,101 `.ska.xen`). Only the
`audio/` and `character-animations/` subfolders are in scope; nothing else in that
tree was read or touched.

### Items 1-3: shipped and verified with real audio

Source cues from `audio/ui/` are 20-30s each, so they were trimmed to usable
lengths. No leading silence was present, so each is cut from the start with a
short fade-out to avoid a click at the trim point. Kept as MP3 - BASS decodes it
natively and `.mp3` is already in `SupportedFormats`, so no conversion was needed.

| Sample | Source | Clip | Volume |
|---|---|---|---|
| `SongWin` | `axel_win_1.mp3` | 5.0s, fade from 4.6s | 0.5 |
| `SongLose` | `axel_lose_1.mp3` | 4.0s, fade from 3.6s | 0.5 |
| `MenuNavigate` | `guitar_lick_16.mp3` | 0.35s mono, fade from 0.27s | 0.15 |
| `MenuSelect` | `guitar_lick_20.mp3` | 0.6s mono, fade from 0.48s | 0.2 |

Files live in `Assets/StreamingAssets/sfx/`, so they are part of the build rather
than something dropped in afterwards.

Enum entries are appended after `Rewind`, leaving every existing index untouched,
and `AudioHelpers.SfxSamples` is extended in the same order. A machine check
asserts the two stay positionally aligned (26 entries each).

`ScoreScreenStats.SongFailed` carries fail state to the results screen so a failed
run does not get a victory sting; the lose stinger fires at the moment of failure
instead.

**Verified at runtime:** fresh launch logs `Loaded song_win`, `Loaded song_lose`,
`Loaded menu_navigate`, `Loaded menu_select` - 26 samples, up from 22 - with no
BASS decode errors. `SmokeTest`: 21 passed, 0 failed (no regression to the v5
character/venue work).

### Item 4 (animations): deferred by decision, not failure

Held pending a `.ska` format spec and the vault mapping table, and scoped to
singers first.

What the reconnaissance pass established, which reframes the original plan:

- **The format is Xbox 360 big-endian.** `.xen` = Xenon. Header decoded: offset
  `0x08` is a big-endian uint32 holding the exact file size (`0x1274` = 4724 in a
  4,724-byte file), `0x0C` = `0x20` data offset, `0x10-0x1F` is `ff` padding, and
  the payload contains big-endian IEEE-754 floats (`3f 80 00 00` = 1.0f).
- **The source is per-song choreography, not a state library.** Files are
  organised as `band/singer/male/songs/<songname>/singer_male_<song>_N.ska.xen`.
  There is no Idle / Playing / Mellow / Intense / MicGrab / SpeedAdjustment
  grouping to map onto - those states do not exist in the data. Only
  `rig/defaults/singer_{male,female}_default.ska.xen` looks like a generic pose.

So a correct parser alone would not produce the clips the original task described.
Routing per-song choreography is a design question, deferred to its own sprint.

### Still outstanding: the YARG.Core submodule

`AudioEnums.cs` and `AudioHelpers.cs` belong to `YARG.Core`, a submodule pointing
at upstream `YARC-Official/YARG.Core`, pinned at `3beb94e5`. These edits build
locally but are **not captured by any commit in `aesse94/yarg`** and would be lost
on a fresh clone or `submodule update --init`. Forking YARG.Core and repointing
the submodule is worth doing before more engine-side changes accumulate.
