using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;
using RiftRally.Modifiers;
using RiftRally.Presentation;
using RiftRally.Scenery;
using RiftRally.Setup;

namespace RiftRally.Trailer
{
    /// <summary>
    /// Films the game. A camera department, a shot list, and an offline frame recorder.
    ///
    /// A trailer for a VR game is usually captured by recording a headset, which gives you
    /// one take, at whatever frame rate the device managed, from a camera held by a human
    /// head. This renders instead: the player runs with <c>-trailer</c>, every shot is a
    /// scripted camera move through the real game, and each frame is written to disk before
    /// the next one is simulated.
    ///
    /// That last part is the important one. <see cref="Time.captureDeltaTime"/> decouples game
    /// time from wall-clock time, so a frame that takes 80 milliseconds to render and encode
    /// still advances the world by exactly one sixtieth of a second. The result is a perfectly
    /// smooth 60 fps film of a game that was never running at 60 fps on this machine, with
    /// true slow motion where the shot calls for it rather than frame blending.
    ///
    /// Nothing here is a special effect staged for the camera. The stones are thrown through
    /// <see cref="StoneLauncher"/>, the hand in the throw shot is a simulated hand the real
    /// input path reads, the modifiers are the real modifiers, and the match at the end is the
    /// game playing itself. The only things this class owns are where the camera is and what
    /// happens when.
    /// </summary>
    public class TrailerDirector : MonoBehaviour
    {
        // ------------------------------------------------------------------ entry point

        /// <summary>True when the player was started with <c>-trailer</c>.</summary>
        public static bool Requested
        {
            get
            {
                foreach (string arg in System.Environment.GetCommandLineArgs())
                    if (arg == "-trailer") return true;
                return false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Requested) return;

            var go = new GameObject("TrailerDirector");
            go.AddComponent<TrailerDirector>();
            DontDestroyOnLoad(go);
        }

        private static string Argument(string name, string fallback)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }

        // ------------------------------------------------------------------ settings

        private int _width = 1920;
        private int _height = 1080;
        private int _fps = 60;
        private float _shotScale = 1f;
        private string _output;

        // ------------------------------------------------------------------ state

        private Camera _camera;
        private DesktopVrSimulator _simulator;
        private Rink _rink;
        private StoneLauncher _launcher;
        private MatchManager _match;
        private ModifierDirector _modifiers;
        private LandscapeDirector _scenery;

        private RenderTexture _target;
        private RenderTexture _resolved;
        private Texture2D _readback;
        private WaitForEndOfFrame _endOfFrame;
        private int _frame;

        /// <summary>Where the camera is looking from and at, in rink-local coordinates.</summary>
        private struct Pose
        {
            public Vector3 Eye;
            public Vector3 Focus;
            public float Fov;

            public Pose(Vector3 eye, Vector3 focus, float fov = 50f)
            {
                Eye = eye;
                Focus = focus;
                Fov = fov;
            }
        }

        private IEnumerator Start()
        {
            _width = int.Parse(Argument("-trailerWidth", "1920"));
            _height = int.Parse(Argument("-trailerHeight", "1080"));
            _fps = int.Parse(Argument("-trailerFps", "60"));

            // Every shot length is multiplied by this. A quarter-length pass
            // renders in a couple of minutes, which is how the framing gets
            // worked out without waiting for the whole film each time.
            _shotScale = float.Parse(Argument("-trailerScale", "1"),
                                     System.Globalization.CultureInfo.InvariantCulture);
            _output = Argument("-trailerOut",
                               Path.Combine(Path.GetTempPath(), "RiftRallyTrailer"));

            Directory.CreateDirectory(_output);

            // Let the bootstrap finish building the world before taking the camera off it.
            yield return null;
            yield return null;

            if (!Bind())
            {
                Debug.LogError("[RiftRally] Trailer: the game did not finish building; "
                               + "nothing to film.");
                Quit(1);
                yield break;
            }

            SetUpRecorder();

            yield return Film();

            Debug.Log($"[RiftRally] Trailer: wrote {_frame} frames at {_width}x{_height} "
                      + $"to {_output}");
            Quit(0);
        }

        private bool Bind()
        {
            _camera = Camera.main;
            _simulator = DesktopVrSimulator.Instance;
            _rink = Rink.Instance;
            _match = MatchManager.Instance;
            _scenery = LandscapeDirector.Instance;

            _launcher = Object.FindAnyObjectByType<StoneLauncher>();
            _modifiers = Object.FindAnyObjectByType<ModifierDirector>();

            return _camera != null && _simulator != null && _rink != null && _launcher != null;
        }

        private void SetUpRecorder()
        {
            // Multisampled, because this game is thin bright lines on dark ice and nothing
            // makes a trailer look cheaper than crawling edges on a slow camera move.
            _target = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGB32)
            {
                name = "RR_TrailerTarget",
                antiAliasing = 4
            };
            _resolved = new RenderTexture(_width, _height, 0, RenderTextureFormat.ARGB32)
            {
                name = "RR_TrailerResolved"
            };
            _readback = new Texture2D(_width, _height, TextureFormat.RGB24, false);

            _camera.targetTexture = _target;
            _camera.allowHDR = true;
            _endOfFrame = new WaitForEndOfFrame();

            // The whole point: one frame of film is one sixtieth of a second of game time,
            // however long the frame actually took to render and encode.
            Time.captureDeltaTime = 1f / _fps;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            // A render takes several minutes and nobody is going to sit watching the window.
            // Without this the player stops updating the moment it loses focus, and the
            // capture stops with it - halfway through, with no error anywhere.
            Application.runInBackground = true;
        }

        private void Quit(int code)
        {
            Time.captureDeltaTime = 0f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(code);
#endif
        }

        // ------------------------------------------------------------------ the shot list

        private IEnumerator Film()
        {
            HideHands();

            // The signage is part of playing and in the way of looking. It is hidden for the
            // scenic acts and brought back for the match at the end, where a live score is
            // exactly what the shot is about.
            ShowSignage(false);

            // ---- act one: the place -------------------------------------------------
            // Open wide and bright, from standing height, with the table small in a large
            // landscape. The first shot has one job: say that this is a real place, and that
            // the game is a table standing in it.
            Biome("Blossom Valley");
            DressTheHouse();

            yield return Move(5.5f,
                new Pose(L(1.70f, -2.30f, 1.05f), L(0f, 0.30f, 0.06f), 46f),
                new Pose(L(0.95f, -1.45f, 0.62f), L(0f, 0.34f, 0.04f), 44f),
                Ease.InOut);

            // A slow orbit that carries the treeline and the peaks across behind the rink.
            Biome("Alpine Dawn");
            DressTheHouse();
            yield return Orbit(5.5f, 2.10f, 0.72f, 205f, 310f, L(0f, 0.26f, 0.04f), 46f);

            // Down to the ice as a stone arrives, ending in slow motion.
            ClearStones();
            PlaceStone(PlayerSide.Two, 0.06f, 0.52f);
            PlaceStone(PlayerSide.Two, -0.09f, 0.40f);

            yield return Together(
                Move(5.0f,
                    new Pose(L(0.72f, -0.35f, 0.46f), L(0.0f, 0.46f, 0.02f), 42f),
                    new Pose(L(0.26f, 0.02f, 0.14f), L(0.0f, 0.46f, 0.02f), 36f),
                    Ease.Out),
                Script(
                    Beat(0.15f, () => ThrowAt(PlayerSide.One, 0.02f, 0.46f, 0f)),
                    Beat(3.4f, () => SlowMotion.Request(0.22f, 1.5f))));

            SlowMotion.Clear();

            // ---- act two: the throw -------------------------------------------------
            // The mechanic, through the real input path: a hand arrives, pinches the docked
            // stone, pulls it back against the aim preview, and lets go.
            Biome("Coral Lagoon");
            ClearStones();
            PlaceStone(PlayerSide.Two, -0.05f, 0.49f);

            yield return Together(
                Move(7.2f,
                    new Pose(L(0.74f, -1.30f, 0.42f), L(0.0f, -0.38f, 0.06f), 40f),
                    new Pose(L(0.46f, -0.86f, 0.30f), L(0.0f, -0.10f, 0.04f), 42f),
                    Ease.InOut),
                ThrowByHand());

            // Riding alongside the stone at ice level: the trail and the rings do the work.
            yield return Follow(4.0f, 0.34f, 0.12f, 34f);

            // ---- act three: the twists ----------------------------------------------
            yield return ModifierShot("Current", "Highland Moor", 6.4f,
                                      aimOffset: -0.075f, speed: 1.15f);
            yield return ModifierShot("Bumpers", "Amber Woods", 6.4f,
                                      aimOffset: 0.085f, speed: 1.35f);
            yield return ModifierShot("Warp", "Red Mesa", 6.8f,
                                      aimOffset: 0.0f, speed: 1.25f);

            // ---- act four: eight worlds ---------------------------------------------
            // Quick cuts, one per biome. These are landscape shots with the rink in them,
            // not table shots with scenery behind: the camera sits high, the horizon is in
            // frame, and each move is different so the sequence does not read as one shot
            // recoloured eight times.
            string[] tour =
            {
                "Alpine Dawn", "Amber Woods", "Red Mesa", "Coral Lagoon",
                "Highland Moor", "Boreal Dusk", "Savanna Dusk", "Blossom Valley"
            };

            for (int i = 0; i < tour.Length; i++)
            {
                Biome(tour[i]);
                ClearStones();
                DressTheHouse();

                switch (i % 4)
                {
                    case 0:     // crane up off the table, horizon rising into frame
                        yield return Move(2.4f,
                            new Pose(L(1.20f, -1.60f, 0.55f), L(0f, 0.3f, 0.05f), 46f),
                            new Pose(L(1.70f, -2.40f, 1.70f), L(0f, 0.3f, 0.02f), 52f),
                            Ease.Out);
                        break;
                    case 1:     // tilt up off the ice to the treeline and the birds
                        yield return Move(2.4f,
                            new Pose(L(0.30f, -1.35f, 0.50f), L(0f, 0.45f, 0.03f), 46f),
                            new Pose(L(0.30f, -1.35f, 0.72f), L(-1.2f, 9.0f, 4.5f), 54f),
                            Ease.InOut);
                        break;
                    case 2:     // low and wide along the table, hills behind the far end
                        yield return Move(2.4f,
                            new Pose(L(0.55f, -1.70f, 0.30f), L(0f, 1.20f, 0.35f), 44f),
                            new Pose(L(0.18f, -1.15f, 0.26f), L(0f, 1.40f, 0.40f), 44f),
                            Ease.InOut);
                        break;
                    default:    // orbit with the sky in the top half of the frame
                        yield return Orbit(2.4f, 2.0f, 0.95f, 150f, 212f,
                                           L(0f, 0.60f, 0.25f), 50f);
                        break;
                }
            }

            // ---- act five: the match ------------------------------------------------
            // The game playing itself, signage live: score, round, modifier announcement.
            Biome("Savanna Dusk");
            ClearStones();
            ShowSignage(true);

            if (_match != null) _match.StartMatch(GameMode.AiVsAi);

            yield return Move(8.0f,
                new Pose(L(1.25f, -1.60f, 0.70f), L(0f, 0.25f, 0.05f), 48f),
                new Pose(L(-0.95f, -1.45f, 0.58f), L(0f, 0.32f, 0.05f), 48f),
                Ease.InOut);

            // The pull-back: the table becomes small and the place becomes large. The last
            // thing a trailer says should be the thing the game is actually about.
            ShowSignage(false);
            yield return Move(7.0f,
                new Pose(L(0.0f, -1.30f, 0.45f), L(0f, 0.40f, 0.04f), 46f),
                new Pose(L(0.0f, -5.20f, 2.30f), L(0f, 0.40f, 0.12f), 52f),
                Ease.InOut);

            yield break;
        }

        // ------------------------------------------------------------------ shot helpers

        /// <summary>Rink-local to world. Every camera position in the shot list is relative
        /// to the table, so the whole film survives the rink being moved.</summary>
        private Vector3 L(float x, float z, float height) => _rink.RinkToWorld(x, z, height);

        private enum Ease { Linear, In, Out, InOut }

        private static float Shape(Ease ease, float t) => ease switch
        {
            Ease.In => t * t,
            Ease.Out => 1f - (1f - t) * (1f - t),
            Ease.InOut => t * t * (3f - 2f * t),
            _ => t
        };

        /// <summary>A camera move from one pose to another.</summary>
        private IEnumerator Move(float seconds, Pose from, Pose to, Ease ease)
        {
            return Play(seconds, t =>
            {
                float e = Shape(ease, t);
                Aim(Vector3.Lerp(from.Eye, to.Eye, e),
                    Vector3.Lerp(from.Focus, to.Focus, e),
                    Mathf.Lerp(from.Fov, to.Fov, e), t);
            });
        }

        /// <summary>A circular move around a point, at a fixed height.</summary>
        private IEnumerator Orbit(float seconds, float radius, float height,
                                  float fromDegrees, float toDegrees, Vector3 focus, float fov)
        {
            Vector3 centre = L(0f, 0.1f, 0f);

            return Play(seconds, t =>
            {
                float angle = Mathf.Lerp(fromDegrees, toDegrees, Shape(Ease.InOut, t))
                              * Mathf.Deg2Rad;
                Vector3 eye = centre + new Vector3(Mathf.Cos(angle) * radius, height,
                                                   Mathf.Sin(angle) * radius);
                Aim(eye, focus, fov, t);
            });
        }

        /// <summary>
        /// Rides alongside the fastest moving stone. Written to follow whatever is actually
        /// sliding rather than a scripted path, so the shot cannot drift off the action when
        /// the physics does something slightly different from last time.
        /// </summary>
        private IEnumerator Follow(float seconds, float sideOffset, float height, float fov)
        {
            return Play(seconds, t =>
            {
                Stone stone = FastestStone();
                Vector3 focus = stone != null
                    ? stone.transform.position
                    : L(0f, 0.46f, 0.01f);

                Vector3 right = _rink.Surface.right;
                Vector3 eye = focus + right * sideOffset + Vector3.up * height
                              - _rink.DownRink * 0.14f;

                Aim(eye, focus, fov, t, local: false);
            });
        }

        /// <summary>
        /// One modifier, one throw, one camera move - and the modifier's own reveal on the
        /// signage, because the announcement is part of how the twist reads in play.
        /// </summary>
        private IEnumerator ModifierShot(string modifierName, string biome, float seconds,
                                         float aimOffset, float speed)
        {
            Biome(biome);
            ClearStones();
            PlaceStone(PlayerSide.Two, 0.0f, 0.46f);
            PlaceStone(PlayerSide.Two, 0.10f, 0.56f);

            Activate(modifierName);

            return Together(
                Move(seconds,
                    new Pose(L(0.40f, -0.85f, 0.26f), L(0f, 0.1f, 0.02f), 44f),
                    new Pose(L(0.14f, -0.20f, 0.14f), L(0f, 0.46f, 0.01f), 40f),
                    Ease.InOut),
                Script(
                    Beat(0.9f, () => ThrowAt(PlayerSide.One, aimOffset, 0.46f, speed)),
                    Beat(seconds - 1.2f, () => SlowMotion.Request(0.3f, 1.0f))));
        }

        /// <summary>
        /// The throw shot: a hand arrives at the docked stone, pinches, pulls back and
        /// releases, driven through the simulated hand the real input path reads. Nothing
        /// about the launch is faked for the camera.
        /// </summary>
        private IEnumerator ThrowByHand()
        {
            _launcher.BeginTurn(PlayerSide.One);
            yield return null;

            Vector3 dock = _rink.LaunchPointWorld(PlayerSide.One);
            Vector3 back = -_rink.DownRink;
            Vector3 right = _rink.Surface.right;

            ShowHands();

            // Reach in.
            yield return Play(0.9f, t =>
            {
                Vector3 start = dock + back * 0.22f + right * 0.16f + Vector3.up * 0.12f;
                _simulator.SetHand(HandSide.Right,
                                   Vector3.Lerp(start, dock, Shape(Ease.InOut, t)), false);
            });

            // Pinch, and let the launcher notice.
            _simulator.SetHand(HandSide.Right, dock, true);
            yield return Play(0.35f, _ => _simulator.SetHand(HandSide.Right, dock, true));

            // Pull back and slightly across: the aim preview draws the line this produces.
            yield return Play(1.5f, t =>
            {
                float e = Shape(Ease.Out, t);
                Vector3 pulled = dock + back * (0.21f * e) + right * (0.035f * e)
                                 + Vector3.up * (0.004f * e);
                _simulator.SetHand(HandSide.Right, pulled, true);
            });

            // Hold at full draw - a beat the eye needs to read the aim line.
            Vector3 held = dock + back * 0.21f + right * 0.035f;
            yield return Play(0.5f, _ => _simulator.SetHand(HandSide.Right, held, true));

            // Release.
            _simulator.SetHand(HandSide.Right, held, false);
            yield return Play(0.25f, _ => _simulator.SetHand(HandSide.Right, held, false));

            // If the launcher did not take it - a pull a hair under the minimum, say - throw
            // it anyway rather than letting the shot die with a stone still on the dock.
            if (_launcher.State != LauncherState.Thrown) ThrowAt(PlayerSide.One, 0f, 0.46f, 1.2f);

            yield return Play(1.1f, t =>
            {
                Vector3 withdraw = held + back * (0.10f * t) + Vector3.up * (0.05f * t);
                _simulator.SetHand(HandSide.Right, withdraw, false);
            });

            HideHands();
        }

        /// <summary>
        /// Runs two sequences over the same frames: a camera move and a list of things that
        /// happen at given times inside it. Shots are much easier to time this way than by
        /// splitting every move at every event.
        /// </summary>
        private IEnumerator Together(IEnumerator camera, IEnumerator action)
        {
            bool cameraAlive = true, actionAlive = true;

            while (cameraAlive || actionAlive)
            {
                // The camera drives the frame loop; the action rides along on its frames.
                if (actionAlive) actionAlive = action.MoveNext();
                if (cameraAlive) cameraAlive = camera.MoveNext();

                if (!cameraAlive && actionAlive)
                {
                    // Out of camera, but the action has not finished: let it run to the end
                    // on its own frames rather than cutting mid-event.
                    while (action.MoveNext()) yield return action.Current;
                    break;
                }

                yield return cameraAlive ? camera.Current : null;
            }
        }

        /// <summary>
        /// One timed event inside a shot. This exists as a method rather than the tuple
        /// being written inline because a tuple literal containing a lambda will not convert
        /// to a named-tuple parameter type; naming the parameters here does it instead.
        /// </summary>
        private static (float at, System.Action what) Beat(float at, System.Action what)
            => (at, what);

        /// <summary>A list of (time, action) pairs played against the clock.</summary>
        private IEnumerator Script(params (float at, System.Action what)[] beats)
        {
            float elapsed = 0f;
            int next = 0;

            while (next < beats.Length)
            {
                while (next < beats.Length && elapsed >= beats[next].at * _shotScale)
                {
                    beats[next].what?.Invoke();
                    next++;
                }

                elapsed += 1f / _fps;
                yield return null;
            }
        }

        // ------------------------------------------------------------------ the frame loop

        /// <summary>
        /// Runs <paramref name="perFrame"/> once per frame of film for the given duration,
        /// and records the frame that results. Everything in the shot list ends up here.
        /// </summary>
        private IEnumerator Play(float seconds, System.Action<float> perFrame)
        {
            int frames = Mathf.Max(1, Mathf.RoundToInt(seconds * _shotScale * _fps));

            for (int i = 0; i < frames; i++)
            {
                float t = frames <= 1 ? 1f : i / (float)(frames - 1);
                perFrame?.Invoke(t);

                // The pose is written in LateUpdate and the frame renders after that, so the
                // capture has to wait for the end of the frame to see what was drawn.
                yield return _endOfFrame;
                Capture();
            }
        }

        private void Capture()
        {
            Graphics.Blit(_target, _resolved);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _resolved;
            _readback.ReadPixels(new Rect(0f, 0f, _width, _height), 0, 0, false);
            _readback.Apply(false);
            RenderTexture.active = previous;

            // JPEG rather than PNG, at a quality where the difference is invisible after the
            // encode to video: a ninety-second trailer is five and a half thousand frames,
            // and lossless ones would be twenty gigabytes of intermediate files.
            File.WriteAllBytes(Path.Combine(_output, $"frame_{_frame:D5}.jpg"),
                               _readback.EncodeToJPG(95));
            _frame++;
        }

        /// <summary>
        /// Points the camera. A little handheld drift is added on top of every move: a camera
        /// on a perfect mathematical path reads as a screensaver, and the amount here is far
        /// below what anyone notices consciously.
        /// </summary>
        private void Aim(Vector3 eye, Vector3 focus, float fov, float phase, bool local = true)
        {
            float wobble = (_frame + phase) * 0.021f;
            eye += new Vector3(Mathf.Sin(wobble * 1.7f), Mathf.Sin(wobble * 2.3f + 1.1f),
                               Mathf.Sin(wobble * 1.3f + 2.7f)) * 0.0045f;

            Vector3 direction = focus - eye;
            if (direction.sqrMagnitude < 1e-6f) direction = _rink.DownRink;

            _simulator.SetHeadPose(eye, Quaternion.LookRotation(direction.normalized,
                                                                Vector3.up));
            _camera.fieldOfView = fov;
        }

        // ------------------------------------------------------------------ scene control

        private void Biome(string name)
        {
            Landscape landscape = LandscapeCatalog.ByName(name);
            if (_scenery != null && landscape != null) _scenery.Build(landscape);
        }

        private void ClearStones() => _rink.ClearStones();

        /// <summary>Puts a few stones in the house so a shot is never of an empty rink.</summary>
        private void DressTheHouse()
        {
            ClearStones();
            PlaceStone(PlayerSide.One, 0.03f, 0.47f);
            PlaceStone(PlayerSide.Two, -0.07f, 0.52f);
            PlaceStone(PlayerSide.One, 0.11f, 0.38f);
            PlaceStone(PlayerSide.Two, -0.13f, 0.33f);
        }

        private void PlaceStone(PlayerSide owner, float x, float z)
        {
            _launcher.SpawnSettledStone(owner, _rink.RinkToWorld(x, z, 0f));
        }

        /// <summary>
        /// Throws at a point on the ice. The speed, when not given, is the one the game's own
        /// AI would choose for the distance - the same square root of twice the ice friction
        /// times the distance - so a scripted throw behaves like a played one.
        /// </summary>
        private void ThrowAt(PlayerSide side, float x, float z, float speed)
        {
            Vector3 from = _rink.LaunchPointWorld(side);
            Vector3 to = _rink.RinkToWorld(x, z, 0f);

            if (speed <= 0f)
            {
                float distance = Vector3.Distance(from, to);
                speed = Mathf.Sqrt(2f * GameConfig.IceFriction * distance);
            }

            speed = Mathf.Clamp(speed, GameConfig.MinLaunchSpeed, GameConfig.MaxLaunchSpeed);
            _launcher.ThrowForAi(side, (to - from).normalized, speed);
        }

        private Stone FastestStone()
        {
            Stone best = null;
            float fastest = 0f;

            foreach (Stone stone in _rink.Stones)
            {
                if (stone == null) continue;
                if (stone.Speed <= fastest) continue;
                fastest = stone.Speed;
                best = stone;
            }

            return best;
        }

        /// <summary>Turns a modifier on by name, without running a round to get to it.</summary>
        private void Activate(string name)
        {
            if (_modifiers == null) return;

            _modifiers.DeactivateAll();

            for (int round = 1; round <= 6; round++)
            {
                RinkModifier modifier = _modifiers.SelectForRound(round);
                if (modifier != null &&
                    modifier.GetType().Name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
            }
        }

        /// <summary>
        /// Shows or hides the table signage and the near-field dock.
        ///
        /// Both are designed to sit inside a seated player's reach, which puts them directly
        /// between a cinematic camera and the rink. They belong in the match shots, where a
        /// live score is the point, and nowhere else in the film.
        /// </summary>
        private void ShowSignage(bool visible)
        {
            // Hunted by name across the whole scene rather than through the components that
            // built them. Both the scoreboard and the dock parent their furniture to the rink
            // surface under a root of their own, so switching off the component's GameObject
            // - the obvious thing, and the first thing tried here - leaves every panel
            // standing exactly where it was, in front of the lens.
            foreach (Transform node in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (node == null) continue;
                if (node.name == "Hud" || node.name == "Dock" || node.name == "MenuPanel")
                    node.gameObject.SetActive(visible);
            }

            foreach (MatchHud hud in Object.FindObjectsByType<MatchHud>(FindObjectsSortMode.None))
                hud.enabled = visible;

            foreach (HudDock dock in Object.FindObjectsByType<HudDock>(FindObjectsSortMode.None))
                dock.enabled = visible;

            // The lobby panel as well. The game opens in its attract state, so the menu is
            // standing on the table in every shot until something tells it not to be - which
            // is correct for a player putting the headset on, and wrong for a camera.
            foreach (MainMenu menu in Object.FindObjectsByType<MainMenu>(FindObjectsSortMode.None))
            {
                if (!visible) menu.Hide();
                menu.enabled = visible;
            }
        }

        private void ShowHands()
        {
            _simulator.SetHandTracked(HandSide.Right, true);
        }

        private void HideHands()
        {
            _simulator.SetHandTracked(HandSide.Left, false);
            _simulator.SetHandTracked(HandSide.Right, false);
        }

        private void OnDestroy()
        {
            Time.captureDeltaTime = 0f;

            if (_camera != null) _camera.targetTexture = null;
            if (_target != null) _target.Release();
            if (_resolved != null) _resolved.Release();
        }
    }
}
