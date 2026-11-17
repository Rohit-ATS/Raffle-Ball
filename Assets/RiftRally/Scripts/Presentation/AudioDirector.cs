using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;

namespace RiftRally.Presentation
{
    /// <summary>
    /// Every sound in the game, synthesised at load time.
    ///
    /// The clips are generated procedurally rather than imported, which keeps the repository
    /// free of binary audio assets and guarantees the build has sound on every platform. It
    /// also lets the stone impact be pitched and gained by the actual collision impulse, so
    /// a glancing nudge and a hard takeout do not play the same sample at the same volume -
    /// which is the single clearest cue the player has that a shot landed.
    /// </summary>
    public class AudioDirector : MonoBehaviour
    {
        [Header("References")]
        public MatchManager Match;
        public StoneLauncher Launcher;
        public Rink Playfield;

        [Header("Mix")]
        [Range(0f, 1f)] public float MasterVolume = 0.8f;

        private const int SampleRate = 44100;

        private AudioClip _grab;
        private AudioClip _launch;
        private AudioClip _impact;
        private AudioClip _slide;
        private AudioClip _score;
        private AudioClip _fanfare;
        private AudioClip _warp;

        private AudioSource _oneShot;
        private AudioSource _loop;
        private bool _subscribed;

        private void Awake()
        {
            _grab = Blip("rr_grab", 0.09f, 540f, 760f, 0.22f, 0f);
            _launch = Blip("rr_launch", 0.16f, 300f, 170f, 0.30f, 0.15f);
            _impact = Blip("rr_impact", 0.22f, 190f, 90f, 0.55f, 0.55f);
            _slide = Noise("rr_slide", 1.0f, 0.10f, true);
            _score = Arpeggio("rr_score", new[] { 523.25f, 659.25f, 783.99f }, 0.13f, 0.30f);
            _fanfare = Arpeggio("rr_win", new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.17f, 0.34f);
            _warp = Blip("rr_warp", 0.30f, 220f, 1400f, 0.26f, 0.08f);

            _oneShot = gameObject.AddComponent<AudioSource>();
            _oneShot.playOnAwake = false;
            _oneShot.spatialBlend = 0f;

            _loop = gameObject.AddComponent<AudioSource>();
            _loop.playOnAwake = false;
            _loop.loop = true;
            _loop.clip = _slide;
            _loop.volume = 0f;
            _loop.spatialBlend = 0f;
        }

        private void Start()
        {
            if (Playfield == null)
                Playfield = Rink.Instance != null ? Rink.Instance : FindFirstObjectByType<Rink>();
            if (Match == null)
                Match = MatchManager.Instance != null
                    ? MatchManager.Instance
                    : FindFirstObjectByType<MatchManager>();
            if (Launcher == null) Launcher = FindFirstObjectByType<StoneLauncher>();

            Subscribe();
        }

        private void OnDestroy()
        {
            if (!_subscribed) return;
            if (Match != null)
            {
                Match.RoundScored -= OnRoundScored;
                Match.MatchEnded -= OnMatchEnded;
                Match.StealHappened -= OnSteal;
            }
            if (Launcher != null)
            {
                Launcher.StoneGrabbed -= OnGrabbed;
                Launcher.StoneThrown -= OnThrown;
            }
            _subscribed = false;
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            if (Match == null && Launcher == null) return;

            if (Match != null)
            {
                Match.RoundScored += OnRoundScored;
                Match.MatchEnded += OnMatchEnded;
                Match.StealHappened += OnSteal;
            }
            if (Launcher != null)
            {
                Launcher.StoneGrabbed += OnGrabbed;
                Launcher.StoneThrown += OnThrown;
            }
            _subscribed = true;
        }

        // ---------------------------------------------------------------- hooks

        private void OnGrabbed() => Play(_grab, 0.7f, 1f);

        private void OnThrown(Stone stone, float speed)
        {
            float t = Mathf.InverseLerp(GameConfig.MinLaunchSpeed, GameConfig.MaxLaunchSpeed, speed);
            Play(_launch, Mathf.Lerp(0.45f, 1f, t), Mathf.Lerp(1.15f, 0.85f, t));
            if (stone != null) stone.Impacted += OnStoneImpact;
        }

        private void OnStoneImpact(float impulse, Vector3 point)
        {
            float t = Mathf.Clamp01(impulse / 0.6f);
            Play(_impact, Mathf.Lerp(0.2f, 1f, t), Mathf.Lerp(1.3f, 0.78f, t));
        }

        private void OnRoundScored(RoundResult result)
        {
            if (!result.IsBlank) Play(_score, 0.8f, 1f);
        }

        private void OnMatchEnded(PlayerSide winner) => Play(_fanfare, 0.95f, 1f);

        private void OnSteal(PlayerSide side) => Play(_warp, 0.6f, 1.1f);

        /// <summary>Called by the warp gate modifier so the teleport is audible.</summary>
        public void PlayWarp() => Play(_warp, 0.75f, 1f);

        private void Update()
        {
            Subscribe();
            UpdateSlideLoop();
        }

        /// <summary>
        /// The ice hiss tracks the fastest moving stone, so the loop rises and falls with the
        /// action instead of droning. Silence is informative here: it means the rink is still.
        /// </summary>
        private void UpdateSlideLoop()
        {
            if (_loop == null || Playfield == null) return;

            float fastest = 0f;
            var stones = Playfield.Stones;
            for (int i = 0; i < stones.Count; i++)
            {
                var s = stones[i];
                if (s == null || s.State != StoneState.InFlight) continue;
                fastest = Mathf.Max(fastest, s.Speed);
            }

            float target = Mathf.Clamp01(fastest / GameConfig.MaxLaunchSpeed) * 0.35f * MasterVolume;
            _loop.volume = Mathf.MoveTowards(_loop.volume, target, 2.5f * Time.deltaTime);
            _loop.pitch = Mathf.Lerp(0.75f, 1.4f, Mathf.Clamp01(fastest / GameConfig.MaxLaunchSpeed));

            if (_loop.volume > 0.001f && !_loop.isPlaying) _loop.Play();
            else if (_loop.volume <= 0.001f && _loop.isPlaying) _loop.Pause();
        }

        private void Play(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || _oneShot == null) return;
            _oneShot.pitch = Mathf.Clamp(pitch, 0.4f, 2.5f);
            _oneShot.PlayOneShot(clip, Mathf.Clamp01(volume * MasterVolume));
        }

        // ---------------------------------------------------------------- synthesis

        /// <summary>
        /// A single percussive tone that glides from one pitch to another, with an optional
        /// noise layer for grit. This one primitive covers grabs, launches and impacts.
        /// </summary>
        private static AudioClip Blip(string name, float seconds, float startHz, float endHz,
                                      float decay, float grit)
        {
            int count = Mathf.Max(16, (int)(SampleRate * seconds));
            var data = new float[count];
            float phase = 0f;
            var rng = new System.Random(name.GetHashCode());

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / count;
                float hz = Mathf.Lerp(startHz, endHz, t * t);
                phase += 2f * Mathf.PI * hz / SampleRate;

                float env = Mathf.Exp(-t / Mathf.Max(0.0001f, decay)) * (1f - t);
                float tone = Mathf.Sin(phase);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

                data[i] = Mathf.Clamp(Mathf.Lerp(tone, noise, grit) * env, -1f, 1f);
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Filtered noise, used for the sliding-on-ice hiss.</summary>
        private static AudioClip Noise(string name, float seconds, float gain, bool seamless)
        {
            int count = Mathf.Max(16, (int)(SampleRate * seconds));
            var data = new float[count];
            var rng = new System.Random(name.GetHashCode());
            float low = 0f;

            for (int i = 0; i < count; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                // One-pole low pass: white noise alone is a harsh hiss, this reads as ice.
                low = Mathf.Lerp(low, white, 0.18f);
                data[i] = low * gain;
            }

            if (seamless)
            {
                // Cross-fade the tail into the head so the loop has no click.
                int fade = Mathf.Min(1024, count / 4);
                for (int i = 0; i < fade; i++)
                {
                    float k = (float)i / fade;
                    data[i] = Mathf.Lerp(data[count - fade + i], data[i], k);
                }
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A rising run of notes, for scoring and for the win.</summary>
        private static AudioClip Arpeggio(string name, float[] notes, float noteSeconds, float decay)
        {
            int perNote = Mathf.Max(16, (int)(SampleRate * noteSeconds));
            int count = perNote * notes.Length;
            var data = new float[count];

            for (int n = 0; n < notes.Length; n++)
            {
                float phase = 0f;
                for (int i = 0; i < perNote; i++)
                {
                    float t = (float)i / perNote;
                    phase += 2f * Mathf.PI * notes[n] / SampleRate;
                    float env = Mathf.Exp(-t / Mathf.Max(0.0001f, decay));
                    // A touch of third harmonic gives the tone a bell-like edge.
                    float tone = Mathf.Sin(phase) * 0.8f + Mathf.Sin(phase * 3f) * 0.2f;
                    data[n * perNote + i] = Mathf.Clamp(tone * env * 0.7f, -1f, 1f);
                }
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
