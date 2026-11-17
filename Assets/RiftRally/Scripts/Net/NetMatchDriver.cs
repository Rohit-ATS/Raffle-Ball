using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;
using RiftRally.Modifiers;
using RiftRally.Presentation;

namespace RiftRally.Net
{
    /// <summary>
    /// Plays a networked match by translating server messages into local gameplay.
    ///
    /// The division of labour is the whole design:
    ///
    /// - **Your own throw is never networked while you make it.** You pinch, pull and release
    ///   against local physics with zero latency, exactly as in solo play. Only once every
    ///   stone has stopped is the result reported. A throw that waited on a server round trip
    ///   would feel broken no matter how fast the server was.
    /// - **Their throw is replayed, then corrected.** The opponent's launch parameters are
    ///   replayed through the same launcher, so you watch a real stone slide down the rink
    ///   rather than seeing one teleport into place. When it settles, the server's
    ///   authoritative positions are applied. Those two almost always agree to within a few
    ///   millimetres, so the correction is invisible; when they do not, truth wins.
    /// - **The server owns the score.** Rounds, turn order and the result all arrive from the
    ///   server and are pushed into <see cref="MatchManager"/>, so the HUD, audio and house
    ///   glow behave identically online and offline.
    /// </summary>
    public class NetMatchDriver : MonoBehaviour
    {
        [Header("References")]
        public RiftNetClient Net;
        public MatchManager Match;
        public StoneLauncher Launcher;
        public Rink Playfield;
        public ModifierDirector Modifiers;

        [Tooltip("Seconds to wait for stones to stop before reporting a throw.")]
        public float SettleTimeout = 14f;

        /// <summary>Which side the local player is drawn as. Seat 0 is blue, seat 1 orange.</summary>
        public PlayerSide MySide => Net != null && Net.MySeat == 1 ? PlayerSide.Two : PlayerSide.One;

        private bool _subscribed;
        private bool _awaitingLocalThrow;
        private Coroutine _reporter;
        private int _lastRoundAnnounced;

        private void Start()
        {
            if (Net == null) Net = RiftNetClient.Instance;
            if (Match == null) Match = MatchManager.Instance ?? FindFirstObjectByType<MatchManager>();
            if (Launcher == null) Launcher = FindFirstObjectByType<StoneLauncher>();
            if (Playfield == null) Playfield = Rink.Instance ?? FindFirstObjectByType<Rink>();
            if (Modifiers == null) Modifiers = FindFirstObjectByType<ModifierDirector>();

            Subscribe();
        }

        private void OnDestroy() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed || Net == null) return;

            Net.MatchStarted += OnMatchStarted;
            Net.TurnStarted += OnTurnStarted;
            Net.OpponentThrew += OnOpponentThrew;
            Net.RoundScored += OnRoundScored;
            Net.MatchOver += OnMatchOver;

            if (Launcher != null) Launcher.StoneThrown += OnLocalStoneThrown;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            if (Net != null)
            {
                Net.MatchStarted -= OnMatchStarted;
                Net.TurnStarted -= OnTurnStarted;
                Net.OpponentThrew -= OnOpponentThrew;
                Net.RoundScored -= OnRoundScored;
                Net.MatchOver -= OnMatchOver;
            }
            if (Launcher != null) Launcher.StoneThrown -= OnLocalStoneThrown;
            _subscribed = false;
        }

        private void Update() => Subscribe();

        // ---------------------------------------------------------------- server events

        private void OnMatchStarted(NetRoom room)
        {
            if (Match == null || room == null) return;

            Match.ExternalMatchStarted();
            if (Playfield != null) Playfield.ClearStones();
            _lastRoundAnnounced = 0;

            AnnounceRoundIfNew(room);
        }

        /// <summary>
        /// Announces a round exactly once. The server mentions the current round in several
        /// messages, and re-announcing it would replay the modifier reveal mid-round.
        /// </summary>
        private void AnnounceRoundIfNew(NetRoom room)
        {
            if (room == null || room.round <= _lastRoundAnnounced) return;
            _lastRoundAnnounced = room.round;

            // Round one is clean by definition on both sides, so it is forced clean here
            // rather than trusted from the wire. That is not belt-and-braces: JsonUtility
            // decodes a JSON null into 0 for an int field, and 0 is a real modifier index,
            // so a server that sent null would silently switch the cross current on during
            // the round meant to teach the plain throw.
            int index = room.round <= 1 ? -1 : room.modifier;
            RinkModifier modifier = ApplyModifier(index);
            if (Playfield != null) Playfield.ClearStones();
            Match.ExternalRoundStarted(room.round, modifier);
        }

        /// <summary>
        /// Activates the modifier the server picked. Both clients derive the same index from
        /// the room seed, so nothing about the modifier's own behaviour is networked.
        /// </summary>
        private RinkModifier ApplyModifier(int index)
        {
            if (Modifiers == null) return null;

            Modifiers.DeactivateAll();
            if (index < 0 || Modifiers.Modifiers == null || index >= Modifiers.Modifiers.Count)
                return null;

            RinkModifier modifier = Modifiers.Modifiers[index];
            if (modifier == null) return null;

            modifier.Activate();
            return modifier;
        }

        private void OnTurnStarted(NetTurnMessage message)
        {
            if (message == null || Match == null) return;

            AnnounceRoundIfNew(message.room);

            PlayerSide side = SeatToSide(message.seat);
            Match.ExternalTurnStarted(side, Mathf.Max(1, message.stoneNumber));

            bool mine = Net != null && message.seat == Net.MySeat;

            if (mine)
            {
                _awaitingLocalThrow = true;
                if (Launcher != null) Launcher.BeginTurn(side);
            }
            else
            {
                _awaitingLocalThrow = false;
                if (Launcher != null) Launcher.EndTurn();
            }
        }

        /// <summary>Replays the opponent's throw locally, then adopts the server's positions.</summary>
        private void OnOpponentThrew(NetOpponentThrow message)
        {
            if (message?.launch == null || Launcher == null || Playfield == null) return;

            PlayerSide side = SeatToSide(message.seat);

            Vector3 direction = Playfield.Surface.TransformDirection(
                new Vector3(message.launch.dirX, 0f, message.launch.dirZ));
            if (direction.sqrMagnitude < 0.0001f) direction = Playfield.DownRink;

            Launcher.ThrowForAi(side, direction, message.launch.speed);
            Match?.ExternalStonesSettling();

            StartCoroutine(AdoptAfterSettle(message.stones));
        }

        private IEnumerator AdoptAfterSettle(NetStone[] stones)
        {
            yield return new WaitForSeconds(0.25f);

            float waited = 0f;
            while (Playfield != null && !Playfield.AllStonesSettled() && waited < SettleTimeout)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            SyncStones(stones);
        }

        /// <summary>
        /// Replaces the rink with the server's version.
        ///
        /// Rebuilding wholesale rather than nudging individual stones is deliberate: it cannot
        /// drift, it needs no stone identity to survive the network, and because it only ever
        /// happens between turns - when nothing is moving and nobody is aiming - it is not
        /// visible. Trying to reconcile stone by stone would be more code and more ways to
        /// end up with two players looking at different rinks.
        /// </summary>
        public void SyncStones(NetStone[] stones)
        {
            if (Playfield == null || Launcher == null || stones == null) return;

            Playfield.ClearStones();

            foreach (NetStone stone in stones)
            {
                if (stone == null) continue;

                Vector3 world = Playfield.RinkToWorld(stone.x, stone.z,
                                                      GameConfig.StoneHeight * 0.5f);
                Stone spawned = Launcher.SpawnSettledStone(SeatToSide(stone.seat), world);
                if (spawned == null) continue;
            }
        }

        // ---------------------------------------------------------------- local events

        private void OnLocalStoneThrown(Stone stone, float speed)
        {
            if (Net == null || Match == null) return;
            if (Match.Mode != GameMode.Online) return;
            if (!_awaitingLocalThrow) return;

            _awaitingLocalThrow = false;
            Match.ExternalStonesSettling();

            if (_reporter != null) StopCoroutine(_reporter);
            _reporter = StartCoroutine(ReportThrowWhenSettled(stone, speed));
        }

        private IEnumerator ReportThrowWhenSettled(Stone stone, float speed)
        {
            Vector3 launchLocal = Playfield != null && stone != null
                ? Playfield.WorldToRink(stone.transform.position)
                : Vector3.zero;

            Vector3 directionLocal = Playfield != null && stone != null
                ? Playfield.Surface.InverseTransformDirection(stone.Velocity.normalized)
                : Vector3.forward;

            // Captured before the wait: by the time the stone stops, its velocity is zero and
            // its position is the resting place, not the launch point.
            var launch = new NetLaunch
            {
                x = launchLocal.x,
                z = launchLocal.z,
                dirX = directionLocal.x,
                dirZ = directionLocal.z,
                speed = speed,
                spin = 0f
            };

            yield return new WaitForSeconds(0.25f);

            float waited = 0f;
            while (Playfield != null && !Playfield.AllStonesSettled() && waited < SettleTimeout)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            Net.SendThrow(launch, CaptureStones());
            _reporter = null;
        }

        /// <summary>Reads the resting rink into wire form, in rink-local metres.</summary>
        public List<NetStone> CaptureStones()
        {
            var list = new List<NetStone>();
            if (Playfield == null) return list;

            IReadOnlyList<Stone> stones = Playfield.Stones;
            for (int i = 0; i < stones.Count; i++)
            {
                Stone stone = stones[i];
                if (stone == null || !stone.CountsForScoring) continue;

                Vector3 local = Playfield.WorldToRink(stone.transform.position);
                list.Add(new NetStone
                {
                    id = $"s{i}",
                    seat = SideToSeat(stone.Owner),
                    x = local.x,
                    z = local.z
                });
            }
            return list;
        }

        private void OnRoundScored(NetRoundScored message)
        {
            if (message?.result == null || Match == null) return;

            var result = new RoundResult
            {
                IsBlank = message.result.blank,
                Winner = SeatToSide(message.result.winnerSeat),
                Points = message.result.points,
                ClosestDistance = message.result.closestDistance
            };

            int[] roundsWon = message.room?.roundsWon;
            int[] points = message.room?.totalPoints;

            // The server reports per-seat arrays; the local manager thinks in sides, and seat
            // 0 is always side One, so the arrays line up without reordering.
            Match.ExternalRoundScored(result, roundsWon, points);

            if (message.room != null && message.room.IsPlaying) AnnounceRoundIfNew(message.room);
        }

        private void OnMatchOver(NetMatchOver message)
        {
            if (message == null || Match == null) return;

            if (Launcher != null) Launcher.EndTurn();
            Match.ExternalMatchEnded(SeatToSide(message.winnerSeat));
        }

        // ---------------------------------------------------------------- mapping

        public static PlayerSide SeatToSide(int seat) => seat == 1 ? PlayerSide.Two : PlayerSide.One;

        public static int SideToSeat(PlayerSide side) => side == PlayerSide.Two ? 1 : 0;
    }
}
