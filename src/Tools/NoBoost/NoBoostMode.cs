// File: Tools/NoBoost/NoBoostMode.cs
using System;
using SystemOptimizer.Core.Settings;

namespace SystemOptimizer.Tools.NoBoost
{
    /// <summary>
    /// The on/off switch for no-boost mode, and notification when it changes.
    ///
    /// All that survives of GamingModeManager, which was 106 lines of which this was
    /// eleven. The rest was dead in three separate ways and is not worth carrying into
    /// the Tools structure:
    ///
    ///   - A ten-second timer, StartGameDetection, that nothing ever started. It matched
    ///     running processes against a hardcoded list of nine executables - "steam",
    ///     "eldenring", "cs2" and so on - chosen at some point and never revisited.
    ///     The user's own no-boost list does this job properly and is the reason the
    ///     feature exists; a fixed guess at what a game is does not need to come too.
    ///   - LoadGamesList and SaveGamesList, never called, reading and writing a list of
    ///     strings against a file that holds a list of objects.
    ///   - GetMonitoredGames and SetMonitoredGames, the accessors for the list the timer
    ///     never loaded.
    ///
    /// If automatic detection is wanted later it belongs in this folder, driven by the
    /// user's list rather than a literal array of game names.
    /// </summary>
    public static class NoBoostMode
    {
        /// <summary>
        /// On or off, remembered across restarts, and ON BY DEFAULT.
        ///
        /// It used to be a plain in-memory bool defaulting to FALSE and written nowhere,
        /// which made the whole feature inert in two ways at once:
        ///
        ///   - Ticking an application in the no-boost list did nothing. The list is the
        ///     feature and the obvious expression of intent, but a separate switch buried
        ///     in the right-click menu had to be found and turned on as well. Mikie ticked
        ///     "claude", watched an automatic boost run anyway, and was right to report it
        ///     as broken.
        ///   - Even after finding that switch, it silently reverted to off at the next
        ///     launch, because nothing persisted it.
        ///
        /// Default ON is what makes ticking sufficient. It costs nothing when the list is
        /// empty: with nothing ticked there is nothing to hold off, which is exactly what
        /// the menu tooltip already says. The menu item now means "suspend this without
        /// clearing my list" rather than "arm the thing you thought you had armed".
        ///
        /// Read through PreferencesManager rather than cached here, so there is one
        /// source - the same rule that settled LastBoostMessage and ShowAdminWarning.
        /// </summary>
        public static bool Enabled
        {
            get => PreferencesManager.Current?.NoBoostEnabled ?? true;
            set
            {
                if (Enabled == value) return;
                PreferencesManager.Current.NoBoostEnabled = value;
                PreferencesManager.SavePreferences();
                Changed?.Invoke(value);
            }
        }

        public static event Action<bool> Changed;
    }
}
