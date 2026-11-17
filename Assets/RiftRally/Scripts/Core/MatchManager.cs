using System.Collections;
using UnityEngine;
using RiftRally.Gameplay;
using RiftRally.Modifiers;

namespace RiftRally.Core
{
    public enum GameMode
    {
        SoloVsAi,
        HotSeatTwoPlayer,

        /// <summary>
        /// Both sides played by the AI. Drives the attract-mode demo match, and lets the
        /// whole match loop be exercised by an automated test with no hands attached.
        /// </summary>
        AiVsAi
    }

    public enum MatchState
    {
        Attract,        // title / first screen
        Tutorial,       // teaching the pinch-pull-release
        RoundIntro,     // modifier reveal
        PlayerTurn,     // waiting for a throw
        StoneSettling,  // stones still moving
        RoundScoring,   // showing the round result
        MatchOver
    }

    /// <summary>
    /// Runs the game: rounds, turn order, scoring and win conditions.
    ///
    /// Deliberately a flat coroutine-driven state machine. A match is short and strictly
    /// sequential, so an explicit sequence is easier to reason about (and to recover from a
    /// stuck stone) than an event web.
    /// </summary>
    public class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [Header("References")]
        public Rink Playfield;
        public StoneLauncher Launcher;
        public ModifierDirector Modifiers;
        public AiOpponent Ai;

        [Header("Setup")]
        public GameMode Mode = GameMode.SoloVsAi;

        [Tooltip("Start straight into a match instead of the attract screen. Handy while developing.")]
        public bool AutoStart = false;

        public MatchState State { get; private set; } = MatchState.Attract;
        public int Round { get; private set; }
        public int[] RoundsWon { get; } = new int[2];
        public int[] TotalPoints { get; } = new int[2];
        public PlayerSide CurrentPlayer { get; private set; } = PlayerSide.One;
        public int StonesThrownThisRound { get; private set; }

        /// <summary>Which player throws the last stone of the round (the advantage).</summary>
        public PlayerSide Hammer { get; private set; } = PlayerSide.Two;

        // ---- events for UI, audio and FX ----
        public event System.Action<MatchState> StateChanged;
        public event System.Action<int, RinkModifier> RoundStarted;
        public event System.Action<PlayerSide, int> TurnStarted;
        public event System.Action<Stone, float> StoneReleased;
        public event System.Action<RoundResult> RoundScored;
        public event System.Action<PlayerSide> MatchEnded;
        public event System.Action<PlayerSide> StealHappened;

        private bool _hadLeader;
        private PlayerSide _leader;
        private Coroutine _loop;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (Playfield == null)
                Playfield = Rink.Instance != null ? Rink.Instance : FindFirstObjectByType<Rink>();
            if (Launcher == null) Launcher = FindFirstObjectByType<StoneLauncher>();
            if (Modifiers == null) Modifiers = FindFirstObjectByType<ModifierDirector>();
            if (Ai == null) Ai = FindFirstObjectByType<AiOpponent>();

            if (Launcher != null) Launcher.StoneThrown += OnStoneThrown;

            SetState(MatchState.Attract);
            if (AutoStart) StartMatch(Mode);
        }

        private void OnDisable()
        {
            if (Launcher != null) Launcher.StoneThrown -= OnStoneThrown;
        }

        // ---------------------------------------------------------------- public control

        public void StartMatch(GameMode mode)
        {
            Mode = mode;
            Round = 0;
            RoundsWon[0] = RoundsWon[1] = 0;
            TotalPoints[0] = TotalPoints[1] = 0;
            Hammer = PlayerSide.Two;

            if (_loop != null) StopCoroutine(_loop);
            _loop = StartCoroutine(MatchLoop());
        }

        public void Rematch() => StartMatch(Mode);

        /// <summary>Abandons the match and returns to the attract screen.</summary>
        public void QuitToAttract()
        {
            if (_loop != null) StopCoroutine(_loop);
            if (Launcher != null) Launcher.EndTurn();
            if (Playfield != null) Playfield.ClearStones();
            SetState(MatchState.Attract);
        }

        // ---------------------------------------------------------------- the loop

        private IEnumerator MatchLoop()
        {
            while (RoundsWon[0] < GameConfig.RoundsToWinMatch &&
                   RoundsWon[1] < GameConfig.RoundsToWinMatch &&
                   Round < GameConfig.MaxRounds)
            {
                Round++;
                yield return StartCoroutine(PlayRound());
            }

            PlayerSide winner = RoundsWon[0] >= RoundsWon[1] ? PlayerSide.One : PlayerSide.Two;
            if (TotalPoints[0] != TotalPoints[1] && RoundsWon[0] == RoundsWon[1])
                winner = TotalPoints[0] > TotalPoints[1] ? PlayerSide.One : PlayerSide.Two;

            SetState(MatchState.MatchOver);
            MatchEnded?.Invoke(winner);
        }

        private IEnumerator PlayRound()
        {
            // --- set up the rink ---
            if (Playfield != null) Playfield.ClearStones();
            StonesThrownThisRound = 0;
            _hadLeader = false;

            RinkModifier modifier = Modifiers != null ? Modifiers.SelectForRound(Round) : null;

            SetState(MatchState.RoundIntro);
            RoundStarted?.Invoke(Round, modifier);
            yield return new WaitForSeconds(GameConfig.ModifierRevealTime);

            // The player without the hammer throws first.
            CurrentPlayer = Hammer.Other();

            int totalStones = GameConfig.StonesPerPlayerPerRound * 2;

            for (int i = 0; i < totalStones; i++)
            {
                yield return StartCoroutine(PlayTurn(CurrentPlayer));
                CurrentPlayer = CurrentPlayer.Other();
            }

            // --- score it ---
            SetState(MatchState.RoundScoring);
            RoundResult result = ScoreKeeper.ScoreRound(Playfield);

            if (!result.IsBlank)
            {
                TotalPoints[(int)result.Winner] += result.Points;
                RoundsWon[(int)result.Winner] += 1;
                // Losing the round earns the hammer for the next one.
                Hammer = result.Winner.Other();
            }

            RoundScored?.Invoke(result);
            yield return new WaitForSeconds(GameConfig.RoundScoreRevealTime);
        }

        private IEnumerator PlayTurn(PlayerSide player)
        {
            SetState(MatchState.PlayerTurn);
            int stoneNumber = StonesThrownThisRound / 2 + 1;
            TurnStarted?.Invoke(player, stoneNumber);

            bool aiTurn = IsAiControlled(player);

            if (aiTurn)
            {
                yield return new WaitForSeconds(GameConfig.TurnHandoffTime);
                yield return StartCoroutine(Ai.TakeTurn(player));
            }
            else
            {
                if (Launcher != null) Launcher.BeginTurn(player);

                // Wait for the throw. The launcher reports it through OnStoneThrown.
                while (Launcher != null && Launcher.State != LauncherState.Thrown)
                    yield return null;
            }

            // --- let the physics finish ---
            SetState(MatchState.StoneSettling);
            if (Launcher != null) Launcher.EndTurn();

            float guard = 0f;
            yield return new WaitForSeconds(0.25f);
            while (Playfield != null && !Playfield.AllStonesSettled() && guard < 20f)
            {
                guard += Time.deltaTime;
                yield return null;
            }

            StonesThrownThisRound++;

            // Track who is winning, so a lead change can be celebrated as a steal.
            var closest = Playfield != null ? Playfield.ClosestScoringStone() : null;
            if (closest != null)
            {
                _leader = closest.Owner;
                _hadLeader = true;
            }

            yield return new WaitForSeconds(GameConfig.TurnHandoffTime * 0.5f);
        }

        /// <summary>True when the AI, rather than a person, throws for this side.</summary>
        public bool IsAiControlled(PlayerSide player)
        {
            if (Ai == null) return false;
            if (Mode == GameMode.AiVsAi) return true;
            return Mode == GameMode.SoloVsAi && player == PlayerSide.Two;
        }

        private void OnStoneThrown(Stone stone, float speed)
        {
            StoneReleased?.Invoke(stone, speed);

            PlayerSide before = _leader;
            bool had = _hadLeader;

            // Evaluated a moment later, once the stone has had time to act.
            StartCoroutine(CheckForSteal(stone, before, had));
        }

        private IEnumerator CheckForSteal(Stone stone, PlayerSide previousLeader, bool hadLeader)
        {
            yield return new WaitForSeconds(0.3f);
            while (Playfield != null && !Playfield.AllStonesSettled()) yield return null;

            if (ScoreKeeper.IsSteal(Playfield, stone, previousLeader, hadLeader))
                StealHappened?.Invoke(stone.Owner);
        }

        private void SetState(MatchState next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
