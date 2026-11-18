# Rift Rally

A hand-tracked VR table game for Meta Quest. You sit at a glowing rink the size of a coffee
table, pinch a stone, pull it back like a slingshot and let go. Closest stone to the centre
takes the round — and from round two onwards the rink itself starts fighting back.

Play solo against the AI, or 1v1 online — quick match a stranger, or read a four-character
room code out to a friend. Guests and registered accounts share one leaderboard.

Built for the MetaVR hackathon with Unity 6 (URP), OpenXR and XR Hands, with a Node
multiplayer service on Render. No controllers: the whole game is pinch, pull, release.

---

## The trailer, the build, and how to judge it fast

- **Trailer** — `Media/RiftRally_Trailer.mp4`, 81 seconds, 1080p60, no voice-over.
- **Quest build** — `Build/Quest/RiftRally.apk` (48 MB, `com.metavrhackathon.riftrally`).
  `adb install -r Build/Quest/RiftRally.apk`, hand tracking on, developer mode enabled.
- **No headset?** `Build/Windows/RiftRally.exe` plays with mouse and keyboard.
- **Submission write-up** — [SUBMISSION.md](SUBMISSION.md); final checks in
  [docs/SUBMISSION_CHECKLIST.md](docs/SUBMISSION_CHECKLIST.md).

**The trailer is rendered by the game, not recorded from it.** The shot list is a script in
the project ([TrailerDirector.cs](Assets/RiftRally/Scripts/Trailer/TrailerDirector.cs)): the
player is launched with `-trailer`, every camera move and every throw is driven through the
game's own systems - the hand in the throw shot is a simulated hand the real input path reads
- and each frame is written to disk before the next is simulated. `Time.captureDeltaTime`
decouples game time from wall-clock time, so a frame that takes eighty milliseconds to render
and encode still advances the world by one sixtieth of a second: a perfectly smooth 60 fps
film of a game that was never running at 60 fps on that machine, with true slow motion where
the shot asks for it.

The music is synthesised by [compose_music.py](Tools/Trailer/compose_music.py) - oscillators,
filters, an FFT convolution reverb - and the cut, titles and grade are assembled by
[assemble_trailer.py](Tools/Trailer/assemble_trailer.py). Nothing in the trailer is licensed
from anywhere, which is the same rule the game itself follows.

```bash
Build/Windows/RiftRally.exe -trailer -trailerOut frames -trailerWidth 1920 -trailerFps 60
python Tools/Trailer/compose_music.py --out trailer_music.wav --length 81
python Tools/Trailer/assemble_trailer.py --frames frames --music trailer_music.wav        --out Media/RiftRally_Trailer.mp4
```

---

## How it plays

| | |
|---|---|
| **Throw** | Pinch the docked stone, pull back along the ice, release. Pull further for more power; pull off-centre for spin. |
| **Score** | Curling's rule: only the side with the closest stone scores, and it scores one point for every stone of its own that is closer than the opponent's best. |
| **Match** | Best of three rounds, three stones each per round. Losing a round earns the hammer (last stone) in the next one. |
| **Twists** | Round one is a clean rink. Every round after that reveals a modifier that changes where a smart player aims. |
| **Online** | 1v1 by room code or quick match. Leaderboard ranks guests and accounts together; a guest who signs up keeps the rating they already earned. |

### The modifiers

- **Cross Current** — a band across the middle pushes sliding stones sideways. Aim upstream, or throw harder to spend less time in the flow.
- **Bumper Posts** — three posts close the straight line to the house. Thread a gap or play a deliberate bank shot.
- **Warp Gates** — slide into one gate and come out of the other, along the exit gate's axis at the same speed. Predictable on purpose, so it is a tool rather than a dice roll.

The design rule for every modifier is that it must change where a good player aims. A twist
that only changes how the rink looks would make rounds feel varied while playing identically.

---

## The world around the table

The rink used to float in the default blue void, which made a game about a physical table on
a physical surface feel like a tech demo. It now stands in a real place, and in a different
one every game.

Eight landscapes are in rotation:

| | |
|---|---|
| **Alpine Dawn** | Snow line, dark spruce, a low peach sun and falling snow. |
| **Amber Woods** | Late autumn broadleaf, amber light, leaves coming down. |
| **Red Mesa** | High desert, red rock, saguaro, vultures turning overhead. |
| **Coral Lagoon** | White sand, palms, turquoise shallows, gulls working the water. |
| **Highland Moor** | Overcast, heather, standing deadwood, a skein of birds crossing. |
| **Boreal Dusk** | Night pines under an aurora, snow in the air, ravens. |
| **Savanna Dusk** | Gold grass, flat-crowned acacia, a huge sun on the horizon. |
| **Blossom Valley** | Spring hills, cherry blossom, petals drifting, water in the low ground. |

**The rotation rolls rather than shuffles.** Picking at random out of eight repeats the same
biome back to back about one game in eight, which reads to a player as the feature being
broken — they were promised somewhere new and got the place they just left. Walking the list
in order guarantees eight different environments before any of them returns, and the position
is saved, so coming back tomorrow continues the tour. It can be pinned or stepped by hand from
the options dock or the lobby.

**Everything is generated, including the birds.** Terrain is a noise heightmap flattened to
level ground under the table; trees are built per biome from prisms, cones and faceted blobs;
the sky is an inside-out dome. The flocks are three triangles a bird, and almost all of the
life in them is in the flight path — birds bank into their turns, lose height on the outside
of a curve, and beat their wings in bursts with glides between, because a bird on a
constant-speed circle reads as a fairground ride. Still no binary assets: the repository
carries no textures, meshes or audio.

**The ground and the sky are baked at 4K, at load time, from the biome's own terrain.** The
landscape used to be shaded by a 128-pixel colour ramp, which is a very cheap way to paint a
hillside and reads correctly in a screenshot. In a headset it fails in a particular way: a
surface with no detail anywhere between one metre and sixty has no sense of scale, so the
hills read as a painted backdrop rather than as ground. Stereo vision makes that worse rather
than better — both eyes agree the shape is distant, and flat colour gives them nothing to
converge on.

So each biome now bakes a 4096 × 4096 ground image and a matching normal map, addressed by a
planar world UV: grass in the hollows, bare rock on the faces that are genuinely too steep to
hold soil, a bleached ridge line, light and shade across tens of metres, metre-scale patchwork
and decimetre grain — about one texel per three centimetres of a hundred-and-twenty-metre
world. The sky is baked the same way, and in two dimensions rather than one, so it is no
longer the same backdrop whichever way you turn: cirrus banding, a bright quarter around the
sun's bearing, stars in the night biomes. Bark, leaf mass, rock and grass are baked as tiling
surfaces, shared between biomes and tinted per biome, and the ground cover is alpha-clipped
blades rather than solid rectangles.

Two details worth knowing about. The ground's colouring is read from the hill the terrain
*would* have been without the flattening under the table, because colouring from the flattened
field put a plateau of valley colour under the rink and a ring of false slope around it, which
the rock rule then painted grey — a target ring on the ground in every biome. And the bake is
fast enough to do at load because only the large-scale shape comes from the engine's noise, on
the main thread at a coarse resolution; everything finer is hand-rolled hashed noise with no
shared state, generated across every core at once.

The budget scales: 4096 on a desktop GPU, 2048 on a standalone headset, 512 once the frame
rate says so, and dropping a preset frees what the old one was holding. `Rift Rally → Export
Landscape Textures` writes every bake out as a PNG, which is also how the store screenshots
get made.

**The interface recolours with the landscape.** Each biome carries its own UI palette, chosen
against its own sky rather than alongside it — the bright biomes get darker, more opaque
panels and the dusk biomes get lighter text. Labels are set in the best font the device
actually has (Roboto on a Quest, a Segoe or Bahnschrift weight on Windows, requested by name
at runtime) with a dark offset copy behind every glyph, because the one background a label can
no longer count on is a dark one.

### The HUD you can reach

The table signage announces things well but could never be *consulted*: the scoreboard was two
metres away at the far end, and the settings lived in a lobby that vanished the moment a match
began. There is now a near-field dock as well, and it works before and during a game.

- **Off to the lower left, not straight ahead.** The corridor from the player to the house is
  where the whole game happens — stone, pull, aim line — so anything in it is in the way.
  Forty centimetres to the left clears the table entirely and is still an easy reach.
- **Comfort-tethered, not head-locked.** It keeps station relative to where the player is
  facing, but lazily, with a yaw dead zone and a damped follow, so small head movements leave
  it alone. A panel welded to the head is the fastest way to make someone feel sick in VR.
- **It gets out of the way by itself.** Opacity follows the gaze — full when glanced at, faint
  when the eyes are up at the house — and drops to almost nothing the instant a stone is in
  hand. That is the real complaint about VR HUDs: not that they are ugly, but that they are
  permanently in front of something you are trying to look at.
- **Options in a pinch.** Landscape (with back and forward keys), whether the world changes
  each game, detail level, sound, HUD dimming, and starting or ending a match — all reachable
  mid-round without leaving the game.

---

## Download size, and what "4K" means here

The Quest build is **46 MB**. There is nothing to compress: the rink, the stones, the rings,
the hands, the HUD and every effect are generated in code at runtime, and all seven sounds are
synthesised at load. The repository carries no textures, no meshes and no audio files, so the
APK is essentially just the Unity runtime.

On resolution, the honest version: a headset has a fixed panel, so a game cannot output 4K to
it. What it controls is the size of the eye buffer rendered *before* that buffer is sampled
onto the panel. Rendering above panel resolution and downsampling — supersampling — is what
removes shimmer from thin bright geometry, and this game is almost entirely thin bright
geometry: ring outlines, aim lines, trails. So that is the dial that matters here, and
[`QualityTuner.cs`](Assets/RiftRally/Scripts/Setup/QualityTuner.cs) sets it:

| Preset | Eye texture scale | MSAA | For |
|---|---|---|---|
| Crisp | 1.4× | 4× | Quest 3 and up |
| Balanced *(default)* | 1.2× | 4× | Quest 2, comfortable headroom |
| Fast | 1.0× | 2× | fallback when frames are being missed |

MSAA rather than post-process antialiasing, because on a tiled mobile GPU it is nearly free
and, unlike FXAA or TAA, it does not blur — and a smeared aim line in a throwing game is lost
information. Quality steps itself down automatically if the frame budget is missed for four
seconds straight; holding the refresh rate matters more than any of this, because dropping
frames in VR is not "choppy", it is nauseating.

## Running it

### In the Editor

1. Open the project in **Unity 6000.6.9f1**.
2. **Rift Rally → Build Play Scene** generates `Assets/RiftRally/Scenes/RiftRally.unity` and
   puts it first in the build settings.
3. Press Play. With no headset attached the game falls back to a desktop camera and drives the
   right hand with the mouse — click to pinch — so the whole loop is playable at a desk.

### Windows fallback controls

In a Windows build, press **Enter** to start a solo match. Use **Arrow keys** (or **IJKL**) to
move the right hand, and hold **Ctrl** to pinch, grab, pull and release. The original mouse
controls remain available: hold **Space** while moving the mouse to drive the right hand, and
use left mouse to pinch.

**Tab** opens and closes the options dock, and **N** jumps straight to the next landscape —
useful for looking through all eight without playing eight games.

### On a Quest

```
Unity.exe -batchmode -quit -projectPath . \
  -executeMethod RiftRally.EditorTools.RiftRallyBuild.Quest
```

Produces `Build/Quest/RiftRally.apk` (ARM64, IL2CPP, Vulkan, linear colour). Install with
`adb install -r Build/Quest/RiftRally.apk`. Hand tracking must be enabled on the headset.

> **The project path must be pure ASCII for Android builds.** Android's toolchain refuses any
> path containing non-ASCII characters, which includes a localized OneDrive `Documents` folder
> such as `ドキュメント`. The cheapest fix is an ASCII junction to the same files, which costs
> no disk space:
>
> ```
> mklink /J C:\RiftRallyBuild "<project folder>"
> Unity.exe -batchmode -quit -projectPath C:\RiftRallyBuild >   -executeMethod RiftRally.EditorTools.RiftRallyBuild.Quest
> ```
>
> Editor work, the desktop build and both test suites are unaffected.

A desktop build for quick checks:

```
Unity.exe -batchmode -quit -projectPath . \
  -executeMethod RiftRally.EditorTools.RiftRallyBuild.Windows
```

### Multiplayer

The server lives in [`server/`](server/) and deploys to Render from the
[`render.yaml`](render.yaml) blueprint — see [server/README.md](server/README.md) for the
deploy steps, the API and the protocol. Once deployed, set the service URL on the
`GameBootstrap` component's **Server Url** field and rebuild.

### Tests

```
Unity.exe -runTests -batchmode -projectPath . -testPlatform EditMode  -testResults EditMode.xml
Unity.exe -runTests -batchmode -projectPath . -testPlatform PlayMode  -testResults PlayMode.xml
cd server && npm test
```

EditMode covers the rink geometry, the tuning invariants, the hand-filter behaviour and the
wire format. PlayMode runs real physics frames: stones must stop where the analytic model says
they will (the aim preview and the AI both depend on that), and an AI-versus-AI match is played
end to end at eight times speed to prove the match loop cannot stall. The server suite plays a
complete match between two real WebSocket clients.

---

## How the code is arranged

```
Assets/RiftRally/
  Scripts/
    Core/         GameConfig, Theme, HandInput, OneEuroFilter, PointerInput,
                  DesktopInput, MatchManager, ScoreKeeper
    Gameplay/     Rink, Stone, StoneLauncher, AimPreview, AiOpponent
    Modifiers/    RinkModifier + Current, Bumpers, WarpGates, ModifierDirector
    Presentation/ MatchHud, HudDock, MainMenu, PinchButton, HandVisuals,
                  StoneVfx, HouseGlow, SlowMotion, Easing, TextPanel,
                  Typography, AudioDirector
    Scenery/      Landscape + LandscapeCatalog, LandscapeRotation,
                  LandscapeBuilder, LandscapeDirector, BirdFlock,
                  SceneryArt, SceneryMotion
    Net/          RiftNetClient, NetMatchDriver, NetProtocol
    Setup/        GameBootstrap, RinkBuilder, ProceduralArt, QualityTuner,
                  DesktopVrSimulator
  Editor/         Scene generator, command-line build entry points
  Tests/          EditMode and PlayMode suites
server/           Node multiplayer service (see server/README.md)
```

A handful of decisions are worth knowing before changing anything:

**The throw is a slingshot, not a flick.** Hand-tracking velocity is noisy, and one mis-read
spike would fire a stone off the rink. A pull-back offset is a stable quantity the player can
see and correct before releasing.

**Stones slide in a constrained 2.5D mode.** Gravity is off, Y is frozen and tilt is locked,
so stones behave like discs on a plane. Shots stay repeatable and readable, and the jitter and
tunnelling that plague thin rolling colliders at VR frame rates disappear.

**Friction is constant deceleration, not linear damping.** The stone holds its speed early and
then dies off predictably, which reads as ice. The same equation appears in exactly three
places — the stone, the aim preview and the AI's shot solver — and the PlayMode tests check
they still agree.

**The playfield is generated in code.** `RinkBuilder` builds the table, rings, walls, stones
and modifier props from `GameConfig`, so retuning a dimension cannot leave stale geometry
behind and the repository carries no binary art. The scene holds only the XR rig and one
`GameBootstrap`.

**Audio is synthesised at load.** Impacts are pitched and gained by the real collision impulse,
so a nudge and a hard takeout do not sound the same — the clearest cue the player has that a
shot landed.

**The HUD only listens.** It subscribes to `MatchManager` events and never calls back into it,
which is why the match loop can be driven headlessly by a test or by the attract demo with no
UI attached — and why an online match, driven entirely by the server through
`MatchManager.External*`, lights up the same HUD, audio and house glow without any of them
knowing they are online.

**Hand positions are one-euro filtered.** Raw tracked joints jitter by millimetres every
frame, and a plain low-pass filter trades that jitter for lag, which on a throw is worse. A
one-euro filter smooths hard while the hand is still and barely at all while it moves fast, so
a held stone sits rock steady and a quick pull still tracks the hand. Its beta is unit
dependent and that is the trap — the literature's value assumes pixels, and using it on metres
left 86 mm of lag on a normal pull. There is a test for exactly that.

**Nothing in VR moves the camera.** Slow motion scales time, never the view; impacts flash
lights and emission, never the head. Camera shake and view-space effects are the fastest route
to motion sickness, so the drama is all in the world.

**Menus are physical.** The lobby is a board standing on the table that you press with your
finger and a pinch, not a head-locked panel with a laser pointer. Direct touch is steadier
with hand tracking, it reuses the one gesture the game already teaches, and the room keeps
feeling like a room.

**Your own throw is never networked while you make it.** Online, you throw against local
physics with zero latency and report the result; the opponent replays your launch and then
adopts the reported positions. The server owns turn order and scoring, but client-reported
physics cannot be independently verified, so these fast hackathon matches are retained in match
history without changing wins or Elo. The full reasoning is in
[server/README.md](server/README.md).

---

## Tuning

Nearly every number that affects feel lives in
[GameConfig.cs](Assets/RiftRally/Scripts/Core/GameConfig.cs) — rink size, stone mass, ice
friction, pinch thresholds, pull distance, round count, timings and team colours. The EditMode
tests guard the relationships between them that are easy to break by hand: the pinch release
gap must stay wider than the grab gap, friction must be able to cross the settle threshold
inside the hold window, a full-power throw must reach the back of the rink and a minimum-power
throw must fall short.
