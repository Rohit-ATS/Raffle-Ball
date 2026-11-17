using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;
using RiftRally.Modifiers;

namespace RiftRally.Presentation
{
    /// <summary>
    /// All of the match's readable state, presented as signage sitting on and above the rink.
    ///
    /// The HUD is purely a listener: it subscribes to <see cref="MatchManager"/> events and
    /// never asks the match to do anything. Keeping that dependency one-way means the match
    /// loop can be driven headlessly by a test, or by the attract-mode demo, with no UI
    /// attached at all.
    /// </summary>
    public class MatchHud : MonoBehaviour
    {
        [Header("References")]
        public MatchManager Match;
        public Rink Playfield;

        [Tooltip("Labels turn to face this. Defaults to the main camera.")]
        public Transform Viewer;

        private TextPanel _score;
        private TextPanel _banner;
        private TextPanel _sub;
        private TextPanel _hint;

        private float _bannerHoldUntil;
        private bool _subscribed;

        private void Start()
        {
            if (Playfield == null)
                Playfield = Rink.Instance != null ? Rink.Instance : FindFirstObjectByType<Rink>();
            if (Match == null)
                Match = MatchManager.Instance != null
                    ? MatchManager.Instance
                    : FindFirstObjectByType<MatchManager>();

            BuildLabels();
            Subscribe();
            RefreshScore();
            ShowHint("PINCH A STONE - PULL BACK - RELEASE");
        }

        private void OnDestroy() => Unsubscribe();

        // ---------------------------------------------------------------- construction

        private void BuildLabels()
        {
            float halfL = GameConfig.RinkLength * 0.5f;
            Transform anchor = Playfield != null ? Playfield.Surface : transform;

            var root = new GameObject("Hud").transform;
            root.SetParent(anchor, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;

            // Scoreboard hangs just beyond the far end, angled back at the player.
            _score = TextPanel.Create(root, "Scoreboard",
                new Vector3(0f, 0.30f, halfL + 0.06f), 0.045f);
            _score.SetColor(GameConfig.NeutralColor);

            // Banner floats over the house, where the player is already looking.
            _banner = TextPanel.Create(root, "Banner",
                new Vector3(0f, 0.20f, halfL - GameConfig.TargetFromFarEnd), 0.055f);
            _banner.HideInstant();

            _sub = TextPanel.Create(root, "BannerSub",
                new Vector3(0f, 0.145f, halfL - GameConfig.TargetFromFarEnd), 0.028f);
            _sub.HideInstant();

            // Hint sits low near the launch line, out of the throwing line of sight.
            _hint = TextPanel.Create(root, "Hint",
                new Vector3(0f, 0.09f, -halfL + 0.02f), 0.022f);
            _hint.SetColor(GameConfig.NeutralColor);
        }

        private void Subscribe()
        {
            if (_subscribed || Match == null) return;
            Match.StateChanged += OnStateChanged;
            Match.RoundStarted += OnRoundStarted;
            Match.TurnStarted += OnTurnStarted;
            Match.RoundScored += OnRoundScored;
            Match.MatchEnded += OnMatchEnded;
            Match.StealHappened += OnSteal;
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
            Match.StealHappened -= OnSteal;
            _subscribed = false;
        }

        // ---------------------------------------------------------------- event handlers

        private void OnStateChanged(MatchState state)
        {
            switch (state)
            {
                case MatchState.Attract:
                    ShowBanner("RIFT RALLY", "PINCH TO BEGIN", GameConfig.TargetColor, 999f);
                    ShowHint("HOLD YOUR HANDS OVER THE TABLE");
                    break;

                case MatchState.MatchOver:
                    ShowHint("PINCH TO PLAY AGAIN");
                    break;
            }
            RefreshScore();
        }

        private void OnRoundStarted(int round, RinkModifier modifier)
        {
            string title = modifier != null
                ? $"ROUND {round} - {modifier.DisplayName.ToUpperInvariant()}"
                : $"ROUND {round}";

            string sub = modifier != null && !string.IsNullOrEmpty(modifier.Description)
                ? modifier.Description.ToUpperInvariant()
                : "CLEAN RINK - GET THE FEEL OF THE ICE";

            Color c = modifier != null ? GameConfig.TargetColor : GameConfig.NeutralColor;
            ShowBanner(title, sub, c, GameConfig.ModifierRevealTime);
            RefreshScore();
        }

        private void OnTurnStarted(PlayerSide player, int stoneNumber)
        {
            bool ai = Match != null && Match.IsAiControlled(player);
            string who = ai ? "OPPONENT THROWS" : $"{player.Label()} TO THROW";

            ShowBanner(who, $"STONE {stoneNumber} OF {GameConfig.StonesPerPlayerPerRound}",
                       player.ToColor(), GameConfig.TurnHandoffTime + 0.6f);

            ShowHint(ai ? "WATCH THE LINE" : "PINCH - PULL BACK - RELEASE");
            RefreshScore();
        }

        private void OnRoundScored(RoundResult result)
        {
            if (result.IsBlank)
            {
                ShowBanner("BLANK ROUND", "NO STONE IN THE HOUSE",
                           GameConfig.NeutralColor, GameConfig.RoundScoreRevealTime);
            }
            else
            {
                string points = result.Points == 1 ? "1 POINT" : $"{result.Points} POINTS";
                ShowBanner($"{result.Winner.Label()} SCORES", points,
                           result.Winner.ToColor(), GameConfig.RoundScoreRevealTime);
            }
            RefreshScore();
        }

        private void OnMatchEnded(PlayerSide winner)
        {
            string tally = Match != null
                ? $"{Match.RoundsWon[0]} - {Match.RoundsWon[1]}"
                : string.Empty;
            ShowBanner($"{winner.Label()} WINS THE MATCH", tally, winner.ToColor(), 999f);
            RefreshScore();
        }

        private void OnSteal(PlayerSide side)
        {
            ShowBanner("SHOT STOLEN", $"{side.Label()} IS LYING CLOSEST", side.ToColor(), 1.4f);
        }

        // ---------------------------------------------------------------- drawing

        private void RefreshScore()
        {
            if (_score == null || Match == null) return;

            int round = Mathf.Clamp(Match.Round, 1, GameConfig.MaxRounds);
            _score.SetText(
                $"BLUE {Match.RoundsWon[0]}   -   {Match.RoundsWon[1]} ORANGE\n" +
                $"ROUND {round} / {GameConfig.MaxRounds}");
        }

        private void ShowBanner(string title, string sub, Color color, float seconds)
        {
            if (_banner == null) return;
            _banner.Show(title, color);
            if (_sub != null) _sub.Show(sub, new Color(color.r, color.g, color.b, 0.75f));
            _bannerHoldUntil = Time.time + seconds;
        }

        private void ShowHint(string text)
        {
            if (_hint != null) _hint.Show(text, GameConfig.NeutralColor);
        }

        private void Update()
        {
            Subscribe();

            if (_banner != null && Time.time > _bannerHoldUntil)
            {
                _banner.Hide();
                if (_sub != null) _sub.Hide();
            }

            Transform viewer = Viewer != null
                ? Viewer
                : (Camera.main != null ? Camera.main.transform : null);
            if (viewer == null) return;

            FaceViewer(_score, viewer);
            FaceViewer(_banner, viewer);
            FaceViewer(_sub, viewer);
            FaceViewer(_hint, viewer);
        }

        private static void FaceViewer(TextPanel panel, Transform viewer)
        {
            if (panel != null) panel.FaceTowards(viewer.position);
        }
    }
}
