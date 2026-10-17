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
using Last.Battle;
using Last.Data.Master;
using Last.Data.User;
using Last.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DissidiaLimit;

// FFV: Dissidia - Limit Break (battle-menu injection).
// Forged master rows (one command/ability pair per roster character, built by
// tools\Build-Limits.ps1) execute natively once injected into the character's pre-assembled
// CommandList at battle-menu build time (SetCommandData prefix).
//
// v0.9: charge modes + a real on-screen gauge.
//   ChargeMode = Turns  : the limit is ready after TurnsRequired actions by that character
//   ChargeMode = Damage : the limit is ready after taking DamagePercent % of max HP in damage
// The gauge is drawn on each nameplate as a thin bar directly under the ATB bar. FFV's HUD
// prefab has no trance/limit gauge objects (verified in the asset bundle), so the bar is a
// clone of the ATB fill image, recoloured and driven by the charge value.
[BepInPlugin("dissidia.limit", "FFV Dissidia Limit Break", "0.13.0")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;
    internal static ConfigEntry<string> ChargeMode;
    internal static ConfigEntry<int> TurnsRequired;
    internal static ConfigEntry<int> DamagePercent;
    internal static ConfigEntry<bool> ShowGauge;
    internal static ConfigEntry<bool> MpCost;
    internal static ConfigEntry<bool> DebugAlwaysReady;
    internal static ConfigEntry<string> Speed, SpeedMultipliers, LevelPercents, LevelTurns;
    private static float[] speedMults = { 1.5f, 1f, 0.6f };
    private static float[] levelPercents = { 60, 90, 120, 150 };
    private static float[] levelTurns = { 0, 1, 2, 3 };

    // Charge needed for a limit of the given level (1-4), in Damage mode (% of max HP) or Turns
    // mode (actions), scaled by the player's Speed setting. Level 0 = the plain config value.
    internal static float NeedPercent(int level, int jobId)
    {
        float b = level >= 1 && level <= levelPercents.Length ? levelPercents[level - 1] : Math.Max(1, DamagePercent.Value);
        if (LimitInject.JobMods.TryGetValue(jobId, out var m)) b += m.PercentAdd;
        return Math.Max(10f, b) * SpeedMult();
    }
    internal static float NeedTurns(int level, int jobId)
    {
        float b = Math.Max(1, TurnsRequired.Value) + (level >= 1 && level <= levelTurns.Length ? levelTurns[level - 1] : 0);
        if (LimitInject.JobMods.TryGetValue(jobId, out var m)) b += m.TurnsAdd;
        return Math.Max(1f, (float)Math.Ceiling(Math.Max(1f, b) * SpeedMult()));
    }
    internal static float SpeedMult()
    {
        string s = (Speed.Value ?? "").Trim().ToLowerInvariant();
        return s == "slow" ? speedMults[0] : s == "fast" ? speedMults[2] : speedMults[1];
    }
    private static float[] ParseList(string text, float[] fallback)
    {
        try
        {
            var parts = (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var vals = parts.Select(p => float.Parse(p.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            return vals.Length == fallback.Length ? vals : fallback;
        }
        catch { return fallback; }
    }
    internal static ConfigEntry<string> GaugeColor;
    internal static ConfigEntry<string> GaugeTrackColor;

    public override void Load()
    {
        Logger = Log;
        ChargeMode    = Config.Bind("Limit", "ChargeMode", "Damage", "How a Limit charges: Damage (the gauge fills with HP lost) or Turns (actions taken)");
        TurnsRequired = Config.Bind("Limit", "TurnsRequired", 3, "Turns mode: base number of actions a character must take before their Limit is available (LevelTurns and the job table add to it)");
        DamagePercent = Config.Bind("Limit", "DamagePercent", 90, "Damage mode: damage taken, as a percentage of max HP, that fills the gauge");
        MpCost        = Config.Bind("Limit", "MpCost", false, "Limits also cost MP (the MP value of each limit, shown in the command menu). Off: limits are free and only the gauge decides when one can be used");
        Speed         = Config.Bind("Limit", "Speed", "Normal", "How fast every limit charges: Slow, Normal or Fast (multiplies the charge each limit needs)");
        SpeedMultipliers = Config.Bind("Limit", "SpeedMultipliers", "1.5,1,0.6", "Charge multiplier for Slow, Normal, Fast (lower = charges sooner)");
        LevelPercents = Config.Bind("Limit", "LevelPercents", "60,90,120,150", "Damage mode: % of max HP that must be lost to charge a level 1, 2, 3, 4 limit (levels come from the limit data; stronger limits are higher)");
        LevelTurns    = Config.Bind("Limit", "LevelTurns", "0,1,2,3", "Turns mode: actions added to TurnsRequired for a level 1, 2, 3, 4 limit");
        speedMults    = ParseList(SpeedMultipliers.Value, speedMults);
        levelPercents = ParseList(LevelPercents.Value, levelPercents);
        levelTurns    = ParseList(LevelTurns.Value, levelTurns);
        ShowGauge     = Config.Bind("Gauge", "ShowGauge", true, "Draw the limit gauge under each character's ATB bar");
        DebugAlwaysReady = Config.Bind("Debug", "AlwaysReady", false, "Testing: every limit is usable at once - the gauge is always full and the job does not need to be mastered");
        GaugeColor    = Config.Bind("Gauge", "GaugeColor", "4CD964", "Gauge fill colour as RRGGBB hex (brightens when full)");
        GaugeTrackColor = Config.Bind("Gauge", "GaugeTrackColor", "55555F", "Empty part of the gauge as RRGGBB hex");

        LimitInject.LoadMap(Path.Combine(Paths.ConfigPath, "DissidiaLimit.csv"));
        LimitInject.LoadLevels(Path.Combine(Paths.ConfigPath, "DissidiaLimitLevels.csv"));
        LimitInject.LoadJobMods(Path.Combine(Paths.ConfigPath, "DissidiaLimitJobs.csv"));
        var harmony = new Harmony("dissidia.limit");
        harmony.PatchAll(typeof(CommandParsePatch));
        harmony.PatchAll(typeof(AbilityParsePatch));
        harmony.PatchAll(typeof(LearningParsePatch));
        harmony.PatchAll(typeof(CommandMenuTouchPatch));
        harmony.PatchAll(typeof(CommandMenuKeyInputPatch));
        harmony.PatchAll(typeof(MenuUpdateTouchPatch));
        harmony.PatchAll(typeof(MenuUpdateKeyInputPatch));
        harmony.PatchAll(typeof(MenuNormalTouchPatch));
        harmony.PatchAll(typeof(MenuNormalKeyInputPatch));
        harmony.PatchAll(typeof(BattleInitPatch));
        harmony.PatchAll(typeof(ActPatch));
        harmony.PatchAll(typeof(NameplateTouchPatch));
        harmony.PatchAll(typeof(NameplateKeyInputPatch));
        Log.LogInfo($"Dissidia Limit v0.9.2 loaded: {LimitInject.CharLimits.Count} character limits, charge mode {ChargeMode.Value} ({TurnsRequired.Value} turns / {DamagePercent.Value}% HP), gauge {(ShowGauge.Value ? "on" : "off")}.");
    }
}

// Per-character charge state for the current battle.
internal static class LimitGauge
{
    private class State { public int Turns; public int Damage; public int LastHp = -1; public int MaxHp; public bool Announced; }
    private static readonly Dictionary<int, State> States = new();

    private static int Key(OwnedCharacterData d) => d.characterStatusId == 6 ? 1 : d.characterStatusId;
    private static State Get(OwnedCharacterData d) { int k = Key(d); if (!States.TryGetValue(k, out var s)) { s = new State(); States[k] = s; } return s; }
    private static bool DamageMode => string.Equals(Plugin.ChargeMode.Value, "Damage", StringComparison.OrdinalIgnoreCase);

    public static void ResetAll() { States.Clear(); }

    public static float Fill(OwnedCharacterData d)
    {
        if (Plugin.DebugAlwaysReady.Value) return 1f;
        var s = Get(d);
        int level = LimitInject.LevelOf(LimitInject.CommandFor(d));
        if (DamageMode)
        {
            float need = s.MaxHp * Plugin.NeedPercent(level, d.JobId) / 100f;
            return need <= 0 ? 0f : Math.Min(1f, s.Damage / need);
        }
        return Math.Min(1f, s.Turns / Plugin.NeedTurns(level, d.JobId));
    }

    public static bool IsReady(OwnedCharacterData d) => Fill(d) >= 1f;

    // An act is being executed: count the turn, or empty the gauge if it was a limit.
    public static void OnAct(BattleActData act)
    {
        var d = act?.AttackUnitData?.GetOwnedCharacterData();
        if (d == null) return;
        var s = Get(d);
        int cid = act.Command != null ? act.Command.Id : 0;
        if (LimitInject.LimitCommandIds.Contains(cid))
        {
            s.Turns = 0; s.Damage = 0;
            Plugin.Logger.LogInfo($"Limit used: char {d.characterStatusId} command {cid} - gauge reset.");
            return;
        }
        s.Turns++;
    }

    // Nameplate refresh: track HP loss (Damage mode) and return the current fill.
    public static float OnView(OwnedCharacterData d, IntPtr viewPtr)
    {
        var s = Get(d);
        var prm = d.Parameter;
        int hp = -1;
        if (prm != null)
        {
            hp = prm.CurrentHP;
            try { int max = prm.ConfirmedMaxHp(); if (max > 0) s.MaxHp = max; } catch { }
            if (hp > s.MaxHp) s.MaxHp = hp;                              // fallback: highest HP seen
        }
        if (hp >= 0 && s.LastHp >= 0 && hp < s.LastHp)
        {
            s.Damage += s.LastHp - hp;
            if (DamageMode) Plugin.Logger.LogInfo($"Limit gauge: char {d.characterStatusId} took {s.LastHp - hp} (total {s.Damage}, max HP {s.MaxHp}, fill {Fill(d):0.00})");
        }
        if (hp >= 0) s.LastHp = hp;
        float fill = Fill(d);
        if (fill >= 1f && !s.Announced) { s.Announced = true; Plugin.Logger.LogInfo($"Limit gauge: char {d.characterStatusId} is charged."); }
        if (fill < 1f) s.Announced = false;
        return fill;
    }
}

// The on-screen bar: a recoloured clone of the ATB fill image, placed under the ATB bar.
internal static class GaugeBar
{
    private class Bar { public Image Track, Fill; }
    private static readonly Dictionary<IntPtr, Bar> Bars = new();
    private static bool warned;
    private static Sprite white;

    // A 2x2 opaque white sprite: Image.Type.Filled only works with a sprite assigned.
    private static Sprite WhiteSprite()
    {
        if (white != null) return white;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) tex.SetPixel(x, y, Color.white);
        tex.Apply();
        white = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f);
        return white;
    }

    public static void Update(IntPtr viewPtr, CommonGauge atb, float fill)
    {
        if (!Plugin.ShowGauge.Value) return;
        if (!Bars.TryGetValue(viewPtr, out var bar) || bar == null || bar.Fill == null)
        {
            bar = Create(atb);
            if (bar == null) return;
            Bars[viewPtr] = bar;
        }
        if (bar.Track != null) bar.Track.color = Colour(Plugin.GaugeTrackColor.Value, new Color(0.33f, 0.33f, 0.37f));
        var fillColour = Colour(Plugin.GaugeColor.Value, new Color(0.3f, 0.85f, 0.39f));
        bar.Fill.fillAmount = fill;
        bar.Fill.color = fill >= 1f ? Color.Lerp(fillColour, Color.white, 0.4f) : fillColour;
    }

    private static Bar Create(CommonGauge atb)
    {
        try
        {
            var src = atb?.gaugeImage;
            if (src == null) { if (!warned) { warned = true; Plugin.Logger.LogWarning("Limit gauge: nameplate has no ATB gauge image - gauge disabled."); } return null; }
            var track = Clone(src, "dissidia_limit_track", 1f);
            var fill = Clone(src, "dissidia_limit_fill", 0f);
            if (fill == null) { if (track != null) UnityEngine.Object.Destroy(track.gameObject); return null; }
            var rt = fill.GetComponent<RectTransform>();
            Plugin.Logger.LogInfo($"Limit gauge: bar created under ATB (size {rt.sizeDelta.x}x{rt.sizeDelta.y} at {rt.anchoredPosition.x},{rt.anchoredPosition.y}).");
            return new Bar { Track = track, Fill = fill };
        }
        catch (Exception e) { if (!warned) { warned = true; Plugin.Logger.LogWarning($"Limit gauge: could not create bar: {e.Message}"); } return null; }
    }

    // A solid-colour horizontal-fill clone of the ATB fill image, 5px tall, directly under it.
    private static Image Clone(Image src, string name, float fillAmount)
    {
        var go = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent);
        go.name = name;
        foreach (var extra in go.GetComponents<MonoBehaviour>())
            if (extra != null && !(extra.TryCast<Image>() is Image)) UnityEngine.Object.Destroy(extra);   // keep only the Image
        var img = go.GetComponent<Image>();
        if (img == null) { Plugin.Logger.LogWarning("Limit gauge: cloned object has no Image component."); UnityEngine.Object.Destroy(go); return null; }
        img.sprite = WhiteSprite();                                      // solid white, tinted by colour
        img.material = null;                                             // default UI material (the ATB one may ignore colour)
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = 0;
        img.fillAmount = fillAmount;
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        var srcRt = src.GetComponent<RectTransform>();
        rt.anchorMin = srcRt.anchorMin; rt.anchorMax = srcRt.anchorMax; rt.pivot = srcRt.pivot;
        rt.sizeDelta = new Vector2(srcRt.sizeDelta.x, 5f);
        rt.anchoredPosition = new Vector2(srcRt.anchoredPosition.x, srcRt.anchoredPosition.y - srcRt.sizeDelta.y / 2f - 5f);
        rt.localScale = Vector3.one;
        go.transform.SetAsLastSibling();                                 // track first, then fill on top
        go.SetActive(true);
        return img;
    }

    private static Color Colour(string hexValue, Color fallback)
    {
        string hex = (hexValue ?? "").Trim().TrimStart('#');
        if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int v))
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        return fallback;
    }
}

internal static class LimitInject
{
    internal const int DefaultCommandId = 300;                            // shared "Limit"
    internal const int FreelancerJobId = 1;
    private static readonly Color32 Grey = new Color32(120, 120, 120, 255);
    private static readonly Regex CostSuffix = new(@" \(\d+\)$", RegexOptions.Compiled);

    internal static readonly Dictionary<(int, int), int> CharLimits = new();
    internal static readonly HashSet<int> LimitCommandIds = new() { DefaultCommandId };
    internal static readonly Dictionary<int, Command> Captured = new();
    internal static readonly Dictionary<int, int> AbilityMp = new();     // forged ability id -> MP cost
    internal static readonly Dictionary<int, int> JobCaps = new();        // job id -> final learning level
    internal static readonly Dictionary<int, int> Levels = new();         // command id -> charge level 1-4
    internal static readonly Dictionary<int, (float PercentAdd, float TurnsAdd)> JobMods = new();   // job id -> added to the requirement

    // DissidiaLimitJobs.csv: job_id,job_name,percent_add,turns_add - the character's current job
    // shifts how much charge their limit needs (negative = sooner).
    internal static void LoadJobMods(string path)
    {
        if (!File.Exists(path)) { Plugin.Logger.LogInfo("Limit job table not found - no per-job adjustment."); return; }
        foreach (string line in File.ReadAllLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            string[] p = line.Split(',');
            if (p.Length < 3 || !int.TryParse(p[0].Trim(), out int job)) continue;
            float.TryParse(p[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pct);
            float turns = 0; if (p.Length > 3) float.TryParse(p[3].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out turns);
            JobMods[job] = (pct, turns);
        }
        Plugin.Logger.LogInfo($"Limit job table: {JobMods.Count} jobs.");
    }

    // The limit command a character currently has (their job's slot), or the shared Limit.
    internal static int CommandFor(OwnedCharacterData d)
    {
        if (d == null) return DefaultCommandId;
        int cs = d.characterStatusId == 6 ? 1 : d.characterStatusId;
        return CharLimits.TryGetValue((cs, d.JobId), out int cmd) ? cmd : DefaultCommandId;
    }
    internal static int LevelOf(int commandId) => Levels.TryGetValue(commandId, out int lv) ? lv : 0;

    internal static void LoadLevels(string path)
    {
        if (!File.Exists(path)) { Plugin.Logger.LogInfo("Limit levels file not found - every limit charges at the config default."); return; }
        foreach (string line in File.ReadAllLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            string[] p = line.Split(',');
            if (p.Length >= 2 && int.TryParse(p[0].Trim(), out int cmd) && int.TryParse(p[1].Trim(), out int lv)) Levels[cmd] = lv;
        }
        Plugin.Logger.LogInfo($"Limit levels: {Levels.Count} limits, speed {Plugin.Speed.Value} (x{Plugin.SpeedMult():0.##}).");
    }

    internal static void LoadMap(string path)
    {
        if (!File.Exists(path)) { Plugin.Logger.LogWarning($"Limit map not found: {path} - only the shared Limit is available."); return; }
        foreach (string line in File.ReadAllLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            string[] p = line.Split(',');
            if (p.Length < 3) continue;
            if (!int.TryParse(p[0].Trim(), out int cs) || !int.TryParse(p[1].Trim(), out int job) || !int.TryParse(p[2].Trim(), out int cmd)) continue;
            CharLimits[(cs, job)] = cmd;
            LimitCommandIds.Add(cmd);
        }
    }

    private static bool IsCurrentJobMastered(OwnedCharacterData data)
    {
        if (Plugin.DebugAlwaysReady.Value) return true;
        if (data.JobId == FreelancerJobId) return true;                   // hero: story-flag unlock pending
        if (!JobCaps.TryGetValue(data.JobId, out int cap)) return false;
        var owned = data.OwnedJobDataList;
        if (owned == null) return false;
        foreach (var ojObj in owned)
        {
            var oj = ((Il2CppSystem.Object)ojObj).TryCast<OwnedJobData>();
            if (oj != null && oj.Id == data.JobId) return oj.Level >= cap;
        }
        return false;
    }

    private static int DesiredCommandId(OwnedCharacterData data)
    {
        int cs = data.characterStatusId;
        if (cs == 6) cs = 1;                                              // second Bartz entry in character_status.csv
        return CharLimits.TryGetValue((cs, data.JobId), out int id) ? id : DefaultCommandId;
    }

    public static void Apply(OwnedCharacterData data)
    {
        if (data == null) return;
        var list = data.CommandList;
        if (list == null) return;

        int desired = DesiredCommandId(data);
        bool available = IsCurrentJobMastered(data) && LimitGauge.IsReady(data);
        bool present = false;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            var c = list[i];
            if (c == null || !LimitCommandIds.Contains(c.Id)) continue;
            if (c.Id == desired && available) { present = true; continue; }
            list.RemoveAt(i);                                             // stale: other character's limit, unmastered, or not charged
        }

        if (available && !present)
        {
            if (Captured.TryGetValue(desired, out var cmd)) list.Add(cmd);
            else Plugin.Logger.LogWarning($"Limit command {desired} not captured from master data (char {data.characterStatusId} job {data.JobId}).");
        }
    }

    // ---- menu entry: cost label, grey-out, disable ----

    private static int CostOf(Command cmd) => AbilityMp.TryGetValue(cmd.AbilityId, out int c) ? c : 0;

    private static bool CanAfford(OwnedCharacterData data, Command cmd)
    {
        int mp = data.Parameter != null ? data.Parameter.CurrentMP : int.MaxValue;
        return mp >= CostOf(cmd);
    }

    private static void GateEntry(OwnedCharacterData data, Command cmd, Text text, Action<Color32> setColor, Action<bool> setEnable, bool diag)
    {
        int cost = CostOf(cmd);
        if (text != null && cost > 0)
        {
            string baseName = CostSuffix.Replace(text.text ?? "", "");
            string want = $"{baseName} ({cost})";
            if (text.text != want) text.text = want;
        }
        bool ok = CanAfford(data, cmd);
        if (!ok) { setEnable(false); setColor(Grey); }
        if (diag) Plugin.Logger.LogInfo($"Limit entry: command {cmd.Id} cost {cost} mp {(data.Parameter != null ? data.Parameter.CurrentMP : -1)} affordable {ok}");
    }

    public static void GateMenu(OwnedCharacterData data, Il2CppSystem.Collections.Generic.List<Last.UI.KeyInput.BattleCommandSelectContentController> contents, bool diag)
    {
        if (data == null || contents == null) return;
        int found = 0;
        for (int i = 0; i < contents.Count; i++)
        {
            var content = contents[i];
            var cmd = content?.TargetCommand;
            if (cmd == null || !LimitCommandIds.Contains(cmd.Id)) continue;
            found++;
            var view = content.view;
            GateEntry(data, cmd, view?.commandNameText, c => view?.SetCommandNameColor(c), e => content.SetCommandEnable(e), diag);
        }
        if (diag) Plugin.Logger.LogInfo($"Limit menu (key): {contents.Count} entries, {found} limit entries");
    }

    public static void GateMenu(OwnedCharacterData data, Il2CppSystem.Collections.Generic.List<Last.UI.Touch.BattleCommandSelectContentController> contents, bool diag)
    {
        if (data == null || contents == null) return;
        int found = 0;
        for (int i = 0; i < contents.Count; i++)
        {
            var content = contents[i];
            var cmd = content?.TargetCommand;
            if (cmd == null || !LimitCommandIds.Contains(cmd.Id)) continue;
            found++;
            var view = content.view;
            GateEntry(data, cmd, view?.commandNameText, c => view?.SetCommandNameColor(c), e => content.SetEnableButton(e), diag);
        }
        if (diag) Plugin.Logger.LogInfo($"Limit menu (touch): {contents.Count} entries, {found} limit entries");
    }
}

[HarmonyPatch(typeof(Learning), "CreateMaster")]
internal static class LearningParsePatch
{
    private static void Postfix(MasterBase __result)
    {
        try
        {
            var l = __result?.TryCast<Learning>();
            if (l == null || l.TypeId != 5 || l.Value2 == 99) return;
            if (!LimitInject.JobCaps.TryGetValue(l.JobId, out int cur) || l.Value1 > cur)
                LimitInject.JobCaps[l.JobId] = l.Value1;
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"LearningParsePatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(Command), "CreateMaster")]
internal static class CommandParsePatch
{
    private static void Postfix(MasterBase __result)
    {
        try
        {
            var c = __result?.TryCast<Command>();
            if (c != null && LimitInject.LimitCommandIds.Contains(c.Id)) LimitInject.Captured[c.Id] = c;
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"CommandParsePatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(Ability), "CreateMaster")]
internal static class AbilityParsePatch
{
    private static void Postfix(MasterBase __result)
    {
        try
        {
            var a = __result?.TryCast<Ability>();
            if (a != null && a.Id >= 1200)
            {
                if (!Plugin.MpCost.Value) a.UseValue = 0;          // free limits: the data itself says 0 MP
                LimitInject.AbilityMp[a.Id] = a.UseValue;
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"AbilityParsePatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(Last.UI.Touch.BattleCommandSelectController), "SetCommandData")]
internal static class CommandMenuTouchPatch
{
    private static void Prefix(OwnedCharacterData data)
    {
        try { LimitInject.Apply(data); }
        catch (Exception e) { Plugin.Logger.LogWarning($"CommandMenuTouchPatch: {e.Message}"); }
    }
    private static void Postfix(Last.UI.Touch.BattleCommandSelectController __instance, OwnedCharacterData data)
    {
        try { LimitInject.GateMenu(data, __instance.contentList, true); }
        catch (Exception e) { Plugin.Logger.LogWarning($"CommandMenuTouchPatch(post): {e.Message}"); }
    }
}

[HarmonyPatch(typeof(Last.UI.KeyInput.BattleCommandSelectController), "SetCommandData")]
internal static class CommandMenuKeyInputPatch
{
    private static void Prefix(OwnedCharacterData data)
    {
        try { LimitInject.Apply(data); }
        catch (Exception e) { Plugin.Logger.LogWarning($"CommandMenuKeyInputPatch: {e.Message}"); }
    }
    private static void Postfix(Last.UI.KeyInput.BattleCommandSelectController __instance, OwnedCharacterData data)
    {
        try { LimitInject.GateMenu(data, __instance.contentList, true); }
        catch (Exception e) { Plugin.Logger.LogWarning($"CommandMenuKeyInputPatch(post): {e.Message}"); }
    }
}

[HarmonyPatch(typeof(Last.UI.Touch.BattleCommandSelectController), "UpdateController")]
internal static class MenuUpdateTouchPatch
{
    private static void Postfix(Last.UI.Touch.BattleCommandSelectController __instance)
    {
        try { LimitInject.GateMenu(__instance.targetData, __instance.contentList, false); }
        catch (Exception e) { Plugin.Logger.LogWarning($"MenuUpdateTouchPatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(Last.UI.KeyInput.BattleCommandSelectController), "UpdateController")]
internal static class MenuUpdateKeyInputPatch
{
    private static void Postfix(Last.UI.KeyInput.BattleCommandSelectController __instance)
    {
        try { LimitInject.GateMenu(__instance.targetData, __instance.contentList, false); }
        catch (Exception e) { Plugin.Logger.LogWarning($"MenuUpdateKeyInputPatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(Last.UI.Touch.BattleCommandSelectController), "NormalUpdate")]
internal static class MenuNormalTouchPatch
{
    private static void Prefix(Last.UI.Touch.BattleCommandSelectController __instance)
    {
        try { LimitInject.GateMenu(__instance.targetData, __instance.contentList, false); }
        catch (Exception e) { Plugin.Logger.LogWarning($"MenuNormalTouchPatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(Last.UI.KeyInput.BattleCommandSelectController), "NormalUpdate")]
internal static class MenuNormalKeyInputPatch
{
    private static void Prefix(Last.UI.KeyInput.BattleCommandSelectController __instance)
    {
        try { LimitInject.GateMenu(__instance.targetData, __instance.contentList, false); }
        catch (Exception e) { Plugin.Logger.LogWarning($"MenuNormalKeyInputPatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(BattleController), "InitBattleData")]
internal static class BattleInitPatch
{
    private static void Postfix()
    {
        try { LimitGauge.ResetAll(); }
        catch (Exception e) { Plugin.Logger.LogWarning($"BattleInitPatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(BattleActExection), "SetBattleActData", new Type[] { typeof(BattleActData) })]
internal static class ActPatch
{
    private static void Postfix(BattleActData battleActData)
    {
        try { LimitGauge.OnAct(battleActData); }
        catch (Exception e) { Plugin.Logger.LogWarning($"ActPatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(Last.UI.Touch.BattlePlayerInfomationContentController), "UpdateView")]
internal static class NameplateTouchPatch
{
    private static void Postfix(Last.UI.Touch.BattlePlayerInfomationContentController __instance, BattlePlayerData player)
    {
        try
        {
            var d = player?.GetOwnedCharacterData();
            var v = __instance.view;
            if (d == null || v == null) return;
            float fill = LimitGauge.OnView(d, v.Pointer);
            GaugeBar.Update(v.Pointer, v.atbGauge, fill);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"NameplateTouchPatch: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(Last.UI.KeyInput.BattlePlayerInfomationContentController), "UpdateView")]
internal static class NameplateKeyInputPatch
{
    private static void Postfix(Last.UI.KeyInput.BattlePlayerInfomationContentController __instance, BattlePlayerData player)
    {
        try
        {
            var d = player?.GetOwnedCharacterData();
            var v = __instance.view;
            if (d == null || v == null) return;
            float fill = LimitGauge.OnView(d, v.Pointer);
            GaugeBar.Update(v.Pointer, v.atbGauge, fill);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"NameplateKeyInputPatch: {e.Message}"); }
    }
}
