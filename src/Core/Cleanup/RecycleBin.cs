// File: RecycleBin.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SystemOptimizer.Core.Logging;

namespace SystemOptimizer.Core.Cleanup
{
    /// <summary>
    /// Every deletion the cleanup engine performs goes through here, and every deletion
    /// goes to the Recycle Bin. Nothing this application removes is unrecoverable.
    ///
    /// This is a hard rule, not a preference. The earlier engine called File.Delete, which
    /// bypasses the bin entirely; a single mis-scoped path could - and did - destroy work
    /// with no way back.
    /// </summary>
    public static class RecycleBin
    {
        private const uint FO_DELETE = 0x0003;

        private const ushort FOF_SILENT = 0x0004;   // no progress dialog
        private const ushort FOF_NOCONFIRMATION = 0x0010;   // don't ask per item
        private const ushort FOF_ALLOWUNDO = 0x0040;   // <- the whole point
        private const ushort FOF_NOERRORUI = 0x0400;   // we log errors, not popup them
        private const ushort FOF_WANTNUKEWARNING = 0x4000;  // never silently bypass the bin

        /// <summary>
        /// Files at or above this size are left alone rather than risked.
        ///
        /// Windows permanently deletes anything too large for the bin's quota instead of
        /// recycling it - silently, if UI is suppressed. That would break the promise that
        /// everything we remove can be restored, so we simply decline to touch them and say
        /// so in the log. A temp file this big is rare and worth a human decision anyway.
        /// </summary>
        public const long MaxRecyclableBytes = 1024L * 1024L * 1024L; // 1 GB

        // ---- WHAT IS SITTING IN THERE RIGHT NOW --------------------------------

        /// <summary>
        /// NO Pack attribute, deliberately. The shell compares cbSize against its own
        /// sizeof, which on x64 is the naturally aligned 24 bytes - a DWORD, four bytes of
        /// padding, then two __int64s. Forcing Pack=4 makes it 20, the size check fails,
        /// and the call returns an error for a bin that is perfectly readable.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        private static DateTime _binQueriedAt = DateTime.MinValue;
        private static (long Bytes, long Items) _binCached;

        /// <summary>
        /// Everything in the Recycle Bin across all drives - not just what this
        /// application put there.
        ///
        /// The last-clean line reports what ONE run moved; this is the total waiting, and
        /// therefore the number that actually decides whether it is worth emptying. Being
        /// honest about the difference matters: System Optimizer should not take credit
        /// for what other things deleted, so the label says "Recycle Bin", not "cleaned".
        ///
        /// Passing null asks about every drive at once. Cached for ten seconds because the
        /// overlay asks roughly once a second and this walks the bin's index - which held
        /// eleven thousand items after one real run here.
        /// </summary>
        public static (long Bytes, long Items) CurrentContents()
        {
            if ((DateTime.UtcNow - _binQueriedAt) < TimeSpan.FromSeconds(10)) return _binCached;

            try
            {
                var info = new SHQUERYRBINFO();
                info.cbSize = Marshal.SizeOf(info);
                // S_OK only. A non-zero result means the shell could not answer, and
                // reporting a zero we did not measure would be a lie in a standing readout.
                _binCached = SHQueryRecycleBin(null, ref info) == 0
                    ? (info.i64Size, info.i64NumItems)
                    : (-1, -1);
            }
            catch
            {
                _binCached = (-1, -1);
            }

            _binQueriedAt = DateTime.UtcNow;
            return _binCached;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        /// <summary>
        /// Sends a batch of paths to the Recycle Bin in one shell operation.
        ///
        /// Batching is not an optimisation detail - one call for 5,000 temp files instead
        /// of 5,000 calls is the difference between a cleanup that finishes and one that
        /// appears to hang.
        /// </summary>
        /// <returns>True if the shell reported success and nothing was aborted.</returns>
        public static bool Send(IEnumerable<string> paths, out string error) =>
            Send(paths, out error, out _);

        /// <param name="filesInUse">
        /// True when the shell's complaint was ERROR_SHARING_VIOLATION - something had a
        /// file open. Worth telling apart from every other failure: it is the ordinary
        /// case, not a fault, and the caller should say so rather than raise an alarm.
        /// </param>
        public static bool Send(IEnumerable<string> paths, out string error, out bool filesInUse)
        {
            error = null;
            filesInUse = false;

            var list = paths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (list == null || list.Count == 0) return true;

            var op = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                // The shell wants the list single-null separated and double-null terminated.
                pFrom = string.Join("\0", list) + "\0\0",
                pTo = null,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI
                         | FOF_SILENT | FOF_WANTNUKEWARNING
            };

            int result = SHFileOperation(ref op);

            if (result != 0)
            {
                error = Describe(result);
                filesInUse = result == 0x20;   // ERROR_SHARING_VIOLATION
                return false;
            }
            if (op.fAnyOperationsAborted)
            {
                error = "the delete was stopped before it finished";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Turns a shell result into something a person can act on.
        ///
        /// "Shell delete failed (0x00000020)" told the user nothing: it is
        /// ERROR_SHARING_VIOLATION, which in a temp-file cleanup just means something had
        /// a file open. SHFileOperation returns a mix of its own DE_* codes and ordinary
        /// Win32 ones, so both are covered, and the raw value is still appended for
        /// anything unrecognised rather than swallowed.
        /// </summary>
        private static string Describe(int code)
        {
            switch (code)
            {
                case 0x05:   return "access was denied to some of them";
                case 0x20:   return "some of them were open in another program";
                case 0x50:   return "something already exists at one of the paths";
                case 0x75:   return "the operation was cancelled";        // DE_OPCANCELLED
                case 0x78:   return "access was denied to the source files"; // DE_ACCESSDENIEDSRC
                case 0x79:   return "a path was nested too deeply";       // DE_PATHTOODEEP
                case 0x7C:   return "one of the paths was not valid";     // DE_INVALIDFILES
                case 0x81:   return "a file name was too long";           // DE_FILENAMETOOLONG
                case 0x402:  return "the shell did not say why";
                default:     return $"the shell reported error 0x{code:X}";
            }
        }

        // ---- RESTORE -----------------------------------------------------------

        public sealed class RestoreResult
        {
            public int Restored { get; set; }
            public int NotFound { get; set; }
            public List<string> Problems { get; } = new List<string>();
        }

        /// <summary>
        /// Puts the given original paths back where they came from, if they are still in the
        /// Recycle Bin.
        ///
        /// Original paths come from the bin's own $I index files, NOT from the shell.
        ///
        /// This used to enumerate Shell.Application and rebuild each original path as
        /// original-folder + item.Name. That is broken for an entire class of file, because
        /// item.Name is a DISPLAY name: Windows marks .lnk (and .url, and .pif) as
        /// NeverShowExt, so a shortcut recorded as "Episode.mkv.lnk" comes back from the
        /// shell as "Episode.mkv", never matches, and is reported to the user as "no longer
        /// in the Recycle Bin" while it is sitting right there. Recent Files is a default
        /// cleanup target and it is made almost entirely of shortcuts, so a large and
        /// ordinary slice of every clean was silently unrestorable - and the message said
        /// something untrue rather than admitting a failure.
        ///
        /// The $I sidecar holds the real original path as UTF-16, written by the shell when
        /// the item was deleted. It is exact, needs no COM, and cannot be reshaped by
        /// display rules or localisation - the same reasoning that already ruled out
        /// InvokeVerb("Restore"), whose verb name is localised.
        /// </summary>
        public static RestoreResult Restore(IEnumerable<string> originalPaths)
        {
            var result = new RestoreResult();
            var wanted = new HashSet<string>(
                originalPaths?.Where(p => !string.IsNullOrWhiteSpace(p)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (wanted.Count == 0) return result;

            Dictionary<string, string> inBin;
            try
            {
                inBin = IndexRecycleBin();
            }
            catch (Exception ex)
            {
                result.Problems.Add("The Recycle Bin could not be read: " + ex.Message);
                return result;
            }

            foreach (var original in wanted)
            {
                if (!inBin.TryGetValue(original, out string binPath))
                {
                    result.NotFound++;
                    continue;
                }

                try
                {
                    RestoreOne(binPath, original);
                    result.Restored++;
                }
                catch (Exception ex)
                {
                    result.Problems.Add($"{original} - {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// The original paths of everything currently in the bin.
        ///
        /// Exposed so a caller can ask "how much of what I recycled is still there?"
        /// without being handed the $R mapping it has no use for. Reads the $I index, so
        /// it costs one small file read per item in the bin - fine occasionally, far too
        /// expensive on a once-a-second timer. See CleanHistory.LastCleanStillInBin.
        /// </summary>
        public static HashSet<string> PresentOriginalPaths()
        {
            try
            {
                return new HashSet<string>(IndexRecycleBin().Keys, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return null;   // unreadable is not the same as empty, and must not look like it
            }
        }

        /// <summary>
        /// Maps every recycled item's true original path to the $R file holding its data.
        ///
        /// Each deleted item is a pair in C:\$Recycle.Bin\&lt;user SID&gt;: $Rxxxxxxx holds the
        /// contents, $Ixxxxxxx holds the original full path and deletion time. Other users'
        /// SID folders are not readable and are skipped rather than treated as an error.
        /// </summary>
        private static Dictionary<string, string> IndexRecycleBin()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var drive in DriveInfo.GetDrives())
            {
                string root;
                try
                {
                    if (!drive.IsReady) continue;
                    root = System.IO.Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                    if (!Directory.Exists(root)) continue;
                }
                catch { continue; }

                IEnumerable<string> sidDirs;
                try { sidDirs = Directory.EnumerateDirectories(root); }
                catch { continue; }

                foreach (var sidDir in sidDirs)
                {
                    IEnumerable<string> indexFiles;
                    try { indexFiles = Directory.EnumerateFiles(sidDir, "$I*"); }
                    catch { continue; }   // another user's bin

                    foreach (var indexFile in indexFiles)
                    {
                        string original = ReadOriginalPath(indexFile);
                        if (string.IsNullOrEmpty(original)) continue;

                        string name = System.IO.Path.GetFileName(indexFile);
                        string dataPath = System.IO.Path.Combine(sidDir, "$R" + name.Substring(2));

                        // AN INDEX ENTRY WITHOUT ITS DATA FILE IS NOT AN ITEM IN THE BIN.
                        //
                        // $I files outlive their $R partner - one real bin held 42 index
                        // entries against 5 data files. Explorer hides the orphans, so they
                        // are invisible until something reads the index directly. Mapping
                        // them anyway made Restore promise items it could not deliver: it
                        // found 37 of 38 paths, tried to move files that were not there and
                        // reported "36 could not be moved back" - alarming, and wrong. They
                        // are simply no longer in the bin, and saying so is the truth.
                        //
                        // This also settles duplicates. The same path can hold several index
                        // entries from repeated cleans; only one has surviving data, and
                        // skipping the empty ones means the live entry is the one kept
                        // rather than whichever happened to be enumerated last.
                        if (!File.Exists(dataPath) && !Directory.Exists(dataPath)) continue;

                        map[original] = dataPath;
                    }
                }
            }
            return map;
        }

        /// <summary>
        /// Reads the original path out of an $I file.
        ///
        /// Two formats exist. Version 1 (Vista to Windows 8) stores a fixed 260-character
        /// path at offset 24. Version 2 (Windows 10 and later) stores a character count at
        /// offset 24 and the path from offset 28. Both are UTF-16 and null-terminated.
        /// </summary>
        private static string ReadOriginalPath(string indexFile)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(indexFile);
                if (bytes.Length < 28) return null;

                long version = BitConverter.ToInt64(bytes, 0);
                string raw;

                if (version == 1)
                {
                    const int fixedChars = 260;
                    if (bytes.Length < 24 + fixedChars * 2) return null;
                    raw = System.Text.Encoding.Unicode.GetString(bytes, 24, fixedChars * 2);
                }
                else
                {
                    int chars = BitConverter.ToInt32(bytes, 24);
                    if (chars <= 0 || 28 + chars * 2 > bytes.Length) return null;
                    raw = System.Text.Encoding.Unicode.GetString(bytes, 28, chars * 2);
                }

                int end = raw.IndexOf('\0');
                if (end >= 0) raw = raw.Substring(0, end);
                return string.IsNullOrWhiteSpace(raw) ? null : raw;
            }
            catch
            {
                return null;   // an unreadable index entry is one item skipped, not a failure
            }
        }

        private static void RestoreOne(string binPath, string originalPath)
        {
            if (File.Exists(originalPath) || Directory.Exists(originalPath))
                throw new IOException("something already exists at that path");

            string parent = System.IO.Path.GetDirectoryName(originalPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            if (File.Exists(binPath))
            {
                File.Move(binPath, originalPath);
                RemoveMetadataSidecar(binPath);
                return;
            }
            if (Directory.Exists(binPath))
            {
                Directory.Move(binPath, originalPath);
                RemoveMetadataSidecar(binPath);
                return;
            }
            throw new IOException("the item is no longer in the Recycle Bin");
        }

        /// <summary>
        /// Each recycled item is a pair: $R holds the data, $I holds the original path and
        /// deletion time. Moving $R out leaves $I behind as an orphan the Recycle Bin UI
        /// renders as a phantom entry, so it goes too.
        /// </summary>
        private static void RemoveMetadataSidecar(string binPath)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(binPath);
                string name = System.IO.Path.GetFileName(binPath);
                if (dir == null || name == null || !name.StartsWith("$R", StringComparison.OrdinalIgnoreCase))
                    return;

                string sidecar = System.IO.Path.Combine(dir, "$I" + name.Substring(2));
                if (System.IO.File.Exists(sidecar)) System.IO.File.Delete(sidecar);
            }
            catch (Exception ex)
            {
                // Still never worth failing a restore over - but no longer silent. Orphaned
                // $I entries are invisible in Explorer and were the reason Restore reported
                // 36 items it could not move, so if they are being created it needs saying.
                LogHelper.Log("Recycle Bin: could not remove index entry for " + binPath + ": " + ex.Message);
            }
        }
    }
}
