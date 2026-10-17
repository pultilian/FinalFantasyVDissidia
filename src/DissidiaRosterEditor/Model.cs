using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DissidiaRosterEditor
{
    public class Hero
    {
        public string Key, Name, BattleName, Game, LimitName, LimitDesc = "";
        public int CommandId, LimitLevel;

        public string LimitText()
        {
            string lv = LimitLevel > 0 ? "  -  Level " + LimitLevel : "";
            return Name + "\n\nLimit Break: " + LimitName + lv + (string.IsNullOrEmpty(LimitDesc) ? "" : "\n" + LimitDesc);
        }
        // Bonus heroes are locked until their Crystal Shard is found in game (unlocks.csv / unlocked.txt).
        public bool Locked;
        public string UnlockHint;          // e.g. "Crystal Shard: Sora - sold in Tule, Weapon Shop (500 gil)"
        public int ShardJob;               // default line-up: the job that unlocks the hero (0 = unlocked by a shard sold in a shop)
        public string ShardItem, ShardTown, ShardShop, ShardAlso, ShardPrice;   // bonus heroes: where the shard is sold

        // Multi-line description for the "Where to get" pop-up.
        public string WhereText(string sourceInfo, bool unlockAll)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Name + (string.IsNullOrEmpty(Game) ? "" : "  (" + Game + ")"));
            sb.AppendLine();
            if (ShardJob > 0)
            {
                sb.AppendLine("Part of the default line-up.");
                sb.AppendLine("Unlocks once the " + (ModLayout.Jobs.FirstOrDefault(j => j.Id == ShardJob)?.Name ?? "job " + ShardJob) + " job has been obtained in the save.");
            }
            else if (!string.IsNullOrEmpty(ShardItem))
            {
                sb.AppendLine("Key item:   " + ShardItem);
                sb.AppendLine("Sold in:    " + ShardTown + ", " + ShardShop + (string.IsNullOrEmpty(ShardPrice) ? "" : "  -  " + ShardPrice + " gil"));
                if (!string.IsNullOrEmpty(ShardAlso)) sb.AppendLine("Also sold:  " + ShardAlso + " (same price)");
            }
            else sb.AppendLine("Always available.");
            sb.AppendLine();
            sb.Append(unlockAll ? "Status: available (Settings: unlock all heroes)" : Locked ? "Status: LOCKED in " + sourceInfo : "Status: unlocked in " + sourceInfo);
            return sb.ToString();
        }

        public override string ToString() => string.IsNullOrEmpty(Game) ? Name : $"{Name}  ({Game})";
    }

    public class Job
    {
        public int Id; public string Name; public string Code;
        public Job(int id, string name, string code) { Id = id; Name = name; Code = code; }
    }

    public class Column
    {
        public string Key, Label; public int[] CharIds;
        public Column(string key, string label, params int[] chars) { Key = key; Label = label; CharIds = chars; }
    }

    // Everything the editor knows about the mod's layout. FFV's ids never change, so these are fixed.
    public static class ModLayout
    {
        public static readonly Job[] Jobs =
        {
            new Job(7,  "Knight",        "J07"), new Job(3,  "Monk",          "J03"), new Job(2,  "Thief",      "J02"),
            new Job(5,  "White Mage",    "J05"), new Job(6,  "Black Mage",    "J06"), new Job(19, "Blue Mage",  "J28"),
            new Job(14, "Berserker",     "J23"), new Job(20, "Mystic Knight", "J29"), new Job(4,  "Red Mage",   "J04"),
            new Job(16, "Time Mage",     "J25"), new Job(13, "Summoner",      "J21"), new Job(22, "Mime",       "J31"),
            new Job(9,  "Ranger",        "J13"), new Job(8,  "Ninja",         "J08"), new Job(21, "Beastmaster","J30"),
            new Job(10, "Geomancer",     "J15"), new Job(12, "Bard",          "J18"), new Job(15, "Samurai",    "J24"),
            new Job(11, "Dragoon",       "J16"), new Job(18, "Dancer",        "J27"), new Job(17, "Chemist",    "J26"),
        };
        public static readonly Column[] Columns =
        {
            new Column("bartz", "Bartz", 1), new Column("lenna", "Lenna", 2),
            new Column("faris", "Faris", 4), new Column("galufkrile", "Galuf / Krile", 3, 5),
        };
        public static readonly string[] CharNames = { "", "Bartz", "Lenna", "Galuf", "Faris", "Krile" };
        public static readonly string[] Poses =
        {
            "Damage_00", "Default_00", "Down_00", "Dying_00", "LeftAttack_00", "LeftAttack_01",
            "Ready_00", "RightAttack_00", "RightAttack_01", "SkillReady_00", "SkillReady_01", "Win_00",
        };
        public static readonly (string Suffix, string LibSub)[] Variants = { ("", "normal"), ("_o", "petrify"), ("_z", "zombie") };
        public const int FreelancerJobId = 1;
        public const int DefaultFreelancerCommand = 385;

        // Galuf leaves before Mime exists; the game has no sprite set for that slot.
        public static bool SlotExists(int charId, int jobId) => !(charId == 3 && jobId == 22);
    }

    // Paths and file IO for one game install.
    public class ModFiles
    {
        public readonly string GameDir, RosterDir, HeroesDir, MagiciteDir, NameSwapCsv, LimitCsv, RosterCsv, DefaultRosterCsv, UnlocksCsv, UnlockedTxt;
        public static bool UnlockAll;      // Settings / --unlock-all: ignore the locks
        public static string ShardSource = "auto";   // auto | slot:<id> | combined  (where Crystal Shards are read from)
        public string ShardSourceInfo = "";          // what the last RefreshLocks actually used (status line)
        private List<SaveSlot> saves;                // cached save listing

        public ModFiles(string gameDir)
        {
            GameDir = gameDir;
            RosterDir = Path.Combine(gameDir, "BepInEx", "config", "DissidiaRoster");
            HeroesDir = Path.Combine(RosterDir, "heroes");
            MagiciteDir = Path.Combine(gameDir, "FINAL FANTASY V_Data", "StreamingAssets", "Magicite", "FFV_Dissidia");
            NameSwapCsv = Path.Combine(gameDir, "BepInEx", "config", "DissidiaNameSwap.csv");
            LimitCsv = Path.Combine(gameDir, "BepInEx", "config", "DissidiaLimit.csv");
            RosterCsv = Path.Combine(RosterDir, "roster.csv");
            DefaultRosterCsv = Path.Combine(RosterDir, "roster.default.csv");
            UnlocksCsv = Path.Combine(RosterDir, "unlocks.csv");
            UnlockedTxt = Path.Combine(RosterDir, "unlocked.txt");
        }

        public static bool LooksLikeGameDir(string dir) =>
            !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "FINAL FANTASY V.exe"));

        public string Missing()
        {
            if (!LooksLikeGameDir(GameDir)) return "FINAL FANTASY V.exe not found in this folder.";
            if (!File.Exists(Path.Combine(RosterDir, "heroes.csv"))) return "BepInEx\\config\\DissidiaRoster\\heroes.csv not found - is the FFV: Dissidia mod installed?";
            if (!Directory.Exists(HeroesDir)) return "BepInEx\\config\\DissidiaRoster\\heroes folder not found.";
            if (!Directory.Exists(MagiciteDir)) return "StreamingAssets\\Magicite\\FFV_Dissidia not found - is the FFV: Dissidia mod installed?";
            return null;
        }

        public List<Hero> LoadHeroes()
        {
            var list = new List<Hero>();
            foreach (var row in Csv.Read(Path.Combine(RosterDir, "heroes.csv")))
            {
                if (row.Length < 6) continue;
                var hero = new Hero { Key = row[0], Name = row[1], BattleName = row[2], Game = row[3], CommandId = int.Parse(row[4]), LimitName = row[5] };
                if (row.Length > 6 && int.TryParse(row[6].Trim(), out int lv)) hero.LimitLevel = lv;
                if (row.Length > 7) hero.LimitDesc = row[7];
                list.Add(hero);
            }
            RefreshLocks(list);
            return list.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // unlocks.csv: content_id,hero_key,item_name[,town,shop,price]  -> heroes that need a shard
        // unlocked.txt: one hero key per line, written by the game plugin when a shard is owned
        // unlocks.csv also carries job_id for the default line-up: those shards are granted by the
        // game when the job is obtained. Re-run after UnlockAll / ShardSource change: it rewrites
        // Locked on the same Hero objects.
        public void RefreshLocks(List<Hero> list)
        {
            foreach (var h in list) h.Locked = false;
            ShardSourceInfo = "";
            if (!File.Exists(UnlocksCsv)) return;
            var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var byContent = new Dictionary<int, string>();
            var jobOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var shopInfo = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Csv.Read(UnlocksCsv))
            {
                if (row.Length < 2 || row[1].Trim().Length == 0) continue;
                string key = row[1].Trim();
                if (int.TryParse(row[0].Trim(), out int cid))
                {
                    byContent[cid] = key;
                    string where = row.Length >= 6 && row[3].Length > 0 ? $" - sold in {row[3]}, {row[4]} ({row[5]} gil)" : "";
                    hints[key] = row[2] + where;
                    shopInfo[key] = row;
                }
                else if (row.Length >= 7 && int.TryParse(row[6].Trim(), out int jobId))
                {
                    jobOf[key] = jobId;
                    hints[key] = "Unlocks once the " + (ModLayout.Jobs.FirstOrDefault(j => j.Id == jobId)?.Name ?? "job " + jobId) + " job is obtained in the save";
                }
            }
            var unlocked = UnlockAll ? null : UnlockedHeroKeys(byContent, jobOf);
            foreach (var h in list)
            {
                if (!hints.TryGetValue(h.Key, out string hint)) continue;
                h.UnlockHint = hint;
                h.ShardJob = jobOf.TryGetValue(h.Key, out int jid) ? jid : 0;
                if (shopInfo.TryGetValue(h.Key, out var row))
                {
                    h.ShardItem = row[2];
                    h.ShardTown = row.Length > 3 ? row[3] : ""; h.ShardPrice = row.Length > 5 ? row[5] : "";
                    string shop = row.Length > 4 ? row[4] : "";
                    int also = shop.IndexOf(" (also ", StringComparison.Ordinal);       // "Armor Shop (also Phantom Village, Item Shop)"
                    h.ShardShop = also >= 0 ? shop.Substring(0, also) : shop;
                    h.ShardAlso = also >= 0 ? shop.Substring(also + 7).TrimEnd(')') : "";
                }
                h.Locked = !UnlockAll && !unlocked.Contains(h.Key);
            }
        }

        public int CountShopShardHeroes(IEnumerable<Hero> heroes) => heroes.Count(h => h.UnlockHint != null && h.ShardJob == 0);

        // Which hero keys the chosen source unlocks: a save file's key items + released jobs, or unlocked.txt.
        private HashSet<string> UnlockedHeroKeys(Dictionary<int, string> byContent, Dictionary<string, int> jobOf)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ShardSource != "combined")
            {
                var slot = PickSlot(out string note);
                if (slot != null)
                {
                    ShardSourceInfo = slot.ShortLabel + note;
                    foreach (int cid in slot.KeyItems) if (byContent.TryGetValue(cid, out string key)) set.Add(key);
                    foreach (var kv in jobOf) if (slot.ReleasedJobs.Contains(kv.Value)) set.Add(kv.Key);
                    return set;
                }
                ShardSourceInfo = "every save combined (no FFV save files found)";
            }
            else ShardSourceInfo = "every save combined";
            if (File.Exists(UnlockedTxt))
                foreach (string line in File.ReadAllLines(UnlockedTxt))
                    if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#")) set.Add(line.Trim());
            return set;
        }

        public List<SaveSlot> LoadSaves(bool rescan = false)
        {
            if (saves == null || rescan) { try { saves = SaveReader.ListAll(); } catch { saves = new List<SaveSlot>(); } }
            return saves;
        }

        private SaveSlot PickSlot(out string note)
        {
            note = "";
            var list = LoadSaves();
            if (list.Count == 0) return null;
            if (ShardSource.StartsWith("slot:") && int.TryParse(ShardSource.Substring(5), out int id))
            {
                var s = list.FirstOrDefault(x => x.Id == id);
                if (s != null) return s;
                note = " - chosen save not found, using the most recent";
            }
            return list.OrderByDescending(x => x.Written).First();
        }

        public int CountLocked(IEnumerable<Hero> heroes) => heroes.Count(h => h.Locked);
        public int CountShardHeroes(IEnumerable<Hero> heroes) => heroes.Count(h => h.UnlockHint != null);

        // ---- sharing rosters ----
        // Same shape as roster.csv plus readable columns: column,job_id,hero_key,hero_name,job_name
        public void ExportRoster(string path, Dictionary<(string, int), Hero> a)
        {
            var lines = new List<string>
            {
                "# FFV: Dissidia roster - load it with Settings > Import roster in the Roster Editor",
                "column,job_id,hero_key,hero_name,job_name",
            };
            foreach (var col in ModLayout.Columns)
                foreach (var job in ModLayout.Jobs)
                {
                    a.TryGetValue((col.Key, job.Id), out Hero h);
                    lines.Add($"{col.Key},{job.Id},{(h == null ? "" : h.Key)},{Csv.Quote(h == null ? "vanilla" : h.Name)},{Csv.Quote(col.Label + " - " + job.Name)}");
                }
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        // Returns (column, jobId) -> heroKey ("" = vanilla). Unknown hero keys are reported, not loaded.
        public Dictionary<(string, int), string> ImportRoster(string path, IEnumerable<Hero> heroes, out List<string> unknown, out int rows)
        {
            var known = new HashSet<string>(heroes.Select(h => h.Key), StringComparer.OrdinalIgnoreCase);
            var map = new Dictionary<(string, int), string>();
            unknown = new List<string>(); rows = 0;
            foreach (var row in Csv.Read(path))
            {
                if (row.Length < 3 || !int.TryParse(row[1].Trim(), out int jid)) continue;
                string col = row[0].Trim().ToLowerInvariant(), key = row[2].Trim();
                if (!ModLayout.Columns.Any(c => c.Key == col) || !ModLayout.Jobs.Any(j => j.Id == jid)) continue;
                rows++;
                if (key.Length > 0 && !known.Contains(key)) { unknown.Add(key); continue; }
                map[(col, jid)] = key;
            }
            return map;
        }

        // (columnKey, jobId) -> heroKey ("" = vanilla)
        public Dictionary<(string, int), string> LoadRoster(bool defaults)
        {
            var path = defaults || !File.Exists(RosterCsv) ? DefaultRosterCsv : RosterCsv;
            var map = new Dictionary<(string, int), string>();
            if (!File.Exists(path)) return map;
            foreach (var row in Csv.Read(path))
                if (row.Length >= 3 && int.TryParse(row[1], out int jid)) map[(row[0], jid)] = row[2];
            return map;
        }

        public string HeroPng(string heroKey, string variant, string pose) =>
            Path.Combine(HeroesDir, heroKey, variant, pose + ".png");

        public string GroupName(int charId, Job job, string suffix) => $"bc_ff5_p00{charId}_{job.Code.ToLower()}{suffix}";

        public string SlotFolder(string group) =>
            Path.Combine(MagiciteDir, group, "Assets", "GameAssets", "Serial", "Res", "Chara", "Battle", group.ToUpper());

        // ---- apply ----
        public string Apply(Dictionary<(string, int), Hero> assignment)
        {
            var log = new StringBuilder();
            int slots = 0, vanilla = 0;
            var applied = LoadRoster(false);
            var locked = assignment.Where(kv => kv.Value.Locked && !(applied.TryGetValue(kv.Key, out string cur) && cur == kv.Value.Key))
                                   .Select(kv => kv.Value.Name).Distinct().ToList();
            if (locked.Count > 0) throw new InvalidOperationException("These heroes are still locked - get their Crystal Shard in game first (or choose \"Unlock all heroes\" in Settings): " + string.Join(", ", locked));
            Backup(NameSwapCsv); Backup(LimitCsv);

            foreach (var col in ModLayout.Columns)
                foreach (var job in ModLayout.Jobs)
                {
                    assignment.TryGetValue((col.Key, job.Id), out Hero hero);
                    foreach (int c in col.CharIds)
                    {
                        if (!ModLayout.SlotExists(c, job.Id)) continue;
                        if (hero == null) { RemoveSlot(c, job); vanilla++; }
                        else { WriteSlot(c, job, hero); slots++; }
                    }
                }

            WriteNameSwap(assignment);
            WriteLimits(assignment);
            SaveRoster(assignment);
            log.AppendLine($"Updated {slots} sprite slots" + (vanilla > 0 ? $", reset {vanilla} to vanilla" : "") + ".");
            log.AppendLine("Battle names and limit breaks updated.");
            log.AppendLine("Restart the game to see the changes.");
            return log.ToString();
        }

        private void WriteSlot(int charId, Job job, Hero hero)
        {
            foreach (var v in ModLayout.Variants)
            {
                string group = GroupName(charId, job, v.Suffix);
                string folder = SlotFolder(group);
                Directory.CreateDirectory(folder);
                foreach (string pose in ModLayout.Poses)
                {
                    string src = HeroPng(hero.Key, v.LibSub, pose);
                    if (!File.Exists(src)) throw new FileNotFoundException($"Missing sprite in hero library: {hero.Name} / {v.LibSub} / {pose}.png");
                    File.Copy(src, Path.Combine(folder, pose + ".png"), true);
                    string meta = Path.Combine(folder, pose + ".spritedata");
                    if (!File.Exists(meta)) File.WriteAllText(meta, SpriteData, new UTF8Encoding(false));
                }
                string keysDir = Path.Combine(MagiciteDir, group, "keys");
                Directory.CreateDirectory(keysDir);
                string keysFile = Path.Combine(keysDir, "Export.json");
                if (!File.Exists(keysFile)) File.WriteAllText(keysFile, ExportJson(group), new UTF8Encoding(false));
            }
        }

        private void RemoveSlot(int charId, Job job)
        {
            foreach (var v in ModLayout.Variants)
            {
                string dir = Path.Combine(MagiciteDir, GroupName(charId, job, v.Suffix));
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        private void WriteNameSwap(Dictionary<(string, int), Hero> a)
        {
            var lines = new List<string> { "character_status_id,job_id,display_name,comment (1=Bartz 2=Lenna 3=Galuf 4=Faris 5=Krile; blank display_name = vanilla; written by Roster Editor)" };
            for (int c = 1; c <= 5; c++)
            {
                lines.Add($"{c},{ModLayout.FreelancerJobId},,{ModLayout.CharNames[c]} - Freelancer");
                foreach (var job in ModLayout.Jobs)
                {
                    if (!ModLayout.SlotExists(c, job.Id)) continue;
                    var col = ModLayout.Columns.First(x => x.CharIds.Contains(c));
                    a.TryGetValue((col.Key, job.Id), out Hero h);
                    string name = h == null ? "" : h.BattleName;
                    string who = h == null ? "vanilla" : h.Name;
                    lines.Add($"{c},{job.Id},{name},{ModLayout.CharNames[c]} - {job.Name} ({who})");
                }
            }
            File.WriteAllLines(NameSwapCsv, lines, new UTF8Encoding(false));
        }

        private void WriteLimits(Dictionary<(string, int), Hero> a)
        {
            // keep whatever Freelancer rows the install already has (shared limit), else the default
            var freelancer = new Dictionary<int, string>();
            if (File.Exists(LimitCsv))
                foreach (var row in Csv.Read(LimitCsv).Skip(1))
                    if (row.Length >= 3 && row[1].Trim() == ModLayout.FreelancerJobId.ToString() && int.TryParse(row[0], out int c)) freelancer[c] = row[2].Trim();

            var lines = new List<string> { "character_status_id,job_id,command_id,comment" };
            for (int c = 1; c <= 5; c++)
            {
                string fl = freelancer.TryGetValue(c, out string v) ? v : ModLayout.DefaultFreelancerCommand.ToString();
                lines.Add($"{c},{ModLayout.FreelancerJobId},{fl},Freelancer ({ModLayout.CharNames[c]})");
                foreach (var job in ModLayout.Jobs)
                {
                    if (!ModLayout.SlotExists(c, job.Id)) continue;
                    var col = ModLayout.Columns.First(x => x.CharIds.Contains(c));
                    a.TryGetValue((col.Key, job.Id), out Hero h);
                    if (h == null) continue;                      // vanilla slot: plugin falls back to the shared Limit
                    lines.Add($"{c},{job.Id},{h.CommandId},{h.LimitName} ({h.Name} - {job.Name})");
                }
            }
            File.WriteAllLines(LimitCsv, lines, new UTF8Encoding(false));
        }

        private void SaveRoster(Dictionary<(string, int), Hero> a)
        {
            var lines = new List<string> { "column,job_id,hero_key" };
            foreach (var col in ModLayout.Columns)
                foreach (var job in ModLayout.Jobs)
                {
                    a.TryGetValue((col.Key, job.Id), out Hero h);
                    lines.Add($"{col.Key},{job.Id},{(h == null ? "" : h.Key)}");
                }
            File.WriteAllLines(RosterCsv, lines, new UTF8Encoding(false));
        }

        private static void Backup(string path)
        {
            if (File.Exists(path) && !File.Exists(path + ".bak")) File.Copy(path, path + ".bak");
        }

        private const string SpriteData = "Rect = [0,0,32,32]\nPivot = [0.5,0]\nPixelsPerUnit = 1\nBorder = [0,0,0,0]\nWrapMode = Clamp\nFilterMode = Point\n";

        private static string ExportJson(string group)
        {
            string g = group.ToUpper();
            var sb = new StringBuilder("{\n  \"keys\": [\n");
            sb.Append(string.Join(",\n", ModLayout.Poses.Select(p => $"    \"{p}\""))).Append("\n  ],\n  \"values\": [\n");
            sb.Append(string.Join(",\n", ModLayout.Poses.Select(p => $"    \"Assets/GameAssets/Serial/Res/Chara/Battle/{g}/{p}\""))).Append("\n  ]\n}\n");
            return sb.ToString();
        }
    }

    // Minimal CSV reader: handles quoted fields with commas; no embedded newlines needed here.
    public static class Csv
    {
        public static IEnumerable<string[]> Read(string path)
        {
            bool first = true;
            foreach (var line in File.ReadAllLines(path))
            {
                if (first) { first = false; continue; }          // header
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                yield return Split(line);
            }
        }

        public static string Quote(string s) => s.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

        public static string[] Split(string line)
        {
            var fields = new List<string>(); var cur = new StringBuilder(); bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (q) { if (ch == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else q = false; } else cur.Append(ch); }
                else if (ch == '"') q = true;
                else if (ch == ',') { fields.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(ch);
            }
            fields.Add(cur.ToString());
            return fields.ToArray();
        }
    }
}
