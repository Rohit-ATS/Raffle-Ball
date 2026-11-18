using UnityEngine;

namespace RiftRally.Scenery
{
    /// <summary>
    /// Decides which landscape a game is played in, and remembers where the rotation got to.
    ///
    /// The rule is a rolling cycle rather than a random pick, and that is the whole point of
    /// this class existing instead of a <c>Random.Range</c> at the call site. A random choice
    /// out of eight repeats the same biome back to back about one game in eight, which reads
    /// to a player as the feature being broken - they were promised a new place and got the
    /// one they just left. Walking the list in order guarantees eight different environments
    /// before any of them comes round again, and the starting point is seeded from the clock
    /// on a first run so two people installing the game do not share an opening sequence.
    ///
    /// The position is kept in <see cref="PlayerPrefs"/> so the cycle survives closing the
    /// game: coming back tomorrow continues the tour instead of restarting it.
    /// </summary>
    public static class LandscapeRotation
    {
        private const string IndexKey = "RiftRally.Landscape.Index";
        private const string AutoKey = "RiftRally.Landscape.AutoRotate";
        private const string SeededKey = "RiftRally.Landscape.Seeded";

        /// <summary>
        /// Index of the landscape currently in play. Always a valid catalogue index.
        /// </summary>
        public static int CurrentIndex
        {
            get
            {
                EnsureSeeded();
                int stored = PlayerPrefs.GetInt(IndexKey, 0);
                return ((stored % LandscapeCatalog.Count) + LandscapeCatalog.Count)
                       % LandscapeCatalog.Count;
            }
            set
            {
                int count = LandscapeCatalog.Count;
                PlayerPrefs.SetInt(IndexKey, ((value % count) + count) % count);
                PlayerPrefs.SetInt(SeededKey, 1);
                PlayerPrefs.Save();
                Changed?.Invoke(Current);
            }
        }

        /// <summary>
        /// Whether a new game moves to the next landscape. A player who has found the one
        /// they like can pin it from the options panel, which is why this is a setting and
        /// not a constant.
        /// </summary>
        public static bool AutoRotate
        {
            get => PlayerPrefs.GetInt(AutoKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(AutoKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static Landscape Current => LandscapeCatalog.At(CurrentIndex);

        /// <summary>Raised whenever the selection moves, so the world can be rebuilt.</summary>
        public static event System.Action<Landscape> Changed;

        /// <summary>
        /// Steps to the next landscape and returns it. Called when a game starts.
        /// </summary>
        public static Landscape Advance()
        {
            CurrentIndex = CurrentIndex + 1;
            return Current;
        }

        /// <summary>
        /// Steps forward only when auto-rotation is on, and returns whatever is now selected.
        /// This is the call a match start makes: it does not have to know the setting.
        /// </summary>
        public static Landscape AdvanceForNewGame() => AutoRotate ? Advance() : Current;

        public static Landscape Step(int delta)
        {
            CurrentIndex = CurrentIndex + delta;
            return Current;
        }

        public static Landscape Next() => Step(1);

        public static Landscape Previous() => Step(-1);

        /// <summary>
        /// Selects a landscape by name. Returns false, changing nothing, when the name is
        /// not in the catalogue - a stale name in a saved preference must not wipe the
        /// rotation.
        /// </summary>
        public static bool Select(string name)
        {
            int index = LandscapeCatalog.IndexOf(name);
            if (index < 0) return false;
            CurrentIndex = index;
            return true;
        }

        /// <summary>
        /// Picks a random starting point the first time the game is ever run, so the tour
        /// does not always open on the same biome.
        /// </summary>
        private static void EnsureSeeded()
        {
            if (PlayerPrefs.GetInt(SeededKey, 0) != 0) return;

            int start = Mathf.Abs(unchecked((int)System.DateTime.Now.Ticks))
                        % LandscapeCatalog.Count;
            PlayerPrefs.SetInt(IndexKey, start);
            PlayerPrefs.SetInt(SeededKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>Forgets the stored position. Used by tests and by a settings reset.</summary>
        public static void Forget()
        {
            PlayerPrefs.DeleteKey(IndexKey);
            PlayerPrefs.DeleteKey(SeededKey);
            PlayerPrefs.DeleteKey(AutoKey);
            PlayerPrefs.Save();
        }
    }
}
