using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace RiftRally.Net
{
    public enum NetStatus
    {
        Offline,
        Connecting,
        Authenticating,
        Online,
        Reconnecting,
        Failed
    }

    /// <summary>
    /// The connection to the multiplayer server: one WebSocket, plus the few HTTP calls for
    /// accounts and the leaderboard.
    ///
    /// Built on <see cref="ClientWebSocket"/> rather than a networking package so there is
    /// nothing extra to keep working through an IL2CPP build. Two details matter:
    ///
    /// - Socket callbacks arrive on a worker thread, and touching a Unity object from there
    ///   is undefined behaviour. Every inbound message is queued and drained in Update, so
    ///   all game-facing events fire on the main thread.
    /// - Reconnection is automatic and backs off. A Quest loses Wi-Fi often enough that a
    ///   client which gives up on the first drop would feel broken, and the server holds the
    ///   player's seat for exactly this reason.
    /// </summary>
    public class RiftNetClient : MonoBehaviour
    {
        public static RiftNetClient Instance { get; private set; }

        [Header("Server")]
        [Tooltip("Base URL of the Render service, e.g. https://rift-rally-server.onrender.com")]
        public string ServerUrl = "https://rift-rally-server.onrender.com";

        [Tooltip("Reconnect automatically when the connection drops.")]
        public bool AutoReconnect = true;

        [Tooltip("Seconds between reconnect attempts. Doubles up to a cap.")]
        public float ReconnectDelay = 1.5f;

        [Header("Session")]
        [Tooltip("Stored player token. Written to PlayerPrefs so a player stays signed in.")]
        public string Token;

        public NetStatus Status { get; private set; } = NetStatus.Offline;
        public NetPlayer Me { get; private set; }
        public NetRoom Room { get; private set; }
        public int MySeat { get; private set; } = -1;
        public string LastError { get; private set; }

        /// <summary>True when this client is the one whose turn it is.</summary>
        public bool IsMyTurn => Room != null && Room.IsPlaying && Room.turnSeat == MySeat;

        // ---- events, all raised on the main thread ----
        public event Action<NetStatus> StatusChanged;
        public event Action<NetPlayer> Authenticated;
        public event Action<NetRoom, int> RoomUpdated;
        public event Action<NetTurnMessage> TurnStarted;
        public event Action<NetRoom> MatchStarted;
        public event Action<NetOpponentThrow> OpponentThrew;
        public event Action<NetRoundScored> RoundScored;
        public event Action<NetMatchOver> MatchOver;
        public event Action<string> OpponentConnectionChanged;
        public event Action<NetError> ServerError;

        private ClientWebSocket _socket;
        private CancellationTokenSource _cancellation;
        private readonly ConcurrentQueue<string> _inbound = new();
        private readonly ConcurrentQueue<string> _outbound = new();
        private float _nextReconnectAt;
        private float _currentDelay;
        private bool _wantConnection;

        private const string TokenKey = "riftrally.token";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            if (string.IsNullOrEmpty(Token)) Token = PlayerPrefs.GetString(TokenKey, string.Empty);
            _currentDelay = ReconnectDelay;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Disconnect();
        }

        // ---------------------------------------------------------------- urls

        private string HttpBase => (ServerUrl ?? string.Empty).TrimEnd('/');

        /// <summary>
        /// Derives the socket URL from the HTTP one, so only one address is ever configured
        /// and an https deployment cannot accidentally be paired with an insecure ws://.
        /// </summary>
        public string SocketUrl
        {
            get
            {
                string baseUrl = HttpBase;
                if (baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return "wss://" + baseUrl.Substring(8) + "/ws";
                if (baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    return "ws://" + baseUrl.Substring(7) + "/ws";
                return baseUrl + "/ws";
            }
        }

        // ---------------------------------------------------------------- sign in

        /// <summary>
        /// Gets a token: reuses a stored one, otherwise creates a guest. This is the default
        /// path into the game - one call, no typing, and the resulting account is real.
        /// </summary>
        public IEnumerator SignInAsGuest(string displayName = null, Action<bool> done = null)
        {
            if (!string.IsNullOrEmpty(Token))
            {
                yield return FetchMe(ok =>
                {
                    if (ok) done?.Invoke(true);
                });
                if (Me != null) { done?.Invoke(true); yield break; }
            }

            var body = new NetGuestRequest { displayName = displayName };
            yield return Post("/api/auth/guest", JsonUtility.ToJson(body), null, response =>
            {
                var parsed = Parse<NetAuthResponse>(response);
                if (parsed?.token == null) { done?.Invoke(false); return; }
                StoreSession(parsed);
                done?.Invoke(true);
            }, error =>
            {
                LastError = error;
                done?.Invoke(false);
            });
        }

        /// <summary>Registers an account. An existing guest token upgrades that guest in place.</summary>
        public IEnumerator Register(string email, string password, string displayName,
                                    Action<bool> done = null)
        {
            var body = new NetCredentials { email = email, password = password,
                                            displayName = displayName };
            yield return Post("/api/auth/register", JsonUtility.ToJson(body), Token, response =>
            {
                var parsed = Parse<NetAuthResponse>(response);
                if (parsed?.token == null) { done?.Invoke(false); return; }
                StoreSession(parsed);
                done?.Invoke(true);
            }, error =>
            {
                LastError = error;
                done?.Invoke(false);
            });
        }

        public IEnumerator LogIn(string email, string password, Action<bool> done = null)
        {
            var body = new NetCredentials { email = email, password = password };
            yield return Post("/api/auth/login", JsonUtility.ToJson(body), null, response =>
            {
                var parsed = Parse<NetAuthResponse>(response);
                if (parsed?.token == null) { done?.Invoke(false); return; }
                StoreSession(parsed);
                done?.Invoke(true);
            }, error =>
            {
                LastError = error;
                done?.Invoke(false);
            });
        }

        public void SignOut()
        {
            Disconnect();
            Token = string.Empty;
            Me = null;
            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.Save();
        }

        private void StoreSession(NetAuthResponse response)
        {
            Token = response.token;
            Me = response.player;
            PlayerPrefs.SetString(TokenKey, Token);
            PlayerPrefs.Save();
            Authenticated?.Invoke(Me);
        }

        public IEnumerator FetchMe(Action<bool> done = null)
        {
            yield return Get("/api/me", Token, response =>
            {
                var parsed = Parse<NetMeResponse>(response);
                if (parsed?.player == null) { done?.Invoke(false); return; }
                Me = parsed.player;
                done?.Invoke(true);
            }, error =>
            {
                LastError = error;
                done?.Invoke(false);
            });
        }

        [Serializable]
        private class NetMeResponse
        {
            public NetPlayer player;
            public int rank;
        }

        /// <summary>
        /// Fetches the leaderboard. Works with or without a token, which is what lets a guest
        /// who has not signed in still see where everyone stands.
        /// </summary>
        public IEnumerator FetchLeaderboard(Action<NetLeaderboard> done, int limit = 25,
                                            string kind = "all")
        {
            yield return Get($"/api/leaderboard?limit={limit}&kind={kind}", Token, response =>
            {
                done?.Invoke(Parse<NetLeaderboard>(response));
            }, error =>
            {
                LastError = error;
                done?.Invoke(null);
            });
        }

        // ---------------------------------------------------------------- connection

        public void Connect()
        {
            if (string.IsNullOrEmpty(Token))
            {
                LastError = "cannot connect without a token; sign in first";
                SetStatus(NetStatus.Failed);
                return;
            }

            _wantConnection = true;
            if (Status == NetStatus.Online || Status == NetStatus.Connecting) return;
            StartSocket();
        }

        public void Disconnect()
        {
            _wantConnection = false;
            Room = null;
            MySeat = -1;

            try
            {
                _cancellation?.Cancel();
                _socket?.Abort();
                _socket?.Dispose();
            }
            catch (Exception)
            {
                // Tearing down an already-dead socket is not worth reporting.
            }

            _socket = null;
            _cancellation = null;
            SetStatus(NetStatus.Offline);
        }

        private void StartSocket()
        {
            SetStatus(NetStatus.Connecting);
            _cancellation = new CancellationTokenSource();
            _socket = new ClientWebSocket();
            _ = RunSocket(_socket, _cancellation.Token);
        }

        private async Task RunSocket(ClientWebSocket socket, CancellationToken cancel)
        {
            try
            {
                await socket.ConnectAsync(new Uri(SocketUrl), cancel);
                _inbound.Enqueue("{\"type\":\"__connected\"}");

                var buffer = new byte[8192];
                var builder = new StringBuilder();

                while (socket.State == WebSocketState.Open && !cancel.IsCancellationRequested)
                {
                    // Drain anything queued by the main thread before waiting on a read, so
                    // sends are never stuck behind an idle socket.
                    while (_outbound.TryDequeue(out string pending))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(pending);
                        await socket.SendAsync(new ArraySegment<byte>(bytes),
                                               WebSocketMessageType.Text, true, cancel);
                    }

                    var receive = socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancel);
                    var completed = await Task.WhenAny(receive, Task.Delay(50, cancel));
                    if (completed != receive) continue;

                    WebSocketReceiveResult result = receive.Result;
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (!result.EndOfMessage) continue;

                    _inbound.Enqueue(builder.ToString());
                    builder.Clear();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on a deliberate disconnect.
            }
            catch (Exception e)
            {
                LastError = e.Message;
            }
            finally
            {
                _inbound.Enqueue("{\"type\":\"__disconnected\"}");
            }
        }

        private void Update()
        {
            while (_inbound.TryDequeue(out string raw)) Dispatch(raw);

            if (!_wantConnection || !AutoReconnect) return;

            bool dead = Status == NetStatus.Offline || Status == NetStatus.Reconnecting
                        || Status == NetStatus.Failed;
            if (!dead || Time.unscaledTime < _nextReconnectAt) return;

            // Backs off to avoid hammering a cold-starting free instance, which can take
            // the best part of a minute to wake.
            _currentDelay = Mathf.Min(_currentDelay * 1.6f, 15f);
            _nextReconnectAt = Time.unscaledTime + _currentDelay;
            StartSocket();
        }

        private void Dispatch(string raw)
        {
            NetEnvelope envelope = Parse<NetEnvelope>(raw);
            if (envelope == null || string.IsNullOrEmpty(envelope.type)) return;

            switch (envelope.type)
            {
                case "__connected":
                    SetStatus(NetStatus.Authenticating);
                    Send(new NetAuthRequest { token = Token });
                    break;

                case "__disconnected":
                    Room = null;
                    MySeat = -1;
                    SetStatus(_wantConnection ? NetStatus.Reconnecting : NetStatus.Offline);
                    break;

                case "hello":
                    break;

                case "authenticated":
                {
                    var message = Parse<NetAuthenticated>(raw);
                    Me = message?.player ?? Me;
                    _currentDelay = ReconnectDelay;
                    SetStatus(NetStatus.Online);
                    Authenticated?.Invoke(Me);
                    break;
                }

                case "roomCreated":
                case "roomJoined":
                case "queued":
                case "rejoined":
                case "roomState":
                {
                    var message = Parse<NetRoomMessage>(raw);
                    if (message == null) break;
                    Room = message.room ?? Room;
                    if (message.seat >= 0) MySeat = message.seat;
                    RoomUpdated?.Invoke(Room, MySeat);
                    break;
                }

                case "roomLeft":
                    Room = null;
                    MySeat = -1;
                    RoomUpdated?.Invoke(null, -1);
                    break;

                case "matchStart":
                {
                    var message = Parse<NetRoomMessage>(raw);
                    Room = message?.room ?? Room;
                    MatchStarted?.Invoke(Room);
                    break;
                }

                case "turn":
                {
                    var message = Parse<NetTurnMessage>(raw);
                    if (message?.room != null) Room = message.room;
                    TurnStarted?.Invoke(message);
                    break;
                }

                case "opponentThrow":
                    OpponentThrew?.Invoke(Parse<NetOpponentThrow>(raw));
                    break;

                case "roundScored":
                {
                    var message = Parse<NetRoundScored>(raw);
                    if (message?.room != null) Room = message.room;
                    RoundScored?.Invoke(message);
                    break;
                }

                case "matchOver":
                {
                    var message = Parse<NetMatchOver>(raw);
                    if (message?.room != null) Room = message.room;
                    if (message?.player != null) Me = message.player;
                    MatchOver?.Invoke(message);
                    break;
                }

                case "opponentDisconnected":
                case "opponentReconnected":
                case "opponentLeft":
                case "opponentTimedOut":
                    OpponentConnectionChanged?.Invoke(envelope.type);
                    break;

                case "replaced":
                    _wantConnection = false;
                    LastError = "signed in from another device";
                    SetStatus(NetStatus.Failed);
                    break;

                case "error":
                {
                    var message = Parse<NetError>(raw);
                    LastError = message?.message ?? "server error";
                    ServerError?.Invoke(message);
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- outbound

        private void Send(object payload)
        {
            if (payload == null) return;
            _outbound.Enqueue(JsonUtility.ToJson(payload));
        }

        public void CreateRoom(bool isPublic = false)
            => Send(new NetCreateRoomRequest { isPublic = isPublic });

        public void JoinRoom(string code)
            => Send(new NetJoinRoomRequest { code = (code ?? string.Empty).Trim().ToUpperInvariant() });

        public void QuickMatch() => Send(new NetSimpleRequest { type = "quickMatch" });

        public void SetReady(bool ready = true) => Send(new NetReadyRequest { ready = ready });

        public void LeaveRoom() => Send(new NetSimpleRequest { type = "leaveRoom" });

        public void RequestRoomState() => Send(new NetSimpleRequest { type = "roomState" });

        /// <summary>Reports a completed throw and the resting state of the whole rink.</summary>
        public void SendThrow(NetLaunch launch, List<NetStone> stones)
            => Send(new NetThrowRequest { launch = launch, stones = stones?.ToArray() ?? Array.Empty<NetStone>() });

        private void SetStatus(NetStatus status)
        {
            if (Status == status) return;
            Status = status;
            StatusChanged?.Invoke(status);
        }

        // ---------------------------------------------------------------- http

        private IEnumerator Get(string path, string token, Action<string> ok, Action<string> fail)
        {
            using var request = UnityWebRequest.Get(HttpBase + path);
            if (!string.IsNullOrEmpty(token)) request.SetRequestHeader("Authorization", "Bearer " + token);
            request.timeout = 20;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success) ok?.Invoke(request.downloadHandler.text);
            else fail?.Invoke(DescribeFailure(request));
        }

        private IEnumerator Post(string path, string json, string token, Action<string> ok,
                                 Action<string> fail)
        {
            using var request = new UnityWebRequest(HttpBase + path, "POST");
            byte[] body = Encoding.UTF8.GetBytes(json ?? "{}");
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(token)) request.SetRequestHeader("Authorization", "Bearer " + token);
            request.timeout = 30;   // a cold Render instance can take a while to answer

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success) ok?.Invoke(request.downloadHandler.text);
            else fail?.Invoke(DescribeFailure(request));
        }

        /// <summary>
        /// Turns a failed request into something worth showing a player. The server's own
        /// message is preferred over Unity's generic "HTTP/1.1 400 Bad Request".
        /// </summary>
        private static string DescribeFailure(UnityWebRequest request)
        {
            string body = request.downloadHandler?.text;
            if (!string.IsNullOrEmpty(body))
            {
                var parsed = Parse<NetApiError>(body);
                if (!string.IsNullOrEmpty(parsed?.error)) return parsed.error;
            }
            return string.IsNullOrEmpty(request.error) ? "request failed" : request.error;
        }

        /// <summary>
        /// JsonUtility throws on malformed input, and a single bad message must never take
        /// the client down, so every parse goes through here.
        /// </summary>
        public static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
