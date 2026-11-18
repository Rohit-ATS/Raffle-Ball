# Rift Rally — MetaVR Hackathon submission

**A hand-tracked table game for Meta Quest. No controllers: the whole game is pinch, pull,
release.**

| | |
|---|---|
| **Platform** | Meta Quest 2 / 3 / 3S / Pro — standalone, hand tracking required |
| **Build** | `Build/Quest/RiftRally.apk` (48 MB, `com.metavrhackathon.riftrally`) |
| **Engine** | Unity 6 (6000.6.9f1), URP, OpenXR, XR Hands |
| **Backend** | Node multiplayer + leaderboard service on Render |
| **Repository** | https://github.com/Rohit-ATS/METAVR_Game |
| **Trailer** | `Media/RiftRally_Trailer.mp4` (90 s, 1080p60) |
| **Tests** | 98 edit-mode, 48 play-mode, run headless in CI-style batch mode |

---

## What it is

You sit at a glowing rink the size of a coffee table. You pinch a stone, pull it back like a
slingshot and let go. Closest stone to the centre takes the round — and from round two
onwards the rink itself starts fighting back.

It is curling's scoring rule, a slingshot's input, and a table you can lean over, in a
landscape that changes every game.

**One minute of play:** pinch the docked stone, pull back along the ice, release. Pull further
for power, pull off-centre for spin. Three stones each, best of three rounds. Only the side
with the closest stone scores, and it scores one point for every stone of its own that is
closer than the opponent's best. Lose a round and you get the hammer — the last stone — in the
next one.

---

## Why hands, and why a table

The two decisions everything else follows from.

**Hands, because a slingshot is a gesture nobody has to be taught.** Controller input for a
throwing game means mapping a trigger to a grip and a flick to a release, and the player spends
the first five minutes learning the mapping rather than the game. Pinch and pull is the real
gesture: everyone who has flicked a bottle cap across a table already knows it. The cost is
that hand tracking is noisy and intermittent, and most of the input work in this project is
about that — see *Challenges* below.

**A table, because it is the one scale VR gets completely right.** Room-scale needs a room and
a safety boundary; standing play with artificial locomotion makes a sizeable minority of
players ill. A table at arm's length is sitting-down comfortable, has no locomotion at all, and
puts the entire game inside the volume where hand tracking is most accurate. It also means the
game works in a chair in a hotel room, which is where a lot of Quests actually get used.

---

## What is in it

### The throw
Pinch to grab, pull to charge, release to launch. The aim preview draws the line the stone will
take, power comes from pull distance, and spin from how far off-centre the pull is. The input
path is filtered, predicted and grab-assisted — the difference between "this is magic" and
"this is broken" in hand tracking is almost entirely in that layer.

### The twists
Round one is a clean rink. Every round after that reveals a modifier:

- **Cross Current** — a band across the middle pushes sliding stones sideways. Aim upstream, or
  throw harder to spend less time in the flow.
- **Bumper Posts** — three posts close the straight line to the house. Thread a gap, or play a
  deliberate bank shot.
- **Warp Gates** — slide into one gate and come out of the other, along the exit gate's axis at
  the same speed. Predictable on purpose, so it is a tool rather than a dice roll.

The design rule for every modifier: **it must change where a good player aims.** A twist that
only changes how the rink looks makes rounds feel varied while playing identically.

### Online
1v1 by four-character room code, or quick match against a stranger. One leaderboard ranks
guests and registered accounts together, and a guest who signs up keeps the rating they already
earned. The server is authoritative for match state and scoring; the client rebuilds the rink
from the server's snapshot through exactly the same code path that creates a locally thrown
stone, so an online stone and an offline stone are the same object.

### The world
Eight landscapes in rotation — Alpine Dawn, Amber Woods, Red Mesa, Coral Lagoon, Highland Moor,
Boreal Dusk, Savanna Dusk, Blossom Valley — and the rotation rolls rather than shuffles, so you
see eight different places before any of them comes round again.

**Everything is generated at runtime. The repository contains no textures, meshes, audio or
binary art of any kind.** The terrain is a noise heightmap flattened to level ground under the
table; the trees are built per biome from prisms, cones and faceted blobs; the ground and sky
are baked to 4096 × 4096 at load from the biome's own terrain function; the music in the
trailer is synthesised by a Python script in `Tools/Trailer/`.

---

## How it was built

| Layer | Approach |
|---|---|
| **Input** | XR Hands via OpenXR, one-euro filtered, with velocity prediction and a grab-assist radius. Desktop mouse/keyboard fallback for development. |
| **Game** | A match state machine (`MatchManager`), a launcher that both the player and the AI throw through (`StoneLauncher`), and modifiers as components that register forces on sliding stones. |
| **Art** | Entirely procedural. Meshes built in code from `GameConfig` dimensions; materials created at runtime on stock URP shaders; ground and sky baked per biome across every CPU core at load. |
| **Networking** | Node service, WebSocket match channel, HTTP leaderboard, deployed on Render. Guest and account identities share a rating table. |
| **Testing** | 146 tests over geometry, scoring, physics settling, protocol parsing, simulated-hand throws and landscape generation — all runnable headless. |

### Running it

```bash
# Sideload to a Quest with hand tracking enabled
adb install -r Build/Quest/RiftRally.apk

# Or play on a desktop with mouse and keyboard, no headset needed
Build/Windows/RiftRally.exe

# Tests
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode
Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode

# Rebuild the APK
Unity.exe -batchmode -quit -projectPath . \
  -executeMethod RiftRally.EditorTools.RiftRallyBuild.Quest
```

---

## Challenges

**Hand tracking is not a mouse.** Pinch detection flickers, the tracked point jitters by a
centimetre, and the hand is lost entirely whenever it leaves the camera frustum — which, for a
throwing gesture that ends with the hand moving fast, is exactly at the moment of release. The
fixes: a one-euro filter tuned so slow aiming is stable but fast release is not smeared; a
pinch hysteresis so a pinch that wavers does not drop the stone; a tracking grace period so a
momentary loss does not cancel the throw; and release velocity taken from a peak rather than
from the last frame, because the last frame before tracking loss is frequently garbage.

**A black headset with nothing in the logs.** The whole game builds its materials at runtime
with `Shader.Find`. That works perfectly in the Editor, where URP's shaders are always loaded,
and ships a player with no shaders in it at all — every surface drew nothing. It is now fixed at
both ends: the lookup is name-first with a loud error on failure, and the build script adds the
shaders to the always-included list.

**The same lesson, twice more, in the landscape.** Two bugs with the same shape as that one,
both found by filming the game rather than by playing it:

- Every piece of ground cover and both wings of every bird rendered **solid black**. A mesh
  wound both ways shares its vertices between two faces pointing in opposite directions, and
  `RecalculateNormals` averages them to zero — and a lit surface with a zero normal takes no
  light from anywhere. Hundreds of black cards in every biome, no error anywhere.
- The replacement grass, as alpha-cutout cards, then rendered as **solid rectangles in the
  player but correct in the Editor**: URP strips shader variants nothing appears to use, every
  material here is made at runtime so nothing declares that it needs alpha clipping, and the
  variant went with the strip. The grass is real blade geometry now. Geometry cannot be
  stripped.

Both are now covered by tests that fail on a zero-length normal and on a tuft that is not
geometry.

**Shading a sixty-metre world at ninety hertz.** The landscape was first shaded with a
128-pixel colour ramp, which reads correctly in a screenshot and fails in a headset: a surface
with no detail between one metre and sixty has no sense of scale, and stereo vision makes that
worse rather than better. It now bakes a 4096 ground image and normal map per biome, at load,
on every core — with only the large-scale terrain shape coming from the engine's noise on the
main thread, because everything finer is thread-safe noise written for the purpose.

---

## What we are proud of

- **The throw feels right.** It survives bad tracking, and it is the same code path for the
  player, the AI and the network.
- **The game has no assets.** Eight distinct places, all the audio, all the type, built from
  code — and the repository is correspondingly small, reviewable and diffable.
- **It is tested.** 146 headless tests, including play-mode tests that throw stones with a
  simulated hand and assert on where they stop.
- **The trailer is made by the game.** Not a screen recording: the shot list is a script in the
  project, the frames are rendered offline at a fixed timestep, and the music is synthesised.
  No stock footage, no library track, nothing licensed from anywhere.

## What is next

- Two-handed play: a second stone docked on the other side.
- Spectating an online match from a seat beside the rink.
- A per-biome modifier — ice that behaves like the place it is in.
- Passthrough mode, so the rink stands on your actual table.

---

## Judging notes

**Technical difficulty.** Hand-tracked physical input with a release gesture; an authoritative
multiplayer service with guest/account identity merging; a fully procedural renderer-level art
pipeline that holds 90 Hz on a Quest 2, including a 4K texture bake at load that runs across
every core without blocking the main thread for more than the terrain field.

**Polish.** Adaptive quality that steps down after sustained missed frames rather than on a
single long one; slow motion on impacts; a UI palette per biome, chosen for contrast against
that biome's own sky; type set in the best font the device actually has, with a shadow copy
behind every glyph.

**Comfort and accessibility.** Seated, table-scale, no artificial locomotion of any kind, no
controllers to hold or lose, 90 Hz target with headroom, and a desktop build for anyone who
wants to look at it without a headset.

**Originality.** Curling's scoring rule is old; the combination of it with a slingshot gesture,
a rink that changes the rules each round, and a different landscape every game is not.
