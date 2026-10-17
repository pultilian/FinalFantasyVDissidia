using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace DissidiaRosterEditor
{
    // A BepInEx .cfg file: [Section] headers, "Key = Value" lines, '#' comments. Edits keep every
    // other line; a missing file or key is created on save, and BepInEx fills in the comments on
    // the next game start.
    public class BepCfg
    {
        public readonly string Path;
        private readonly List<string> lines = new List<string>();

        public BepCfg(string path)
        {
            Path = path;
            try { if (File.Exists(path)) lines.AddRange(File.ReadAllLines(path)); } catch { }
        }

        public bool Exists => File.Exists(Path);

        private int SectionStart(string section)
        {
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private int KeyLine(string section, string key)
        {
            int s = SectionStart(section);
            if (s < 0) return -1;
            for (int i = s + 1; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("[")) break;
                if (t.StartsWith("#") || t.Length == 0) continue;
                int eq = t.IndexOf('=');
                if (eq > 0 && t.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        public string Get(string section, string key, string fallback)
        {
            int i = KeyLine(section, key);
            if (i < 0) return fallback;
            string t = lines[i]; return t.Substring(t.IndexOf('=') + 1).Trim();
        }

        public bool GetBool(string section, string key, bool fallback) => bool.TryParse(Get(section, key, fallback.ToString()), out bool b) ? b : fallback;

        public void Set(string section, string key, string value)
        {
            int i = KeyLine(section, key);
            if (i >= 0) { lines[i] = key + " = " + value; return; }
            int s = SectionStart(section);
            if (s < 0) { if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add(""); lines.Add("[" + section + "]"); lines.Add(""); s = lines.Count - 2; }
            int insert = s + 1;
            while (insert < lines.Count && !lines[insert].Trim().StartsWith("[")) insert++;
            lines.Insert(insert, key + " = " + value);
            if (insert + 1 < lines.Count && lines[insert + 1].Trim().Length > 0) lines.Insert(insert + 1, "");
        }

        public void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.WriteAllLines(Path, lines, new UTF8Encoding(false));
        }
    }

    // The Limit Break plugin's settings as the Mod Manager exposes them (defaults match the plugin).
    public class LimitSettings
    {
        public string ChargeMode = "Damage", Speed = "Normal";
        public int[] LevelPercents = { 60, 90, 120, 150 }, LevelTurns = { 0, 1, 2, 3 };
        public int TurnsBase = 3;
        public bool MpCost = false, ShowGauge = true, AlwaysReady = false;
        public string GaugeColor = "4CD964", GaugeTrackColor = "55555F";

        public static LimitSettings Load(BepCfg cfg)
        {
            var s = new LimitSettings();
            s.ChargeMode = cfg.Get("Limit", "ChargeMode", s.ChargeMode);
            s.Speed = cfg.Get("Limit", "Speed", s.Speed);
            s.LevelPercents = ParseInts(cfg.Get("Limit", "LevelPercents", ""), s.LevelPercents);
            s.LevelTurns = ParseInts(cfg.Get("Limit", "LevelTurns", ""), s.LevelTurns);
            int.TryParse(cfg.Get("Limit", "TurnsRequired", "3"), out s.TurnsBase); if (s.TurnsBase < 1) s.TurnsBase = 3;
            s.MpCost = cfg.GetBool("Limit", "MpCost", s.MpCost);
            s.ShowGauge = cfg.GetBool("Gauge", "ShowGauge", s.ShowGauge);
            s.GaugeColor = cfg.Get("Gauge", "GaugeColor", s.GaugeColor);
            s.GaugeTrackColor = cfg.Get("Gauge", "GaugeTrackColor", s.GaugeTrackColor);
            s.AlwaysReady = cfg.GetBool("Debug", "AlwaysReady", s.AlwaysReady);
            return s;
        }

        public void SaveTo(BepCfg cfg)
        {
            cfg.Set("Limit", "ChargeMode", ChargeMode);
            cfg.Set("Limit", "Speed", Speed);
            cfg.Set("Limit", "LevelPercents", string.Join(",", LevelPercents));
            cfg.Set("Limit", "LevelTurns", string.Join(",", LevelTurns));
            cfg.Set("Limit", "TurnsRequired", TurnsBase.ToString());
            cfg.Set("Limit", "MpCost", MpCost ? "true" : "false");
            cfg.Set("Gauge", "ShowGauge", ShowGauge ? "true" : "false");
            cfg.Set("Gauge", "GaugeColor", GaugeColor);
            cfg.Set("Gauge", "GaugeTrackColor", GaugeTrackColor);
            cfg.Set("Debug", "AlwaysReady", AlwaysReady ? "true" : "false");
            cfg.Save();
        }

        private static int[] ParseInts(string text, int[] fallback)
        {
            var parts = (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != fallback.Length) return (int[])fallback.Clone();
            var vals = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) if (!int.TryParse(parts[i].Trim(), out vals[i])) return (int[])fallback.Clone();
            return vals;
        }
    }

    // One row of DissidiaLimitJobs.csv.
    public class JobAdjust
    {
        public int JobId; public string JobName; public int PercentAdd, TurnsAdd; public string Comment;
    }

    public class InstallItem
    {
        public string Name, Detail; public bool Present; public bool ThirdParty; public bool Optional;
    }

    // Install moves the mod's files out of the package folder ("FFV Dissidia Mod", next to the
    // Mod Manager in the game folder) into their places in the game; Uninstall moves them back
    // into that folder after taking the Crystal Shards out of the saves. Nothing of the mod is
    // ever deleted, so the same folder installs again. Third-party components are never touched.
    public static class Installer
    {
        public const string PackageFolderName = "FFV Dissidia Mod";
        private const string SA = "FINAL FANTASY V_Data\\StreamingAssets";

        public static string PluginsDir(ModFiles f) => Path.Combine(f.GameDir, "BepInEx", "plugins");
        public static string ConfigDir(ModFiles f) => Path.Combine(f.GameDir, "BepInEx", "config");
        public static string ModsDir(ModFiles f) => Path.Combine(f.GameDir, SA, "Mods", "FFV_Dissidia");
        public static string LimitCfg(ModFiles f) => Path.Combine(ConfigDir(f), "dissidia.limit.cfg");
        public static string JobsCsv(ModFiles f) => Path.Combine(ConfigDir(f), "DissidiaLimitJobs.csv");
        public static string PackageDir(ModFiles f) => Path.Combine(f.GameDir, PackageFolderName);

        // Everything the mod adds to the game folder, relative to it (single files or whole folders).
        // The player's own roster, unlock record, settings and backups live inside these paths, so
        // they travel with the mod and are back after a reinstall.
        public static readonly string[] ModPaths =
        {
            "BepInEx\\plugins\\DissidiaLimit.dll", "BepInEx\\plugins\\DissidiaNameSwap.dll",
            "BepInEx\\config\\DissidiaNameSwap.csv", "BepInEx\\config\\DissidiaLimit.csv",
            "BepInEx\\config\\DissidiaLimitLevels.csv", "BepInEx\\config\\DissidiaLimitJobs.csv", "BepInEx\\config\\DissidiaLimitJobs.default.csv",
            "BepInEx\\config\\dissidia.limit.cfg", "BepInEx\\config\\dissidia.nameswap.cfg",
            "BepInEx\\config\\DissidiaNameSwap.csv.bak", "BepInEx\\config\\DissidiaLimit.csv.bak",
            "BepInEx\\config\\DissidiaRoster",
            SA + "\\Magicite\\FFV_Dissidia", SA + "\\Mods\\FFV_Dissidia",
            "README.txt",
        };
        // What must be present for the mod to count as installed (in the game) or complete (in the package).
        private static readonly string[] CorePaths =
        {
            "BepInEx\\plugins\\DissidiaLimit.dll", "BepInEx\\plugins\\DissidiaNameSwap.dll",
            "BepInEx\\config\\DissidiaNameSwap.csv", "BepInEx\\config\\DissidiaLimit.csv",
            "BepInEx\\config\\DissidiaRoster\\heroes.csv", SA + "\\Magicite\\FFV_Dissidia", SA + "\\Mods\\FFV_Dissidia",
        };

        // Files older builds left at the game root or in BepInEx that no current path covers; removed by Update.
        public static readonly string[] LegacyPaths =
        {
            "README.md", "LIMIT-BREAKS.txt", "LIMIT-BREAKS.md", "MOD-MANAGER.txt", "CRYSTAL-SHARDS.txt", "ROSTER-EDITOR.txt", "INSTALL-GUIDE.md",
            "FFV Dissidia Roster Editor.exe", "BepInEx\\plugins\\DissidiaAbilities.dll.disabled", "BepInEx\\config\\DissidiaAbilities.csv",
        };
        // The player's own files, carried from the installed build into the new package by Update.
        private static readonly string[] UserPaths =
        {
            "BepInEx\\config\\DissidiaRoster\\roster.csv", "BepInEx\\config\\DissidiaRoster\\unlocked.txt",
            "BepInEx\\config\\dissidia.limit.cfg", "BepInEx\\config\\dissidia.nameswap.cfg",
        };
        private const string JobsRel = "BepInEx\\config\\DissidiaLimitJobs.csv", JobsDefaultRel = "BepInEx\\config\\DissidiaLimitJobs.default.csv";

        public static bool IsInstalled(ModFiles f) => CorePaths.All(p => Exists(Path.Combine(f.GameDir, p)));
        // A package is waiting while some build is already in the game: Install becomes Update.
        public static bool UpdatePending(ModFiles f) => PackageReady(f) && PartlyInstalled(f);
        public static bool PartlyInstalled(ModFiles f) => ModPaths.Where(p => !p.EndsWith(".bak") && !p.EndsWith(".cfg")).Any(p => Exists(Path.Combine(f.GameDir, p)));
        public static bool PackageReady(ModFiles f) => CorePaths.All(p => Exists(Path.Combine(PackageDir(f), p)));
        public static bool PackagePartial(ModFiles f) => Directory.Exists(PackageDir(f)) && ModPaths.Any(p => Exists(Path.Combine(PackageDir(f), p)));
        private static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);

        public static bool GameRunning()
        {
            try { return Process.GetProcessesByName("FINAL FANTASY V").Length > 0; } catch { return false; }
        }

        private static string Version(string dll)
        {
            try { var v = FileVersionInfo.GetVersionInfo(dll); return string.IsNullOrEmpty(v.FileVersion) ? "" : v.FileVersion; } catch { return ""; }
        }

        public static List<InstallItem> Status(ModFiles f)
        {
            var list = new List<InstallItem>();
            void Add(string name, string path, bool thirdParty, string detailIfPresent = null)
            {
                bool present = Exists(path);
                string ver = present && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? Version(path) : "";
                list.Add(new InstallItem { Name = name, Present = present, ThirdParty = thirdParty, Detail = present ? (detailIfPresent ?? (ver.Length > 0 ? "version " + ver : "installed")) : "missing: " + path.Substring(Math.Min(path.Length, f.GameDir.Length + 1)) });
            }
            Add("BepInEx (mod loader)", Path.Combine(f.GameDir, "BepInEx", "core", "BepInEx.Core.dll"), true);
            Add("Memoria.FFPR", Path.Combine(PluginsDir(f), "Memoria.FF5.dll"), true);
            Add("Magicite", Path.Combine(PluginsDir(f), "Magicite.dll"), true);
            Add("Dissidia Limit Break plugin", Path.Combine(PluginsDir(f), "DissidiaLimit.dll"), false);
            Add("Dissidia Name Swap plugin", Path.Combine(PluginsDir(f), "DissidiaNameSwap.dll"), false);
            int groups = 0; try { groups = Directory.Exists(f.MagiciteDir) ? Directory.GetDirectories(f.MagiciteDir).Length : 0; } catch { }
            Add("Hero sprites and logos", f.MagiciteDir, false, groups + " sprite groups");
            Add("Limit break data", Path.Combine(ModsDir(f), "Assets", "GameAssets", "Serial", "Data", "Master", "ability.csv"), false, "forged abilities, items and text");
            int heroes = 0; try { heroes = File.Exists(Path.Combine(f.RosterDir, "heroes.csv")) ? File.ReadAllLines(Path.Combine(f.RosterDir, "heroes.csv")).Length - 1 : 0; } catch { }
            Add("Hero library", Path.Combine(f.RosterDir, "heroes.csv"), false, heroes + " heroes");
            Add("Limit settings file", LimitCfg(f), false, "present");
            if (!list[list.Count - 1].Present) { list[list.Count - 1].Optional = true; list[list.Count - 1].Detail = "not created yet - the game writes it on first start (defaults apply until then)"; }
            return list;
        }

        public static string NewBackupDir() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FFV Dissidia backup " + DateTime.Now.ToString("yyyy-MM-dd HHmm"));

        // Moves one file or folder; a folder merges into an existing one, files replace existing files.
        private static bool Move(string srcBase, string dstBase, string rel, StringBuilder log, string verb)
        {
            string src = Path.Combine(srcBase, rel), dst = Path.Combine(dstBase, rel);
            if (!Exists(src)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            if (File.Exists(src))
            {
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(src, dst);
            }
            else if (!Directory.Exists(dst)) Directory.Move(src, dst);
            else
            {
                foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(dst, file.Substring(src.Length + 1));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(file, target);
                }
                Directory.Delete(src, true);
            }
            log.AppendLine(verb + " " + rel);
            return true;
        }

        private static void CheckClosed()
        {
            if (GameRunning()) throw new InvalidOperationException("FINAL FANTASY V is running. Close the game first.");
        }

        // Package folder -> game folder.
        public static string Install(ModFiles f)
        {
            if (!PackageReady(f)) throw new InvalidOperationException("The mod package was not found. Expected the folder \"" + PackageFolderName + "\" in the game folder, next to the Mod Manager, with the mod's files inside.");
            CheckClosed();
            var log = new StringBuilder();
            string pkg = PackageDir(f);
            int n = 0;
            foreach (string rel in ModPaths) if (Move(pkg, f.GameDir, rel, log, "installed")) n++;
            RemoveEmptyFolders(pkg);
            log.AppendLine(n + " items moved into the game folder");
            return log.ToString();
        }

        // Game folder -> package folder, after taking the Crystal Shards out of every save (the game
        // cannot load a save that holds one once the mod's item data is gone).
        public static string Uninstall(ModFiles f, out string backupDir)
        {
            CheckClosed();
            var log = new StringBuilder();
            backupDir = NewBackupDir();
            var saveLog = new List<string>();
            var cleaned = SaveReader.RemoveForgedItemsEverywhere(Path.Combine(backupDir, "saves"), saveLog);
            foreach (string l in saveLog) log.AppendLine(l);
            log.AppendLine(cleaned.Files == 0 ? "no save held Crystal Shards" : $"Crystal Shards removed from {cleaned.Files} save{(cleaned.Files == 1 ? "" : "s")} ({cleaned.Items} items); originals copied to the backup folder");
            if (!Directory.Exists(backupDir)) backupDir = null;
            string pkg = PackageDir(f);
            Directory.CreateDirectory(pkg);
            int n = 0;
            foreach (string rel in ModPaths) if (Move(f.GameDir, pkg, rel, log, "moved to package:")) n++;
            log.AppendLine(n + " items moved to " + pkg);
            return log.ToString();
        }

        // Older build in the game + new build in the package folder: keep the player's own files, drop
        // the old files (including what earlier builds left at the game root), then install the package.
        // Saves are not touched: shard ids are stable between builds.
        public static string Update(ModFiles f, out string backupDir)
        {
            if (!PackageReady(f)) throw new InvalidOperationException("The mod package was not found. Expected the folder \"" + PackageFolderName + "\" in the game folder, next to the Mod Manager, with the new build inside.");
            if (!PartlyInstalled(f)) throw new InvalidOperationException("Nothing to update: FFV: Dissidia is not installed. Use Install.");
            CheckClosed();
            var log = new StringBuilder();
            backupDir = NewBackupDir();
            string keepDir = Path.Combine(backupDir, "previous version");
            int kept = 0;
            foreach (string rel in UserPaths) if (Carry(f, rel, keepDir, log)) kept++;
            string jobs = Path.Combine(f.GameDir, JobsRel), jobsDefault = Path.Combine(f.GameDir, JobsDefaultRel);
            if (File.Exists(jobs) && (!File.Exists(jobsDefault) || !SameText(jobs, jobsDefault)) && Carry(f, JobsRel, keepDir, log)) kept++;   // only an edited job table
            log.AppendLine(kept == 0 ? "no roster or settings to keep" : kept + " of your own files kept (copies in the backup folder)");
            int removed = 0;
            foreach (string rel in ModPaths.Concat(LegacyPaths))
            {
                string p = Path.Combine(f.GameDir, rel);
                if (Directory.Exists(p)) { Directory.Delete(p, true); removed++; log.AppendLine("removed old " + rel); }
                else if (File.Exists(p)) { File.Delete(p); removed++; log.AppendLine("removed old " + rel); }
            }
            log.AppendLine(removed + " items of the previous build removed");
            log.Append(Install(f));
            if (!Directory.Exists(backupDir)) backupDir = null;
            return log.ToString();
        }

        // Copies one of the player's files into the backup folder and into the package (where Install picks it up).
        private static bool Carry(ModFiles f, string rel, string keepDir, StringBuilder log)
        {
            string src = Path.Combine(f.GameDir, rel);
            if (!File.Exists(src)) return false;
            foreach (string dst in new[] { Path.Combine(keepDir, rel), Path.Combine(PackageDir(f), rel) })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(src, dst, true);
            }
            log.AppendLine("kept " + rel);
            return true;
        }

        private static bool SameText(string a, string b)
        {
            try { return File.ReadAllText(a).Replace("\r\n", "\n").Trim() == File.ReadAllText(b).Replace("\r\n", "\n").Trim(); } catch { return false; }
        }

        private static void RemoveEmptyFolders(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (string sub in Directory.GetDirectories(dir)) RemoveEmptyFolders(sub);
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
    }

    public static class JobTable
    {
        public static List<JobAdjust> Load(string path)
        {
            var list = new List<JobAdjust>();
            if (!File.Exists(path)) return list;
            foreach (var row in Csv.Read(path))
            {
                if (row.Length < 4 || !int.TryParse(row[0].Trim(), out int id)) continue;
                int.TryParse(row[2].Trim(), out int pct); int.TryParse(row[3].Trim(), out int turns);
                list.Add(new JobAdjust { JobId = id, JobName = row[1], PercentAdd = pct, TurnsAdd = turns, Comment = row.Length > 4 ? row[4] : "" });
            }
            return list;
        }

        public static void Save(string path, List<JobAdjust> rows)
        {
            var lines = new List<string> { "job_id,job_name,percent_add,turns_add,comment" };
            foreach (var r in rows) lines.Add($"{r.JobId},{Csv.Quote(r.JobName)},{r.PercentAdd},{r.TurnsAdd},{Csv.Quote(r.Comment ?? "")}");
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }
    }
}
