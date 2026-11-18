using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;
using RiftRally.Net;
using RiftRally.Scenery;
using RiftRally.Setup;

namespace RiftRally.Presentation
{
    /// <summary>
    /// The lobby: a physical panel standing at the far end of the table.
    ///
    /// Three decisions shape it. It is diegetic - a board on the table rather than a screen
    /// bolted to the player's face - because head-locked menus in VR are uncomfortable and
    /// make the room feel fake. It never requires typing beyond a four-character room code,
    /// entered on a keypad of big physical keys, because typing an email on a floating
    /// keyboard with hand tracking is miserable and would be the first thing to make someone
    /// quit. And every screen is reachable in one pinch from the first one, so the path from
    /// putting the headset on to throwing a stone is as short as it can be.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public enum Screen
        {
            Home,
            Online,
            JoinCode,
            Lobby,
            Leaderboard,
            Connecting
        }

        [Header("References")]
        public RiftNetClient Net;
        public MatchManager Match;
        public Rink Playfield;

        [Header("Placement")]
        [Tooltip("Height above the rink surface the panel stands at.")]
        public float PanelHeight = 0.30f;

        [Tooltip("Where along the rink the panel stands, as a fraction of its length from " +
                 "the centre. Negative is towards the player.")]
        public float PanelAlongRink = -0.22f;

        public Screen Current { get; private set; } = Screen.Home;

        private Transform _root;
        private TextPanel _title;
        private TextPanel _subtitle;
        private TextPanel _status;
        private TextPanel _board;

        private readonly List<PinchButton> _buttons = new();
        private readonly StringBuilder _codeEntry = new();
        private bool _busy;

        private void Start()
        {
            if (Net == null) Net = RiftNetClient.Instance;
            if (Match == null) Match = MatchManager.Instance ?? FindFirstObjectByType<MatchManager>();
            if (Playfield == null) Playfield = Rink.Instance ?? FindFirstObjectByType<Rink>();

            BuildPanel();
            Show(Screen.Home);

            if (Net != null)
            {
                Net.StatusChanged += _ => RefreshStatus();
                Net.RoomUpdated += OnRoomUpdated;
                Net.MatchStarted += _ => Hide();
                Net.MatchOver += _ => StartCoroutine(ShowAfterMatch());
                Net.ServerError += error => SetStatus(error?.message ?? "server error");
            }

            if (Match != null) Match.StateChanged += OnMatchState;

            Theme.Changed += OnThemeChanged;
        }

        private void OnDestroy()
        {
            Theme.Changed -= OnThemeChanged;
        }

        /// <summary>
        /// Rebuilds the open screen in the new palette when the landscape rolls over.
        ///
        /// Only the screens that are just buttons and text, though. Rebuilding the lobby or
        /// the connecting screen would throw away a room the player is sitting in, or cancel
        /// the look of a connection in progress, to achieve a colour change - which is a
        /// trade nobody would take.
        /// </summary>
        private void OnThemeChanged(Theme.Palette palette)
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            if (Current != Screen.Home) return;
            Show(Screen.Home);
        }

        private void BuildPanel()
        {
            Transform anchor = Playfield != null ? Playfield.Surface : transform;

            _root = new GameObject("MenuPanel").transform;
            _root.SetParent(anchor, false);
            // Over the near third of the rink, not beyond the far end.
            //
            // The panel has to be inside arm's reach, because its buttons are pressed by
            // physically putting a hand into them. Standing it past the house looked tidy and
            // made the entire lobby impossible to operate: every button was about two metres
            // from the player, roughly twice as far as anyone can reach.
            _root.localPosition = new Vector3(
                0f, PanelHeight, GameConfig.RinkLength * PanelAlongRink);

            // Faces back down the rink towards the player, so the buttons are reachable
            // across the near half of the table rather than at arm's full stretch.
            _root.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var backing = ProceduralArt.Box(_root, "Backing", new Vector3(0f, -0.02f, 0.012f),
                new Vector3(0.46f, 0.44f, 0.008f),
                ProceduralArt.MakeTransparentMaterial(Theme.Panel),
                false);
            backing.name = "Backing";

            _title = TextPanel.Create(_root, "Title", new Vector3(0f, 0.145f, 0f), 0.034f);
            _title.Show("RIFT RALLY", Theme.Primary);

            _subtitle = TextPanel.Create(_root, "Subtitle", new Vector3(0f, 0.108f, 0f), 0.018f);
            _subtitle.Show("", Theme.TextDim);

            _status = TextPanel.Create(_root, "Status", new Vector3(0f, -0.185f, 0f), 0.015f);
            _status.Show("", Theme.TextDim);

            _board = TextPanel.Create(_root, "Board", new Vector3(0f, 0.02f, 0f), 0.0165f);
            _board.HideInstant();
        }

        // ---------------------------------------------------------------- screens

        public void Show(Screen screen)
        {
            Current = screen;
            ClearButtons();
            _board.HideInstant();
            if (_root != null) _root.gameObject.SetActive(true);

            switch (screen)
            {
                case Screen.Home: BuildHome(); break;
                case Screen.Online: BuildOnline(); break;
                case Screen.JoinCode: BuildJoinCode(); break;
                case Screen.Lobby: BuildLobby(); break;
                case Screen.Leaderboard: BuildLeaderboard(); break;
                case Screen.Connecting: BuildConnecting(); break;
            }

            RefreshStatus();
        }

        public void Hide()
        {
            ClearButtons();
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private void BuildHome()
        {
            _subtitle.Show("PINCH A BUTTON TO BEGIN", Theme.TextDim);

            Add("PLAY SOLO", 0.055f, GameConfig.PlayerOneColor, () =>
            {
                Hide();
                Match?.StartMatch(GameMode.SoloVsAi);
            });

            Add("PLAY ONLINE", 0f, Theme.Primary, () => Show(Screen.Online));

            Add("LEADERBOARD", -0.055f, new Color(0.62f, 0.52f, 0.92f),
                () => Show(Screen.Leaderboard));

            Add("TWO PLAYERS, ONE HEADSET", -0.11f, GameConfig.PlayerTwoColor, () =>
            {
                Hide();
                Match?.StartMatch(GameMode.HotSeatTwoPlayer);
            });

            BuildLandscapeRow(-0.165f);
        }

        /// <summary>
        /// A landscape picker on the first screen.
        ///
        /// The dock has the same control, but putting one here as well is worth the
        /// duplication: the lobby is where somebody is deciding what kind of game they are
        /// about to have, the landscape has just rolled over to something new, and standing
        /// in it while choosing is the natural moment to say no, not that one. Changing it
        /// here rebuilds the world immediately, so the choice is made by looking rather than
        /// by reading a name.
        /// </summary>
        private void BuildLandscapeRow(float y)
        {
            string name = LandscapeRotation.Current.Name.ToUpperInvariant();

            // Built directly rather than through Add, because this row is narrower than a
            // full-width button to leave room for the two arrow keys beside it. The width is
            // read when the plate is built, so it has to be passed in, not set afterwards.
            var label = PinchButton.Create(_root, $"WORLD: {name}", new Vector3(0f, y, 0f),
                                           0.22f, 0.042f, Theme.Secondary,
                                           () => StepLandscape(1));
            _buttons.Add(label);

            var back = PinchButton.Create(_root, "<", new Vector3(-0.16f, y, 0f),
                                          0.05f, 0.042f, Theme.TextDim,
                                          () => StepLandscape(-1));
            _buttons.Add(back);

            var forward = PinchButton.Create(_root, ">", new Vector3(0.16f, y, 0f),
                                             0.05f, 0.042f, Theme.TextDim,
                                             () => StepLandscape(1));
            _buttons.Add(forward);
        }

        private void StepLandscape(int delta)
        {
            LandscapeDirector director = LandscapeDirector.Instance;
            if (director != null) director.Step(delta);
            else LandscapeRotation.Step(delta);

            // The panel is rebuilt rather than relabelled, because the new biome has brought
            // a new interface palette with it and every button on this screen is tinted from
            // it. Rebuilding is how the whole lobby re-colours in one go.
            Show(Screen.Home);
        }

        private void BuildOnline()
        {
            string who = Net?.Me != null
                ? $"PLAYING AS {Net.Me.displayName.ToUpperInvariant()}"
                : "CONNECTING...";
            _subtitle.Show(who, Theme.TextDim);

            Add("QUICK MATCH", 0.055f, Theme.Primary,
                () => StartCoroutine(GoOnline(() => Net.QuickMatch())));

            Add("CREATE A ROOM", 0f, GameConfig.PlayerOneColor,
                () => StartCoroutine(GoOnline(() => Net.CreateRoom())));

            Add("JOIN WITH A CODE", -0.055f, GameConfig.PlayerTwoColor,
                () => Show(Screen.JoinCode));

            Add("BACK", -0.12f, Theme.TextDim, () => Show(Screen.Home));
        }

        /// <summary>
        /// A keypad, not a keyboard. Room codes use 25 unambiguous characters, so the keys can
        /// be big enough to hit reliably with tracked hands.
        /// </summary>
        private void BuildJoinCode()
        {
            _subtitle.Show("ENTER THE FOUR CHARACTER CODE", Theme.TextDim);
            _codeEntry.Clear();
            UpdateCodeDisplay();

            const string keys = "ACDEFGHJKMNPQRTUVWXY34679";
            const int columns = 7;
            float keySize = 0.048f;

            for (int i = 0; i < keys.Length; i++)
            {
                int row = i / columns;
                int column = i % columns;
                int inRow = Mathf.Min(columns, keys.Length - row * columns);

                float x = (column - (inRow - 1) * 0.5f) * keySize;
                float y = 0.055f - row * keySize;

                char key = keys[i];
                var button = PinchButton.Create(_root, key.ToString(),
                    new Vector3(x, y, 0f), keySize * 0.86f, keySize * 0.78f,
                    new Color(0.18f, 0.42f, 0.62f), () => TypeCode(key));
                _buttons.Add(button);
            }

            Add("JOIN", -0.145f, Theme.Primary, () =>
            {
                if (_codeEntry.Length != 4)
                {
                    SetStatus("A CODE IS FOUR CHARACTERS");
                    return;
                }
                string code = _codeEntry.ToString();
                StartCoroutine(GoOnline(() => Net.JoinRoom(code)));
            });

            Add("BACK", -0.19f, Theme.TextDim, () => Show(Screen.Online));
        }

        private void BuildLobby()
        {
            NetRoom room = Net?.Room;
            if (room == null)
            {
                Show(Screen.Online);
                return;
            }

            _subtitle.Show($"ROOM CODE   {room.code}", Theme.Primary);

            string opponent = "WAITING FOR AN OPPONENT";
            if (room.players != null)
            {
                for (int i = 0; i < room.players.Length; i++)
                {
                    var p = room.players[i];
                    if (p == null || i == Net.MySeat) continue;
                    opponent = $"{p.displayName.ToUpperInvariant()}   {p.rating}"
                               + (p.ready ? "   READY" : "");
                }
            }

            _board.Show($"{opponent}\n\nREAD THE CODE OUT TO A FRIEND\nAND THEY CAN JOIN YOU",
                        Theme.TextDim);

            bool canReady = room.players != null
                            && room.players.Length > 1
                            && room.players[0] != null
                            && room.players[1] != null;

            var ready = Add("I AM READY", -0.10f, Theme.Primary,
                            () => Net.SetReady(true));
            ready.Interactable = canReady;

            Add("LEAVE", -0.155f, Theme.TextDim, () =>
            {
                Net.LeaveRoom();
                Show(Screen.Online);
            });
        }

        private void BuildLeaderboard()
        {
            _subtitle.Show("TOP PLAYERS", Theme.TextDim);
            _board.Show("LOADING...", Theme.TextDim);

            Add("BACK", -0.155f, Theme.TextDim, () => Show(Screen.Home));
            StartCoroutine(LoadLeaderboard());
        }

        private void BuildConnecting()
        {
            _subtitle.Show("REACHING THE SERVER", Theme.TextDim);
            _board.Show("THE FREE SERVER SLEEPS WHEN IDLE,\nSO THE FIRST CONNECTION\nCAN TAKE A MOMENT",
                        Theme.TextDim);
            Add("CANCEL", -0.155f, Theme.TextDim, () =>
            {
                Net?.Disconnect();
                Show(Screen.Online);
            });
        }

        // ---------------------------------------------------------------- actions

        /// <summary>
        /// Signs in as a guest if needed, connects, then runs the requested action. Written as
        /// one coroutine because every online entry point needs the same three steps, and
        /// doing them in sequence is what keeps the player on one "connecting" screen rather
        /// than flicking through three.
        /// </summary>
        private IEnumerator GoOnline(System.Action action)
        {
            if (Net == null || _busy) yield break;
            _busy = true;
            Show(Screen.Connecting);

            if (string.IsNullOrEmpty(Net.Token) || Net.Me == null)
            {
                bool ok = false;
                yield return Net.SignInAsGuest(null, result => ok = result);
                if (!ok)
                {
                    SetStatus(Net.LastError ?? "COULD NOT REACH THE SERVER");
                    _busy = false;
                    Show(Screen.Online);
                    yield break;
                }
            }

            Net.Connect();

            float waited = 0f;
            while (Net.Status != NetStatus.Online && waited < 45f)
            {
                if (Net.Status == NetStatus.Failed) break;
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (Net.Status != NetStatus.Online)
            {
                SetStatus(Net.LastError ?? "COULD NOT CONNECT");
                _busy = false;
                Show(Screen.Online);
                yield break;
            }

            action?.Invoke();
            _busy = false;
        }

        private IEnumerator LoadLeaderboard()
        {
            if (Net == null) yield break;

            NetLeaderboard board = null;
            yield return Net.FetchLeaderboard(result => board = result, 10);

            if (board?.entries == null || board.entries.Length == 0)
            {
                _board.Show("NO MATCHES PLAYED YET.\nBE THE FIRST.", Theme.TextDim);
                yield break;
            }

            var text = new StringBuilder();
            foreach (NetLeaderboardEntry entry in board.entries)
            {
                // Guests are badged rather than hidden: they are ranked on the same board,
                // and seeing a guest at the top is the point.
                string badge = entry.IsGuest ? "*" : " ";
                string name = (entry.displayName ?? "?").ToUpperInvariant();
                if (name.Length > 12) name = name.Substring(0, 12);
                text.AppendLine($"{entry.rank,2}{badge} {name,-12} {entry.rating,5}  {entry.wins}W");
            }

            if (board.me != null)
            {
                text.AppendLine();
                text.AppendLine($"YOU: RANK {board.me.rank}   {board.me.rating}   {board.me.wins}W");
            }

            text.AppendLine();
            text.Append("* GUEST");

            _board.Show(text.ToString(), Theme.TextDim);
        }

        private IEnumerator ShowAfterMatch()
        {
            yield return new WaitForSecondsRealtime(5f);
            Show(Screen.Home);
        }

        private void TypeCode(char key)
        {
            if (_codeEntry.Length >= 4) _codeEntry.Clear();
            _codeEntry.Append(key);
            UpdateCodeDisplay();
        }

        private void UpdateCodeDisplay()
        {
            string shown = _codeEntry.ToString().PadRight(4, '_');
            _board.Show(string.Join(" ", shown.ToCharArray()), Theme.Primary);
        }

        // ---------------------------------------------------------------- reactions

        private void OnRoomUpdated(NetRoom room, int seat)
        {
            if (room == null) return;
            if (room.IsPlaying) { Hide(); return; }
            if (Current == Screen.Connecting || Current == Screen.Online
                || Current == Screen.JoinCode || Current == Screen.Lobby)
            {
                Show(Screen.Lobby);
            }
        }

        private void OnMatchState(MatchState state)
        {
            // Back to the menu when a local match finishes, but not between rounds.
            if (state == MatchState.MatchOver && Net?.Room == null)
            {
                StartCoroutine(ShowAfterMatch());
            }
        }

        private void RefreshStatus()
        {
            if (Net == null) return;

            string text = Net.Status switch
            {
                NetStatus.Online => Net.Me != null ? $"ONLINE AS {Net.Me.displayName.ToUpperInvariant()}" : "ONLINE",
                NetStatus.Connecting => "CONNECTING...",
                NetStatus.Authenticating => "SIGNING IN...",
                NetStatus.Reconnecting => "RECONNECTING...",
                NetStatus.Failed => Net.LastError ?? "OFFLINE",
                _ => ""
            };

            SetStatus(text);
        }

        private void SetStatus(string text)
        {
            if (_status == null) return;
            _status.Show((text ?? string.Empty).ToUpperInvariant(), Theme.TextDim);
        }

        private PinchButton Add(string label, float y, Color tint, System.Action onPressed)
        {
            var button = PinchButton.Create(_root, label, new Vector3(0f, y, 0f),
                                            0.30f, 0.042f, tint, onPressed);
            _buttons.Add(button);
            return button;
        }

        private void ClearButtons()
        {
            foreach (PinchButton button in _buttons)
                if (button != null) Destroy(button.gameObject);
            _buttons.Clear();
        }

        private void Update()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;

            // The panel leans to face wherever the player actually is, so a seated player and
            // a standing one both get a square-on view without a settings menu.
            Transform viewer = Camera.main != null ? Camera.main.transform : null;
            if (viewer == null) return;

            Vector3 toViewer = viewer.position - _root.position;
            toViewer.y = 0f;
            if (toViewer.sqrMagnitude < 0.0004f) return;

            Quaternion want = Quaternion.LookRotation(-toViewer.normalized, Vector3.up);
            _root.rotation = Easing.Damp(_root.rotation, want, 2.5f, Time.unscaledDeltaTime);
        }
    }
}
