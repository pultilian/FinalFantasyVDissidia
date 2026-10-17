using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace DissidiaRosterEditor
{
    // One save slot as the editor sees it: which key items (Crystal Shards) it holds, plus a label.
    public class SaveSlot
    {
        public int Id;                       // 1-20 = File N, 21 = Quick save, 22 = Autosave
        public string Path;
        public DateTime Written;             // file time (the game stamps saves when it writes them)
        public double PlaySeconds;
        public List<string> Party = new List<string>();
        public HashSet<int> KeyItems = new HashSet<int>();
        public List<int> ReleasedJobs = new List<int>();

        public string SlotName => Id == 21 ? "Quick save" : Id == 22 ? "Autosave" : $"File {Id}";
        public string ShortLabel => $"{SlotName} ({Written:yyyy-MM-dd HH:mm})";
        public string Label
        {
            get
            {
                var t = TimeSpan.FromSeconds(PlaySeconds);
                string party = Party.Count == 0 ? "" : string.Join(", ", Party.Take(4)) + "  -  ";
                return $"{SlotName}:  {party}{(int)t.TotalHours}h{t.Minutes:00}m  -  {Written:yyyy-MM-dd HH:mm}";
            }
        }
    }

    // Reads FFV Pixel Remaster save files: base64 -> Rijndael (256-bit block, CBC, fixed key/IV,
    // zero padding) -> DEFLATE -> JSON whose nested objects are themselves JSON strings.
    // Read-only: the editor never writes a save.
    public static class SaveReader
    {
        private static readonly byte[] Key =
        {
            97, 9, 7, 0, 185, 195, 184, 185, 66, 21, 153, 231, 156, 165, 123, 135,
            190, 169, 50, 211, 121, 123, 173, 118, 99, 237, 77, 222, 10, 148, 60, 197,
        };
        private static readonly byte[] Iv =
        {
            2, 36, 249, 119, 31, 219, 110, 14, 59, 213, 8, 215, 183, 149, 191, 46,
            12, 189, 23, 105, 66, 104, 10, 99, 123, 18, 188, 98, 115, 219, 46, 187,
        };

        // Documents\My Games\FINAL FANTASY V PR\Steam\<steam id>\ (every account folder found)
        public static List<string> SaveFolders()
        {
            var list = new List<string>();
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "FINAL FANTASY V PR", "Steam");
                if (Directory.Exists(root)) list.AddRange(Directory.GetDirectories(root));
            }
            catch { }
            return list;
        }

        public static List<SaveSlot> ListAll()
        {
            var slots = new List<SaveSlot>();
            foreach (string folder in SaveFolders())
                foreach (string file in Directory.GetFiles(folder))
                {
                    var info = new FileInfo(file);
                    if (info.Length < 20000) continue;                 // config / flag files are tiny; slots carry a picture
                    try { var s = Parse(file); if (s != null) slots.Add(s); } catch { }
                }
            return slots.OrderBy(s => s.Id).ToList();
        }

        public static SaveSlot Parse(string path)
        {
            string json = Decrypt(File.ReadAllBytes(path));
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var top = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (top == null || !top.ContainsKey("id") || !top.ContainsKey("pictureData") || !top.ContainsKey("userData")) return null;
            var slot = new SaveSlot { Id = Convert.ToInt32(top["id"]), Path = path, Written = File.GetLastWriteTime(path) };
            var user = ser.DeserializeObject(top["userData"] as string) as Dictionary<string, object>;
            if (user == null) return slot;
            if (user.TryGetValue("playTime", out object pt)) slot.PlaySeconds = Convert.ToDouble(pt);
            foreach (var item in Targets(ser, user, "importantOwendItemList"))
                if (item.TryGetValue("contentId", out object cid) && (!item.TryGetValue("count", out object cnt) || Convert.ToInt32(cnt) > 0))
                    slot.KeyItems.Add(Convert.ToInt32(cid));
            foreach (var ch in Targets(ser, user, "ownedCharacterList"))
                if (ch.TryGetValue("name", out object n) && n is string name && name.Length > 0 && !slot.Party.Contains(name)) slot.Party.Add(name);
            if (user.TryGetValue("releasedJobs", out object rj) && ser.DeserializeObject(rj as string) is Dictionary<string, object> jobs && jobs.TryGetValue("target", out object jt) && jt is object[] ja)
                slot.ReleasedJobs = ja.Select(Convert.ToInt32).ToList();
            return slot;
        }

        // userData.<key> is a JSON string {"target":[...]} whose elements are JSON strings again.
        private static IEnumerable<Dictionary<string, object>> Targets(JavaScriptSerializer ser, Dictionary<string, object> user, string key)
        {
            if (!user.TryGetValue(key, out object raw) || !(raw is string s)) yield break;
            if (!(ser.DeserializeObject(s) is Dictionary<string, object> list) || !list.TryGetValue("target", out object t) || !(t is object[] arr)) yield break;
            foreach (object e in arr)
            {
                var d = e as Dictionary<string, object> ?? (e is string es ? ser.DeserializeObject(es) as Dictionary<string, object> : null);
                if (d != null) yield return d;
            }
        }

        // The mod's forged items use content ids from 1700 up; nothing vanilla lives there.
        public const int ForgedContentMin = 1700, ForgedContentMax = 1999;
        public static bool IsForged(int contentId) => contentId >= ForgedContentMin && contentId <= ForgedContentMax;

        // Removes the forged key items from one save file. The file is backed up first (same name,
        // in backupDir), the edit touches only the two item lists' text so every other byte of the
        // JSON is kept, and the rewritten file is decrypted and checked before it replaces the
        // original. Returns how many items were removed (0 = file untouched).
        public static int RemoveForgedItems(string path, string backupDir)
        {
            string json = Decrypt(File.ReadAllBytes(path));
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var top = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (top == null || !(top.TryGetValue("userData", out object udObj) && udObj is string userStr)) return 0;
            var user = ser.DeserializeObject(userStr) as Dictionary<string, object>;
            if (user == null) return 0;
            int removed = 0; string newUserStr = userStr;
            foreach (string key in new[] { "importantOwendItemList", "normalOwnedItemList" })
            {
                if (!user.TryGetValue(key, out object lo) || !(lo is string listStr)) continue;
                if (!(ser.DeserializeObject(listStr) is Dictionary<string, object> list) || !list.TryGetValue("target", out object t) || !(t is object[] arr)) continue;
                var keep = new List<string>(); int dropped = 0;
                foreach (object e in arr)
                {
                    string es = e as string;
                    var d = es != null ? ser.DeserializeObject(es) as Dictionary<string, object> : e as Dictionary<string, object>;
                    int cid = d != null && d.TryGetValue("contentId", out object c) ? Convert.ToInt32(c) : -1;
                    if (IsForged(cid)) { dropped++; continue; }
                    keep.Add(es ?? ser.Serialize(e));
                }
                if (dropped == 0) continue;
                string newListStr = "{\"target\":[" + string.Join(",", keep.Select(k => "\"" + JsonEscape(k) + "\"")) + "]}";
                string escOld = JsonEscape(listStr), escNew = JsonEscape(newListStr);
                int idx = newUserStr.IndexOf(escOld, StringComparison.Ordinal);
                if (idx < 0) throw new InvalidOperationException("could not locate the " + key + " text inside the save");
                newUserStr = newUserStr.Substring(0, idx) + escNew + newUserStr.Substring(idx + escOld.Length);
                removed += dropped;
            }
            if (removed == 0) return 0;
            string escUserOld = JsonEscape(userStr), escUserNew = JsonEscape(newUserStr);
            int j = json.IndexOf(escUserOld, StringComparison.Ordinal);
            if (j < 0) throw new InvalidOperationException("could not locate the userData text inside the save");
            string newJson = json.Substring(0, j) + escUserNew + json.Substring(j + escUserOld.Length);

            byte[] encrypted = Encrypt(newJson);
            var check = ParseJson(Decrypt(encrypted));                      // must decrypt and parse again
            if (check == null || check.KeyItems.Any(IsForged)) throw new InvalidOperationException("the rewritten save did not verify; original left untouched");

            Directory.CreateDirectory(backupDir);
            File.Copy(path, Path.Combine(backupDir, Path.GetFileName(path)), true);
            string tmp = path + ".dissidia-tmp";
            File.WriteAllBytes(tmp, encrypted);
            File.Delete(path); File.Move(tmp, path);
            return removed;
        }

        // Every slot in every save folder: returns (files changed, items removed).
        public static (int Files, int Items) RemoveForgedItemsEverywhere(string backupDir, List<string> log)
        {
            int files = 0, items = 0;
            foreach (string folder in SaveFolders())
                foreach (string file in Directory.GetFiles(folder))
                {
                    if (new FileInfo(file).Length < 20000) continue;
                    try
                    {
                        var slot = Parse(file);
                        if (slot == null || !slot.KeyItems.Any(IsForged)) continue;
                        int n = RemoveForgedItems(file, backupDir);
                        if (n > 0) { files++; items += n; log?.Add($"{slot.SlotName}: removed {n} Crystal Shard{(n == 1 ? "" : "s")}"); }
                    }
                    catch (Exception e) { log?.Add(Path.GetFileName(file) + ": " + e.Message); }
                }
            return (files, items);
        }

        private static SaveSlot ParseJson(string json)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var top = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (top == null || !top.ContainsKey("userData")) return null;
            var slot = new SaveSlot();
            var user = ser.DeserializeObject(top["userData"] as string) as Dictionary<string, object>;
            if (user == null) return null;
            foreach (var item in Targets(ser, user, "importantOwendItemList"))
                if (item.TryGetValue("contentId", out object cid)) slot.KeyItems.Add(Convert.ToInt32(cid));
            foreach (var item in Targets(ser, user, "normalOwnedItemList"))
                if (item.TryGetValue("contentId", out object cid)) slot.KeyItems.Add(Convert.ToInt32(cid));
            return slot;
        }

        // The game's own escaping: backslash, quote and control characters only.
        private static string JsonEscape(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default: if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4")); else sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        // JSON -> DEFLATE -> zero pad to 32 -> Rijndael-256 CBC -> base64, framed as the game writes it.
        private static byte[] Encrypt(string json)
        {
            byte[] data = Encoding.UTF8.GetBytes(json);
            byte[] compressed;
            using (var ms = new MemoryStream())
            {
                using (var z = new DeflateStream(ms, CompressionMode.Compress, true)) z.Write(data, 0, data.Length);
                compressed = ms.ToArray();
            }
            int pad = (32 - compressed.Length % 32) % 32;
            var padded = new byte[compressed.Length + pad];
            Buffer.BlockCopy(compressed, 0, padded, 0, compressed.Length);
            byte[] cipher;
            using (var r = new RijndaelManaged { BlockSize = 256, KeySize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.None, Key = Key, IV = Iv })
            using (var enc = r.CreateEncryptor())
                cipher = enc.TransformFinalBlock(padded, 0, padded.Length);
            string b64 = Convert.ToBase64String(cipher);
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            byte[] body = Encoding.ASCII.GetBytes(b64 + "\r\n");
            var framed = new byte[bom.Length + body.Length];
            Buffer.BlockCopy(bom, 0, framed, 0, 3); Buffer.BlockCopy(body, 0, framed, 3, body.Length);
            return framed;
        }

        private static string Decrypt(byte[] file)
        {
            int start = file.Length >= 3 && file[0] == 0xEF && file[1] == 0xBB && file[2] == 0xBF ? 3 : 0;
            string b64 = Encoding.ASCII.GetString(file, start, file.Length - start).Trim();
            while (b64.Length % 4 != 0) b64 += "=";
            byte[] cipher = Convert.FromBase64String(b64);
            byte[] plain;
            using (var r = new RijndaelManaged { BlockSize = 256, KeySize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.None, Key = Key, IV = Iv })
            using (var dec = r.CreateDecryptor())
                plain = dec.TransformFinalBlock(cipher, 0, cipher.Length - cipher.Length % 32);
            int end = plain.Length;
            while (end > 0 && plain[end - 1] == 0) end--;
            using (var input = new MemoryStream(plain, 0, end))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                deflate.CopyTo(output);
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }
}
