using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Last.Data.User;

namespace DissidiaNameSwap;

// FFV: Dissidia — Part 2 (name swap).
// Battle: the party HUD shows the roster character name for the unit's current job.
// Job menu: each job row is labeled "Character (Job)".
// Mapping lives in BepInEx/config/DissidiaNameSwap.csv — blank names fall back to vanilla.
// Unlocks: Crystal Shard key items found in game unlock bonus heroes for the Roster Editor
// (UnlockTracker.cs writes BepInEx/config/DissidiaRoster/unlocked.txt).
[BepInPlugin("dissidia.nameswap", "FFV Dissidia Name Swap", "0.4.0")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;
    private static readonly Dictionary<(int charId, int jobId), string> Map = new();
    internal static ConfigEntry<string> RenameHero;
    internal static string RenameFrom = "", RenameTo = "";

    public override void Load()
    {
        Logger = Log;
        LoadMap();
        // Save migration: the hero's name lives in the save file. "From>To" renames him whenever
        // the game fetches him (e.g. Gilgamesh>Bartz after shelving the story; reverse it later).
        RenameHero = Config.Bind("SaveMigration", "RenameHero", "Gilgamesh>Bartz", "Rename the hero (character 1) if his saved name equals the left side: From>To. Empty = off.");
        string[] rn = (RenameHero.Value ?? "").Split('>');
        if (rn.Length == 2 && rn[0].Trim().Length > 0 && rn[1].Trim().Length > 0) { RenameFrom = rn[0].Trim(); RenameTo = rn[1].Trim(); }
        var harmony = new Harmony("dissidia.nameswap");
        harmony.PatchAll(typeof(HeroRenameMemberPatch));
        harmony.PatchAll(typeof(HeroRenameAllMemberPatch));
        harmony.PatchAll(typeof(BattleNameTouchPatch));
        harmony.PatchAll(typeof(BattleNameKeyInputPatch));
        harmony.PatchAll(typeof(JobLabelTouchPatch));
        harmony.PatchAll(typeof(JobLabelKeyInputPatch));
        try
        {
            UnlockTracker.LoadTables();
            harmony.PatchAll(typeof(UnlockAddItemPatch));
            harmony.PatchAll(typeof(ShardStockListPatch));
            harmony.PatchAll(typeof(ShardStockCanBuyPatch));
            harmony.PatchAll(typeof(ShardTradeKeyInputPatch));
            harmony.PatchAll(typeof(ShardTradeTouchPatch));
            harmony.PatchAll(typeof(ShardBuyOnePatch));
            int patched = 0; foreach (var m in harmony.GetPatchedMethods()) patched++;
            Log.LogInfo($"Unlocks: {patched} game methods patched (shop hooks included).");
            AddComponent<UnlockTracker>();
        }
        catch (Exception e) { Log.LogWarning($"Unlock tracker could not start: {e.Message}"); }
        Log.LogInfo($"Dissidia NameSwap loaded with {Map.Count} name mappings; hero rename {(RenameFrom.Length > 0 ? RenameFrom + ">" + RenameTo : "off")}.");
    }

    private void LoadMap()
    {
        string path = Path.Combine(Paths.ConfigPath, "DissidiaNameSwap.csv");
        if (!File.Exists(path))
        {
            Log.LogWarning($"Mapping file not found: {path} — names stay vanilla.");
            return;
        }
        foreach (string line in File.ReadAllLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split(',');
            if (parts.Length < 3) continue;
            if (!int.TryParse(parts[0].Trim(), out int charId)) continue;
            if (!int.TryParse(parts[1].Trim(), out int jobId)) continue;
            string name = parts[2].Trim();
            if (name.Length == 0) continue;
            Map[(charId, jobId)] = name;
        }
    }

    internal static bool TryGetName(int characterStatusId, int jobId, out string name)
    {
        if (characterStatusId == 6) characterStatusId = 1; // second Bartz entry in character_status.csv
        return Map.TryGetValue((characterStatusId, jobId), out name);
    }

    // Rows may be refreshed repeatedly; recover the vanilla job name from an already-wrapped label.
    internal static string WrapJobLabel(string mappedName, string currentText)
    {
        Match m = Regex.Match(currentText, @"^.+ \((.+)\)$");
        string jobName = m.Success ? m.Groups[1].Value : currentText;
        return $"{mappedName} ({jobName})";
    }
}

[HarmonyPatch(typeof(Last.UI.Touch.BattlePlayerInfomationContentController), "UpdateView")]
internal static class BattleNameTouchPatch
{
    private static void Postfix(Last.UI.Touch.BattlePlayerInfomationContentController __instance, BattlePlayerData player)
    {
        try
        {
            OwnedCharacterData data = player?.GetOwnedCharacterData();
            if (data == null) return;
            if (Plugin.TryGetName(data.characterStatusId, data.JobId, out string name))
                __instance.view.SetName(name);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"BattleNameTouchPatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(Last.UI.KeyInput.BattlePlayerInfomationContentController), "UpdateView")]
internal static class BattleNameKeyInputPatch
{
    private static void Postfix(Last.UI.KeyInput.BattlePlayerInfomationContentController __instance, BattlePlayerData player)
    {
        try
        {
            OwnedCharacterData data = player?.GetOwnedCharacterData();
            if (data == null) return;
            if (Plugin.TryGetName(data.characterStatusId, data.JobId, out string name))
                __instance.view.SetName(name);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"BattleNameKeyInputPatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(Serial.FF5.UI.Touch.JobContentController), "SetData")]
internal static class JobLabelTouchPatch
{
    private static void Postfix(Serial.FF5.UI.Touch.JobContentController __instance, int characterStatusId, int jobId)
    {
        try
        {
            if (!Plugin.TryGetName(characterStatusId, jobId, out string name)) return;
            var text = __instance.view.JobNameText;
            if (text == null) return;
            text.text = Plugin.WrapJobLabel(name, text.text);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"JobLabelTouchPatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(Serial.FF5.UI.KeyInput.JobContentController), "SetData")]
internal static class JobLabelKeyInputPatch
{
    private static void Postfix(Serial.FF5.UI.KeyInput.JobContentController __instance, int characterStatusId, int jobId)
    {
        try
        {
            if (!Plugin.TryGetName(characterStatusId, jobId, out string name)) return;
            var text = __instance.view.JobNameText;
            if (text == null) return;
            text.text = Plugin.WrapJobLabel(name, text.text);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"JobLabelKeyInputPatch: {e.Message}"); }
    }
}

internal static class HeroRename
{
    private static bool logged;
    internal static void Apply(OwnedCharacterData d)
    {
        if (d == null || Plugin.RenameFrom.Length == 0) return;
        if (d.characterStatusId != 1 && d.characterStatusId != 6) return;
        if (d.Name != Plugin.RenameFrom) return;
        d.Name = Plugin.RenameTo;
        if (!logged) { logged = true; Plugin.Logger.LogInfo($"Hero renamed in save data: {Plugin.RenameFrom} -> {Plugin.RenameTo}"); }
    }
}

[HarmonyPatch(typeof(Last.Management.UserDataManager), "GetMemberData")]
internal static class HeroRenameMemberPatch
{
    private static void Postfix(OwnedCharacterData __result)
    {
        try { HeroRename.Apply(__result); }
        catch (Exception e) { Plugin.Logger.LogWarning($"HeroRenameMemberPatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(Last.Management.UserDataManager), "GetAllMemberData")]
internal static class HeroRenameAllMemberPatch
{
    private static void Postfix(OwnedCharacterData __result)
    {
        try { HeroRename.Apply(__result); }
        catch (Exception e) { Plugin.Logger.LogWarning($"HeroRenameAllMemberPatch: {e.Message}"); }
    }
}
