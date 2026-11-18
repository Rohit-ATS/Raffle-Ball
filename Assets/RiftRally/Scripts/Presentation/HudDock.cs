using System.Collections.Generic;
using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;
using RiftRally.Scenery;
using RiftRally.Setup;

namespace RiftRally.Presentation
{
    /// <summary>
    /// The near-field HUD: the score, the round, the landscape, and the options, all within
    /// arm reach and out of the line of the throw.
    ///
    /// The game already had signage on the table - a scoreboard beyond the far end and
    /// banners over the house - and that signage is good at announcing things. What it could
    /// not do is be consulted. Anything a player wants to check mid-game, or change mid-game,
    /// was either two metres away at the wrong end of the rink or not reachable at all, and
    /// the lobby that owned the settings disappeared the moment a match began.
    ///
    /// So this is the opposite kind of surface: body-anchored rather than world-anchored,
    /// close, and persistent. Three decisions make it work.
    ///
    /// It sits to the lower left rather than straight ahead. Centring a panel in front of the
    /// player is the obvious thing and it is wrong here: the corridor from the player to the
    /// house is where the entire game happens - the stone, the pull, the aim line - and
    /// anything in it is in the way. Off to one side and below eye level is empty space in
    /// this game, and it is still inside the comfortable reach cone for a direct-touch pinch.
    ///
    /// It is comfort-tethered, not head-locked. The dock keeps station relative to where the
    /// player is facing, but lazily, with a dead zone and a damped follow, so small head
    /// movements leave it alone and it never swims in the periphery. A panel welded to the
    /// head is the single most reliable way to make someone feel sick in VR.
    ///
    /// And it gets out of the way by itself. Opacity is driven by where the player is looking
    /// and by what the launcher is doing: full when glanced at, faint when the eyes are up at
    /// the house, and all but gone the moment a stone is in hand. That is the part that
    /// answers the actual complaint about HUDs in VR, which is not that they are ugly but
    /// that they are permanently in front of something you are trying to see.
    /// </summary>
    public class HudDock : MonoBehaviour
    {
        [Header("References")]
        public MatchManager Match;
        public StoneLauncher Launcher;
        public Rink Playfield;
        public LandscapeDirector Scenery;

        [Tooltip("The head the dock stations itself against. Defaults to the main camera.")]
        public Transform Viewer;

        [Header("Placement")]
        /// <summary>
        /// Offset from the head in the player heading frame: left, down, forward.
        ///
        /// Tuned against the table rather than picked for looks. Forty centimetres to the
        /// left clears the rink entirely - the table is seventy wide, so anything inside
        /// thirty-five is hanging over the ice where a left-handed pull-back goes - and
        /// twenty-six down puts it below the line from the eye to the house while keeping the
        /// whole dock inside a comfortable sixty-centimetre reach.
        /// </summary>
        [Tooltip("Offset from the head, in the player heading frame: left, down, forward.")]
        public Vector3 Offset = new Vector3(-0.40f, -0.26f, 0.42f);

        [Tooltip("Degrees of head turn tolerated before the dock follows.")]
        public float YawDeadZone = 26f;

        [Tooltip("How quickly the dock catches up once it does follow.")]
        public float FollowSharpness = 2.2f;

        [Header("Visibility")]
        [Tooltip("Opacity when the player is looking at the dock.")]
        public float ReadOpacity = 1f;

        [Tooltip("Opacity when the player is looking elsewhere.")]
        public float IdleOpacity = 0.34f;

        [Tooltip("Opacity while a stone is being aimed or is in flight.")]
        public float ThrowOpacity = 0.06f;

        /// <summary>
        /// Angle from the dock within which the player counts as reading it.
        ///
        /// Has to be read together with <see cref="Offset"/>: from the default offset the
        /// dock sits about forty-nine degrees off straight ahead, so a threshold tighter than
        /// that is what separates looking down at it from looking up the rink. Set it wider
        /// than the offset angle and the dock is simply always at full brightness.
        /// </summary>
        [Tooltip("Angle from the dock within which the player counts as reading it.")]
        public float ReadAngle = 44f;

        /// <summary>True while the options list is open.</summary>
        public bool OptionsOpen { get; private set; }

        private Transform _root;
        private TextPanel _score;
        private TextPanel _turn;
        private TextPanel _place;
        private TextPanel _note;
        private PinchButton _optionsTab;

        private readonly List<PinchButton> _optionButtons = new();
        private readonly List<TextPanel> _labels = new();

        private float _opacity = 1f;
        private float _noteUntil;
        private bool _subscribed;
        private bool _hudHidden;

        private const float PanelWidth = 0.27f;

        private void Start()
        {
            if (Playfield == null) Playfield = Rink.Instance ?? FindFirstObjectByType<Rink>();
            if (Match == null) Match = MatchManager.Instance ?? FindFirstObjectByType<MatchManager>();
            if (Launcher == null) Launcher = FindFirstObjectByType<StoneLauncher>();
            if (Scenery == null) Scenery = LandscapeDirector.Instance;

            Build();
            Subscribe();

            Theme.Changed += OnThemeChanged;
            if (Scenery != null) Scenery.LandscapeChanged += OnLandscapeChanged;

            Refresh();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            Theme.Changed -= OnThemeChanged;
            if (Scenery != null) Scenery.LandscapeChanged -= OnLandscapeChanged;
        }

        // ---------------------------------------------------------------- construction

        private void Build()
        {
            var rootGo = new GameObject("Dock");
            rootGo.transform.SetParent(transform, false);
            _root = rootGo.transform;

            // A backing slab, so the dock is an object in the room rather than text floating
            // in mid air. Text alone over a busy landscape never settles into a readable
            // shape however it is coloured.
            ProceduralArt.Box(_root, "Backing", new Vector3(0f, -0.004f, 0.006f),
                new Vector3(PanelWidth, 0.112f, 0.005f),
                ProceduralArt.MakeTransparentMaterial(Theme.Panel), false);

            ProceduralArt.Box(_root, "Edge", new Vector3(0f, 0.058f, 0.004f),
                new Vector3(PanelWidth, 0.0022f, 0.004f),
                ProceduralArt.MakeGlowMaterial(Theme.PanelEdge, 1.3f), false);

            _score = Track(TextPanel.Create(_root, "Score", new Vector3(0f, 0.032f, 0f),
                                            0.0215f, TextAnchor.MiddleCenter, FontStyle.Bold));
            _turn = Track(TextPanel.Create(_root, "Turn", new Vector3(0f, 0.006f, 0f), 0.0145f));
            _place = Track(TextPanel.Create(_root, "Place", new Vector3(0f, -0.019f, 0f), 0.0115f));

            // A one-line channel under the dock for anything that needs saying right now,
            // which keeps transient messages off the table where the stones are.
            _note = Track(TextPanel.Create(_root, "Note", new Vector3(0f, -0.042f, 0f), 0.0115f));
            _note.HideInstant();

            _optionsTab = PinchButton.Create(_root, "OPTIONS", new Vector3(0f, -0.072f, 0f),
                                             0.13f, 0.026f, Theme.Accent, ToggleOptions);
        }

        private TextPanel Track(TextPanel panel)
        {
            _labels.Add(panel);
            return panel;
        }

        private void Subscribe()
        {
            if (_subscribed || Match == null) return;
            Match.StateChanged += OnStateChanged;
            Match.RoundStarted += OnRoundStarted;
            Match.TurnStarted += OnTurnStarted;
            Match.RoundScored += OnRoundScored;
            Match.MatchEnded += OnMatchEnded;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Match == null) return;
            Match.StateChanged -= OnStateChanged;
            Match.RoundStarted -= OnRoundStarted;
            Match.TurnStarted -= OnTurnStarted;
            Match.RoundScored -= OnRoundScored;
            Match.MatchEnded -= OnMatchEnded;
            _subscribed = false;
        }

        // ---------------------------------------------------------------- options

        public void ToggleOptions()
        {
            if (OptionsOpen) CloseOptions();
            else OpenOptions();
        }

        public void OpenOptions()
        {
            OptionsOpen = true;
            RebuildOptions();
        }

        public void CloseOptions()
        {
            OptionsOpen = false;
            ClearOptions();
            if (_optionsTab != null) _optionsTab.Label = "OPTIONS";
        }

        /// <summary>
        /// Builds the options list above the dock.
        ///
        /// Rebuilt from scratch on every change rather than updated in place. The list is a
        /// dozen buttons whose labels all show live state, and keeping handles to each of
        /// them to poke the text in would be more code and more ways to go stale than simply
        /// throwing the list away and asking the settings what they are again.
        /// </summary>
        private void RebuildOptions()
        {
            ClearOptions();
            if (_optionsTab != null) _optionsTab.Label = "CLOSE";

            float y = 0.085f;
            const float stride = 0.034f;

            // Landscape, with the name between a back and a forward key. This is the control
            // the whole environment rotation hangs off, so it is first and it is the widest.
            // The row position is taken before AddOption advances it, so the two arrow keys
            // sit beside the name rather than a row above it.
            float landscapeRow = y;
            AddOption(ref y, stride, $"LANDSCAPE  {CurrentLandscapeName()}", Theme.Primary,
                      () => StepLandscape(1));

            var back = PinchButton.Create(_root, "<",
                new Vector3(-PanelWidth * 0.5f - 0.028f, landscapeRow, 0f),
                0.042f, 0.028f, Theme.Secondary, () => StepLandscape(-1));
            _optionButtons.Add(back);

            var forward = PinchButton.Create(_root, ">",
                new Vector3(PanelWidth * 0.5f + 0.028f, landscapeRow, 0f),
                0.042f, 0.028f, Theme.Secondary, () => StepLandscape(1));
            _optionButtons.Add(forward);

            AddOption(ref y, stride,
                      $"NEW PLACE EACH GAME  {OnOff(LandscapeRotation.AutoRotate)}",
                      Theme.Secondary, () =>
                      {
                          LandscapeRotation.AutoRotate = !LandscapeRotation.AutoRotate;
                          Note(LandscapeRotation.AutoRotate
                              ? "THE WORLD WILL CHANGE EACH GAME"
                              : "STAYING HERE");
                          RebuildOptions();
                      });

            AddOption(ref y, stride, $"DETAIL  {QualityName()}", Theme.Secondary, CycleQuality);

            AddOption(ref y, stride, $"SOUND  {OnOff(AudioListener.volume > 0.01f)}",
                      Theme.Secondary, () =>
                      {
                          AudioListener.volume = AudioListener.volume > 0.01f ? 0f : 1f;
                          RebuildOptions();
                      });

            AddOption(ref y, stride, $"DIM THE HUD  {OnOff(_hudHidden)}", Theme.Secondary, () =>
            {
                _hudHidden = !_hudHidden;
                RebuildOptions();
            });

            bool playing = Match != null
                           && Match.State != MatchState.Attract
                           && Match.State != MatchState.MatchOver;

            if (playing)
            {
                AddOption(ref y, stride, "END THIS MATCH", Theme.Warn, () =>
                {
                    Match.QuitToAttract();
                    CloseOptions();
                });
            }
            else if (Match != null)
            {
                AddOption(ref y, stride, "PLAY SOLO", Theme.Accent, () =>
                {
                    Match.StartMatch(GameMode.SoloVsAi);
                    CloseOptions();
                });
            }
        }

        private void AddOption(ref float y, float stride, string label, Color tint,
                               System.Action action)
        {
            var button = PinchButton.Create(_root, label, new Vector3(0f, y, 0f),
                                            PanelWidth, 0.028f, tint, action);
            _optionButtons.Add(button);
            y += stride;
        }

        private void ClearOptions()
        {
            foreach (PinchButton button in _optionButtons)
                if (button != null) Destroy(button.gameObject);
            _optionButtons.Clear();
        }

        private void StepLandscape(int delta)
        {
            if (Scenery == null)
            {
                // No world built - still move the rotation on, so the setting is honoured
                // the next time one is.
                LandscapeRotation.Step(delta);
                Note($"NEXT: {CurrentLandscapeName()}");
                RebuildOptions();
                return;
            }

            Landscape landscape = Scenery.Step(delta);
            Note(landscape.Tagline);
            RebuildOptions();
        }

        private void CycleQuality()
        {
            var tuner = FindFirstObjectByType<QualityTuner>();
            if (tuner == null)
            {
                Note("DETAIL IS NOT ADJUSTABLE HERE");
                return;
            }

            QualityTuner.Preset next = tuner.Quality switch
            {
                QualityTuner.Preset.Crisp => QualityTuner.Preset.Balanced,
                QualityTuner.Preset.Balanced => QualityTuner.Preset.Fast,
                _ => QualityTuner.Preset.Crisp
            };

            // Switching by hand turns the automatic step-down off: having the game quietly
            // override a choice the player just made, four seconds later, is worse than
            // letting them keep a setting their device cannot hold.
            tuner.AdaptiveQuality = false;
            tuner.Apply(next);
            RebuildOptions();
        }

        private string QualityName()
        {
            var tuner = FindFirstObjectByType<QualityTuner>();
            return tuner == null ? "-" : tuner.Quality.ToString().ToUpperInvariant();
        }

        private string CurrentLandscapeName()
        {
            if (Scenery != null && Scenery.Current != null)
                return Scenery.Current.Name.ToUpperInvariant();
            return LandscapeRotation.Current.Name.ToUpperInvariant();
        }

        private static string OnOff(bool value) => value ? "ON" : "OFF";

        // ---------------------------------------------------------------- content

        private void OnStateChanged(MatchState state)
        {
            Refresh();

            switch (state)
            {
                case MatchState.Attract:
                    Note("PINCH OPTIONS TO CHANGE THE WORLD");
                    break;
                case MatchState.MatchOver:
                    Note("PINCH TO PLAY AGAIN");
                    break;
            }
        }

        private void OnRoundStarted(int round, Modifiers.RinkModifier modifier)
        {
            Refresh();
            if (modifier != null) Note(modifier.DisplayName.ToUpperInvariant());
        }

        private void OnTurnStarted(PlayerSide player, int stoneNumber) => Refresh();

        private void OnRoundScored(RoundResult result) => Refresh();

        private void OnMatchEnded(PlayerSide winner) => Refresh();

        private void Refresh()
        {
            if (_score == null) return;

            if (Match == null)
            {
                _score.Show("RIFT RALLY", Theme.Primary);
                _turn.Show("", Theme.Text);
                _place.Show(CurrentLandscapeName(), Theme.TextDim);
                return;
            }

            _score.Show($"{Match.RoundsWon[0]}  -  {Match.RoundsWon[1]}", Theme.Text);

            int round = Mathf.Clamp(Match.Round, 1, GameConfig.MaxRounds);

            switch (Match.State)
            {
                case MatchState.Attract:
                    _score.Show("RIFT RALLY", Theme.Primary);
                    _turn.Show("READY", Theme.Text);
                    break;

                case MatchState.MatchOver:
                    _turn.Show("MATCH OVER", Theme.Primary);
                    break;

                default:
                {
                    bool ai = Match.IsAiControlled(Match.CurrentPlayer);
                    string who = ai ? "OPPONENT" : "YOUR THROW";
                    int stone = Mathf.Clamp(Match.StonesThrownThisRound / 2 + 1, 1,
                                            GameConfig.StonesPerPlayerPerRound);

                    _turn.Show($"{who}   ROUND {round}/{GameConfig.MaxRounds}   "
                               + $"STONE {stone}/{GameConfig.StonesPerPlayerPerRound}",
                               Match.CurrentPlayer.ToColor());
                    break;
                }
            }

            _place.Show(CurrentLandscapeName(), Theme.TextDim);
        }

        private void Note(string text)
        {
            if (_note == null || string.IsNullOrEmpty(text)) return;
            _note.Show(text.ToUpperInvariant(), Theme.Secondary);
            _noteUntil = Time.unscaledTime + 4f;
        }

        private void OnLandscapeChanged(Landscape landscape)
        {
            Refresh();
            Note($"{landscape.Name} - {landscape.Tagline}");
        }

        /// <summary>
        /// Re-tints everything when the landscape installs a new palette. The dock is built
        /// once and lives for the session, so it cannot simply read the theme at construction
        /// and be done.
        /// </summary>
        private void OnThemeChanged(Theme.Palette palette)
        {
            if (_root == null) return;

            var backing = _root.Find("Backing");
            if (backing != null)
            {
                var renderer = backing.GetComponent<Renderer>();
                if (renderer != null && renderer.sharedMaterial != null)
                {
                    Material material = renderer.sharedMaterial;
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", palette.Panel);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", palette.Panel);
                }
            }

            var edge = _root.Find("Edge");
            if (edge != null)
            {
                var renderer = edge.GetComponent<Renderer>();
                if (renderer != null)
                    ProceduralArt.SetGlowFade(renderer.sharedMaterial, palette.PanelEdge, 1f, 1.3f);
            }

            if (_optionsTab != null) _optionsTab.Tint = palette.Accent;

            Refresh();
            if (OptionsOpen) RebuildOptions();
        }

        // ---------------------------------------------------------------- station keeping

        private void Update()
        {
            if (DesktopInput.PressedThisFrame(SimKey.ToggleOptions)) ToggleOptions();
            if (DesktopInput.PressedThisFrame(SimKey.NextLandscape)) StepLandscape(1);

            if (_note != null && Time.unscaledTime > _noteUntil) _note.Hide();

            Transform viewer = Viewer != null
                ? Viewer
                : (Camera.main != null ? Camera.main.transform : null);
            if (viewer == null || _root == null) return;

            KeepStation(viewer);
            Fade(viewer);
        }

        /// <summary>
        /// Holds the dock at its offset from the head, lazily.
        ///
        /// The dead zone is what makes this comfortable. Inside it the dock does not move at
        /// all, so reading it - which means turning towards it - does not push it away, and
        /// the small constant motion of a tracked head does not make it drift. Outside it, the
        /// dock damps towards the new heading over most of a second.
        /// </summary>
        private void KeepStation(Transform viewer)
        {
            Vector3 forward = viewer.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            float wantYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float haveYaw = _root.eulerAngles.y;
            float error = Mathf.DeltaAngle(haveYaw, wantYaw);

            float yaw = Mathf.Abs(error) <= YawDeadZone
                ? haveYaw
                : Mathf.LerpAngle(haveYaw, wantYaw,
                                  1f - Mathf.Exp(-FollowSharpness * Time.unscaledDeltaTime));

            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            Vector3 target = viewer.position + heading * Offset;

            _root.position = Vector3.Lerp(_root.position, target,
                                          1f - Mathf.Exp(-FollowSharpness * 2.4f
                                                         * Time.unscaledDeltaTime));

            // Face the head, pitched so it is square-on to a downward glance rather than
            // seen edge-on from above.
            Vector3 toViewer = viewer.position - _root.position;
            if (toViewer.sqrMagnitude > 0.0001f)
                _root.rotation = Quaternion.LookRotation(-toViewer.normalized, Vector3.up);
        }

        /// <summary>
        /// Decides how visible the dock should be, and applies it.
        ///
        /// Three states, in priority order. A stone in hand wins over everything: during the
        /// pull and the release the player is tracking a small object across a long table and
        /// nothing else on screen is welcome. Otherwise, looking at the dock brings it up and
        /// looking away lets it recede - not off, because a scoreboard you cannot find is its
        /// own kind of annoying, but faint enough to be scenery.
        /// </summary>
        private void Fade(Transform viewer)
        {
            float want;

            bool throwing = Launcher != null
                            && (Launcher.State == LauncherState.Aiming
                                || Launcher.State == LauncherState.Thrown);

            if (_hudHidden)
            {
                // Dimmed, never gone. Fading the dock to nothing also fades the button that
                // turns it back on, which leaves a player who tried the setting once with no
                // way back to it short of a keyboard they do not have in a headset.
                want = OptionsOpen ? ReadOpacity : 0.12f;
            }
            else if (throwing && !OptionsOpen)
            {
                want = ThrowOpacity;
            }
            else
            {
                Vector3 toDock = _root.position - viewer.position;
                float angle = toDock.sqrMagnitude > 0.0001f
                    ? Vector3.Angle(viewer.forward, toDock.normalized)
                    : 0f;

                bool reading = angle < ReadAngle || OptionsOpen;
                want = reading ? ReadOpacity : IdleOpacity;
            }

            _opacity = Mathf.MoveTowards(_opacity, want, 3.2f * Time.unscaledDeltaTime);

            foreach (TextPanel label in _labels)
            {
                if (label == null) continue;
                // The note channel fades itself when it times out; scaling it here as well
                // would fight that.
                if (label == _note && label.Opacity <= 0.01f) continue;
                label.SetColor(Theme.At(LabelColour(label), _opacity));
            }

            foreach (PinchButton button in _optionButtons)
                if (button != null) button.Interactable = _opacity > 0.25f;

            // Deliberately below the dimmed opacity above, so the tab stays pressable.
            if (_optionsTab != null) _optionsTab.Interactable = _opacity > 0.05f;

            if (_root.gameObject.activeSelf != _opacity > 0.005f)
                _root.gameObject.SetActive(_opacity > 0.005f);
        }

        /// <summary>
        /// The colour a label wants, before opacity. Looked up per label rather than stored,
        /// so a theme change needs no bookkeeping here.
        /// </summary>
        private Color LabelColour(TextPanel label)
        {
            if (label == _score) return Theme.Text;
            if (label == _turn)
            {
                return Match != null && Match.State != MatchState.Attract
                       && Match.State != MatchState.MatchOver
                    ? Match.CurrentPlayer.ToColor()
                    : Theme.Primary;
            }
            if (label == _place) return Theme.TextDim;
            return Theme.Secondary;
        }
    }
}
