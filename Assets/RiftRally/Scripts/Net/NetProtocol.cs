using System;
using UnityEngine;

namespace RiftRally.Net
{
    /// <summary>
    /// Wire types shared with the Node server.
    ///
    /// These are plain serialisable classes read by <see cref="JsonUtility"/> rather than a
    /// third-party JSON library, which keeps the client free of any package that would have
    /// to survive an IL2CPP strip. The cost is that every field must be a concrete type -
    /// no dictionaries, no polymorphism - so the envelope is parsed first to read the type
    /// tag, and the payload is then parsed into the matching class.
    ///
    /// Field names match the server exactly and are intentionally camelCase, because that is
    /// what JsonUtility matches against and renaming either side silently yields default
    /// values rather than an error.
    /// </summary>
    [Serializable]
    public class NetEnvelope
    {
        public string type;
    }

    [Serializable]
    public class NetPlayer
    {
        public string id;
        public string kind;            // "guest" or "account"
        public string displayName;
        public int rating;
        public int wins;
        public int losses;
        public int matches;
        public int pointsFor;
        public int pointsAgainst;
        public int bestRound;

        public bool IsGuest => kind == "guest";
    }

    [Serializable]
    public class NetRoomPlayer
    {
        public string id;
        public string displayName;
        public string kind;
        public int rating;
        public bool ready;
        public bool connected;
    }

    [Serializable]
    public class NetRoom
    {
        public string code;
        public string state;           // "waiting", "playing", "finished"
        public int seed;
        public int round;
        public int[] roundsWon;
        public int[] totalPoints;
        public int hammer;
        public int turnSeat;
        public int stonesThrown;
        public int stoneNumber;
        public int modifier = -1;      // -1 means a clean rink
        public int winnerSeat = -1;
        public string endedReason;
        public bool isPublic;
        public NetRoomPlayer[] players;

        public bool IsPlaying => state == "playing";
        public bool IsWaiting => state == "waiting";
        public bool IsFinished => state == "finished";
    }

    [Serializable]
    public class NetLaunch
    {
        public float x;
        public float z;
        public float dirX;
        public float dirZ;
        public float speed;
        public float spin;
    }

    [Serializable]
    public class NetStone
    {
        public string id;
        public int seat;
        public float x;
        public float z;
    }

    [Serializable]
    public class NetRoundResult
    {
        public bool blank;
        public int winnerSeat = -1;
        public int points;
        public float closestDistance;
        public int inHouse;
    }

    // ---------------------------------------------------------------- server messages

    [Serializable]
    public class NetAuthenticated
    {
        public NetPlayer player;
    }

    [Serializable]
    public class NetRoomMessage
    {
        public NetRoom room;
        public int seat = -1;
        public NetStone[] stones;
    }

    [Serializable]
    public class NetTurnMessage
    {
        public int seat;
        public int stoneNumber;
        public NetRoom room;
    }

    [Serializable]
    public class NetOpponentThrow
    {
        public int seat;
        public NetLaunch launch;
        public NetStone[] stones;
    }

    [Serializable]
    public class NetRoundScored
    {
        public NetRoundResult result;
        public NetRoom room;
    }

    [Serializable]
    public class NetMatchOver
    {
        public NetRoom room;
        public bool youWon;
        public int winnerSeat;
        public string reason;
        public NetPlayer player;
        public int rank;
    }

    [Serializable]
    public class NetError
    {
        public string code;
        public string message;
    }

    [Serializable]
    public class NetSeatMessage
    {
        public int seat;
    }

    // ---------------------------------------------------------------- client messages

    [Serializable]
    public class NetAuthRequest
    {
        public string type = "auth";
        public string token;
    }

    [Serializable]
    public class NetSimpleRequest
    {
        public string type;
    }

    [Serializable]
    public class NetCreateRoomRequest
    {
        public string type = "createRoom";
        public bool isPublic;
    }

    [Serializable]
    public class NetJoinRoomRequest
    {
        public string type = "joinRoom";
        public string code;
    }

    [Serializable]
    public class NetReadyRequest
    {
        public string type = "ready";
        public bool ready = true;
    }

    [Serializable]
    public class NetThrowRequest
    {
        public string type = "throw";
        public NetLaunch launch;
        public NetStone[] stones;
    }

    // ---------------------------------------------------------------- HTTP payloads

    [Serializable]
    public class NetAuthResponse
    {
        public string token;
        public NetPlayer player;
    }

    [Serializable]
    public class NetGuestRequest
    {
        public string displayName;
    }

    [Serializable]
    public class NetCredentials
    {
        public string email;
        public string password;
        public string displayName;
    }

    [Serializable]
    public class NetLeaderboardEntry
    {
        public int rank;
        public string id;
        public string kind;
        public string displayName;
        public int rating;
        public int wins;
        public int losses;
        public int matches;

        public bool IsGuest => kind == "guest";
    }

    [Serializable]
    public class NetLeaderboard
    {
        public NetLeaderboardEntry[] entries;
        public NetLeaderboardEntry me;
    }

    [Serializable]
    public class NetApiError
    {
        public string error;
    }
}
