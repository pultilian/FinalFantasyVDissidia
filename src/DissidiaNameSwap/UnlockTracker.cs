using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Last.Data;
using Last.Management;
using Last.Systems;
using UnityEngine;

namespace DissidiaNameSwap;

// Crystal Shards: key items sold in shops (forged by tools\Build-Unlocks.ps1). Owning one
// unlocks a bonus hero in the Roster Editor. This component watches the party's key items and
// keeps BepInEx\config\DissidiaRoster\unlocked.txt up to date (one hero key per line, union of
// everything ever seen - a hero never locks again).
//   unlocks.csv   content_id,hero_key,item_name[,town,shop,price,job_id]   (shipped with the mod)
//   unlocked.txt  hero keys the player has earned                          (written here)
// Rows with a job_id and no content id are the default line-up: those heroes count as unlocked
// once the job is released in the save (no item involved, so old saves work).
public class UnlockTracker : MonoBehaviour
{
    public UnlockTracker(IntPtr ptr) : base(ptr) { }

    internal static readonly Dictionary<int, string> ByContent = new();   // content id -> hero key
    internal static readonly Dictionary<int, string> ItemNames = new();
    internal static readonly Dictionary<int, List<string>> JobHeroes = new();   // job id -> hero keys it unlocks
    private static readonly HashSet<string> Unlocked = new(StringComparer.OrdinalIgnoreCase);
    private static string unlockedPath;
    private static bool loaded, dirty;
    private float next;
    private const float PollSeconds = 5f;

    internal static string RosterDir => Path.Combine(BepInEx.Paths.ConfigPath, "DissidiaRoster");

    internal static void LoadTables()
    {
        unlockedPath = Path.Combine(RosterDir, "unlocked.txt");
        string map = Path.Combine(RosterDir, "unlocks.csv");
        if (!File.Exists(map)) { Plugin.Logger.LogInfo("Unlocks: no unlocks.csv - every hero stays available."); return; }
        foreach (string line in File.ReadAllLines(map).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            string[] p = line.Split(',');
            if (p.Length < 2 || p[1].Trim().Length == 0) continue;
            if (int.TryParse(p[0].Trim(), out int cid))
            {
                ByContent[cid] = p[1].Trim();
                if (p.Length > 2) ItemNames[cid] = p[2].Trim();
            }
            else if (p.Length > 6 && int.TryParse(p[6].Trim(), out int job))
            {
                if (!JobHeroes.TryGetValue(job, out var l)) JobHeroes[job] = l = new List<string>();
                l.Add(p[1].Trim());
            }
        }
        if (File.Exists(unlockedPath))
            foreach (string line in File.ReadAllLines(unlockedPath))
                if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#")) Unlocked.Add(line.Trim());
        loaded = true;
        Plugin.Logger.LogInfo($"Unlocks: {ByContent.Count} Crystal Shards mapped, {JobHeroes.Values.Sum(l => l.Count)} heroes tied to jobs, {Unlocked.Count} heroes already recorded.");
    }

    // Called from the AddOwnedItem postfix (instant) and from the poll (covers loaded saves).
    internal static void Notice(int contentId)
    {
        if (!loaded || !ByContent.TryGetValue(contentId, out string key)) return;
        Record(key, ItemNames.TryGetValue(contentId, out var n) ? n : "item " + contentId);
    }

    private static void Record(string key, string why)
    {
        if (!Unlocked.Add(key)) return;
        dirty = true;
        Plugin.Logger.LogInfo($"Unlocks: {why} -> hero '{key}' recorded as unlocked.");
    }

    private static void Scan()
    {
        var udm = UserDataManager.Instance();
        if (udm == null) return;
        var items = udm.GetImportantOwnedItemsClone();
        if (items == null) return;
        foreach (var it in items)
            if (it != null && it.Count > 0) Notice(it.ContentId);
        RecordReleasedJobs(udm);
    }

    // Default line-up: a hero counts as unlocked once their job is released in the save.
    private static void RecordReleasedJobs(UserDataManager udm)
    {
        if (JobHeroes.Count == 0) return;
        var jobs = udm.GetReleasedJobsClone();
        if (jobs == null || jobs.Count <= 1) return;                 // only Freelancer: no save loaded yet
        foreach (var j in jobs)
        {
            if (j == null || !JobHeroes.TryGetValue(j.Id, out var keys)) continue;
            foreach (string key in keys) Record(key, $"job {j.Id} released");
        }
    }

    private static void Flush()
    {
        if (!dirty) return;
        dirty = false;
        var sb = new StringBuilder();
        sb.Append("# Heroes unlocked by Crystal Shards found in game. Written by FFV: Dissidia - one hero key per line.\n");
        foreach (string k in Unlocked.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) sb.Append(k).Append('\n');
        Directory.CreateDirectory(RosterDir);
        File.WriteAllText(unlockedPath, sb.ToString(), new UTF8Encoding(false));
        Plugin.Logger.LogInfo($"Unlocks: unlocked.txt updated ({Unlocked.Count} heroes).");
    }

    public void Update()
    {
        if (!loaded || Time.unscaledTime < next) return;
        next = Time.unscaledTime + PollSeconds;
        try { Scan(); Flush(); }
        catch (Exception e) { if (failures++ < 3) Plugin.Logger.LogWarning($"Unlocks: scan failed: {e.Message}"); }
    }
    private static int failures;
}

[HarmonyPatch(typeof(OwnedItemClient), "AddOwnedItem", typeof(int), typeof(int))]
internal static class UnlockAddItemPatch
{
    private static void Postfix(int contentId) { try { UnlockTracker.Notice(contentId); } catch { } }
}

// Stock of one: a Crystal Shard the party already owns is dropped from the shop's product list
// (and refused by CanBuy in case a list was built earlier), in every shop that sells it.
internal static class ShardStock
{
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);

    internal static bool IsShardName(string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return false;
        string n = Tags.Replace(displayName, "").Trim();
        foreach (var name in UnlockTracker.ItemNames.Values) if (string.Equals(name, n, StringComparison.Ordinal)) return true;
        return false;
    }

    internal static bool Owned(int contentId)
    {
        if (!UnlockTracker.ByContent.ContainsKey(contentId)) return false;
        var udm = UserDataManager.Instance();
        if (udm == null) return false;
        var important = udm.GetImportantOwnedItemsClone();
        if (important != null)
            foreach (var it in important) if (it != null && it.ContentId == contentId && it.Count > 0) return true;
        var normal = udm.SearchOwnedItem(contentId);
        return normal != null && normal.Count > 0;
    }

    internal static int logBudget = 40;
    internal static void Log(string msg) { if (logBudget-- > 0) Plugin.Logger.LogInfo("ShardStock: " + msg); }
}

[HarmonyPatch(typeof(ShopUtility), "GetProducts")]
internal static class ShardStockListPatch
{
    private static void Postfix(Il2CppSystem.Collections.Generic.List<ShopProductData> __result)
    {
        try
        {
            if (__result == null || UnlockTracker.ByContent.Count == 0) return;
            int before = __result.Count, removed = 0, shards = 0;
            for (int i = __result.Count - 1; i >= 0; i--)
            {
                var p = __result[i];
                if (p == null || !UnlockTracker.ByContent.ContainsKey(p.ContentId)) continue;
                shards++;
                if (ShardStock.Owned(p.ContentId)) { __result.RemoveAt(i); removed++; }
            }
            if (shards > 0) ShardStock.Log($"GetProducts: {before} products, {shards} shards, {removed} already owned removed");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"ShardStockListPatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(ShopUtility), "CanBuy", typeof(ShopProductData))]
internal static class ShardStockCanBuyPatch
{
    private static void Postfix(ShopProductData target, ref bool __result)
    {
        try { if (__result && target != null && ShardStock.Owned(target.ContentId)) { __result = false; ShardStock.Log($"CanBuy refused owned shard {target.ContentId}"); } }
        catch { }
    }
}

// Quantity window: a shard can only be bought one at a time (the data's purchase_limit is ignored
// by the game, so the window's possible/max counts are capped here, for both UI flavours).
[HarmonyPatch(typeof(Last.UI.KeyInput.ShopTradeWindowController), "Show")]
internal static class ShardTradeKeyInputPatch
{
    private static void Prefix(string name, ref int possibleCount, ref int maxCount)
    {
        try
        {
            bool shard = ShardStock.IsShardName(name);
            ShardStock.Log($"KeyInput trade window: name='{name}' possible={possibleCount} max={maxCount} shard={shard}");
            if (shard) { possibleCount = Math.Min(possibleCount, 1); maxCount = 1; }
        }
        catch (Exception e) { ShardStock.Log("KeyInput trade window prefix failed: " + e.Message); }
    }
}

[HarmonyPatch(typeof(Last.UI.Touch.ShopTradeWindowController), "Show")]
internal static class ShardTradeTouchPatch
{
    private static void Prefix(string name, ref int possibleCount, ref int maxCount)
    {
        try
        {
            bool shard = ShardStock.IsShardName(name);
            ShardStock.Log($"Touch trade window: name='{name}' possible={possibleCount} max={maxCount} shard={shard}");
            if (shard) { possibleCount = Math.Min(possibleCount, 1); maxCount = 1; }
        }
        catch (Exception e) { ShardStock.Log("Touch trade window prefix failed: " + e.Message); }
    }
}

// Last line of defence: whatever the window said, a shard purchase is for one copy.
[HarmonyPatch(typeof(ShopUtility), "BuyItem", typeof(ShopProductData), typeof(int))]
internal static class ShardBuyOnePatch
{
    private static void Prefix(ShopProductData data, ref int count)
    {
        try
        {
            if (data == null || !UnlockTracker.ByContent.ContainsKey(data.ContentId)) return;
            ShardStock.Log($"BuyItem: shard {data.ContentId} count {count} -> 1");
            if (count > 1) count = 1;
        }
        catch { }
    }
}
