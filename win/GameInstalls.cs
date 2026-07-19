using System;
using System.Collections.Generic;
using System.IO;

namespace MakeYourChoice
{
    /// <summary>
    /// Locates the Dead by Daylight executables under a user-chosen game folder.
    ///
    /// DBD ships under a different binary name per storefront, and a player can have more than one
    /// installed. The hard region lock is scoped per-executable (Windows Firewall's -Program takes one
    /// full path — no bare names, no wildcards), so every build present must be found: missing one
    /// leaves it unblocked while the UI still reports "locked".
    ///
    /// We search by FILE NAME rather than a fixed relative path, so we don't have to know each
    /// storefront's folder layout (Binaries\Win64 vs Binaries\WinGDK).
    /// </summary>
    public static class GameInstalls
    {
        public static readonly string[] ExeNames =
        {
            "DeadByDaylight-Win64-Shipping.exe",  // Steam
            "DeadByDaylight-EGS-Shipping.exe",    // Epic Games (also Heroic/Rare/Legendary)
            "DeadByDaylight-WinGDK-Shipping.exe", // Windows Store
        };

        // How deep the fallback walk goes below the chosen folder. Enough for a launcher root that
        // nests the install a few levels down, while stopping a drive root turning into a disk scan.
        private const int MaxDepth = 6;

        /// <summary>
        /// Every DBD executable found under <paramref name="root"/>, which may be an install folder or
        /// a direct path to an .exe. Empty when none are found (the caller then falls back to an
        /// unscoped block). Never throws — an unreadable folder just yields fewer results.
        /// </summary>
        public static List<string> Find(string root)
        {
            var found = new List<string>();
            root = root?.Trim();
            if (string.IsNullOrEmpty(root)) return found;

            try
            {
                // The user may point directly at the exe rather than the install folder.
                if (root.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(root)) found.Add(root);
                    return found;
                }
                if (!Directory.Exists(root)) return found;

                // Fast path: the standard Unreal layout, so the common case costs a handful of
                // File.Exists calls instead of walking the tree.
                foreach (var subDir in new[] { "Win64", "WinGDK" })
                    foreach (var name in ExeNames)
                        AddIfPresent(found, Path.Combine(root, "DeadByDaylight", "Binaries", subDir, name));
                if (found.Count > 0) return found;

                // Fallback: bounded walk for non-standard layouts (and for when the user picks a
                // parent folder). Depth-capped, and unreadable directories are skipped rather than
                // aborting the search — WindowsApps and reparse points are ACL-locked.
                foreach (var dir in EnumerateDirsBounded(root, MaxDepth))
                    foreach (var name in ExeNames)
                        AddIfPresent(found, Path.Combine(dir, name));
            }
            catch { /* best-effort: no scoping is handled by the caller */ }

            return found;
        }

        /// <summary>
        /// The install ROOT folder for <paramref name="gamePath"/> — the folder that directly contains
        /// <c>DeadByDaylight\</c> and <c>EasyAntiCheat\</c>. Null when it can't be determined.
        ///
        /// The game-path setting may hold either an install folder or a direct path to an executable
        /// (the only option for installs we can't browse to). The firewall only needs the executables,
        /// but the content features — custom splash art, skip trailer — build paths relative to the
        /// install root, so they must resolve it rather than using the raw setting.
        /// </summary>
        public static string ResolveInstallRoot(string gamePath)
        {
            gamePath = gamePath?.Trim();
            if (string.IsNullOrEmpty(gamePath)) return null;

            try
            {
                // A folder is already the root (this is the common case, and matches old behaviour).
                if (Directory.Exists(gamePath)) return gamePath;
                if (!File.Exists(gamePath)) return null;

                // A direct exe: climb out of Binaries\<Platform>\ looking for the install root. Walking
                // up rather than assuming a fixed depth keeps this working across storefront layouts.
                var dir = Path.GetDirectoryName(gamePath);
                for (int up = 0; up < 6 && !string.IsNullOrEmpty(dir); up++)
                {
                    if (LooksLikeInstallRoot(dir)) return dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { /* unreadable -> treated as not found */ }

            return null;
        }

        // The install root is the folder holding the game's content tree and the EAC folder — the two
        // things the content features write into.
        private static bool LooksLikeInstallRoot(string dir)
        {
            try
            {
                return Directory.Exists(Path.Combine(dir, "DeadByDaylight", "Content"))
                    || Directory.Exists(Path.Combine(dir, "EasyAntiCheat"));
            }
            catch { return false; }
        }

        private static void AddIfPresent(List<string> found, string candidate)
        {
            if (!File.Exists(candidate)) return;
            foreach (var existing in found)
                if (string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase)) return;
            found.Add(candidate);
        }

        // Breadth-first directory walk with a depth cap, skipping directories we can't read.
        private static IEnumerable<string> EnumerateDirsBounded(string root, int maxDepth)
        {
            var level = new List<string> { root };
            for (int depth = 0; depth <= maxDepth && level.Count > 0; depth++)
            {
                foreach (var dir in level) yield return dir;

                var next = new List<string>();
                foreach (var dir in level)
                {
                    try { next.AddRange(Directory.EnumerateDirectories(dir)); }
                    catch { /* unreadable (ACL / reparse point) -> skip this branch */ }
                }
                level = next;
            }
        }
    }
}
