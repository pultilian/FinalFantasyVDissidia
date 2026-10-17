using System;
using System.Linq;
using System.Windows.Forms;

namespace DissidiaRosterEditor
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Headless modes for scripting/testing:
            //   --check [gameDir]   validate the install and print what was found
            //   --apply [gameDir]   apply the saved roster.csv (or the default) without a window
            //   --export <file> [gameDir] / --import <file> [gameDir]   share rosters without a window
            //   --unlock-all        (any mode) ignore the Crystal Shard locks for this run (the Unlocks
            //                       page has the same switch, saved in %AppData%\FFVDissidia\settings.txt)
            //   --install [gameDir] / --uninstall [gameDir]   move the mod between the "FFV Dissidia Mod" package
            //                       folder and the game (uninstall also takes the Crystal Shards out of the saves)
            //   --update [gameDir]  replace an installed older build with the package (keeps roster and settings)
            //   --clean-saves       remove the Crystal Shards from every save (backups in Documents)
            //   --screenshot <png> [roster|limits|unlocks|install] [gameDir]   render a page and exit
            //   --screenshot-picker <png> [game]                     render the hero picker and exit
            ModFiles.UnlockAll = Settings.UnlockAll;
            ModFiles.ShardSource = Settings.Get("shard_source", "auto");
            if (args.Contains("--unlock-all")) { ModFiles.UnlockAll = true; args = args.Where(a => a != "--unlock-all").ToArray(); }
            if (args.Length > 0 && (args[0] == "--check" || args[0] == "--apply"))
                return Headless(args[0] == "--apply", args.Length > 1 ? args[1] : Settings.FindGameDir());
            if (args.Length > 0 && args[0] == "--clean-saves")                 // remove the forged key items from every save (backups in Documents)
            {
                string backup = System.IO.Path.Combine(Installer.NewBackupDir(), "saves");
                var log = new System.Collections.Generic.List<string>();
                var r = SaveReader.RemoveForgedItemsEverywhere(backup, log);
                foreach (string l in log) Console.WriteLine(l);
                Console.WriteLine($"files changed: {r.Files}, items removed: {r.Items}, backups: {backup}");
                return 0;
            }
            if (args.Length > 0 && (args[0] == "--install" || args[0] == "--uninstall" || args[0] == "--update"))      // package folder <-> game folder, no window
            {
                var f = new ModFiles(args.Length > 1 ? args[1] : Settings.FindGameDir());
                if (!ModFiles.LooksLikeGameDir(f.GameDir)) { Console.Error.WriteLine("ERROR: FINAL FANTASY V.exe not found in " + f.GameDir); return 2; }
                try
                {
                    string backup = null;
                    if (args[0] == "--install") Console.Write(Installer.Install(f));
                    else if (args[0] == "--update") Console.Write(Installer.Update(f, out backup));
                    else Console.Write(Installer.Uninstall(f, out backup));
                    if (backup != null) Console.WriteLine("backups: " + backup);
                }
                catch (Exception ex) { Console.Error.WriteLine("ERROR: " + ex.Message); return 1; }
                return 0;
            }
            if (args.Length > 1 && (args[0] == "--export" || args[0] == "--import"))
                return Transfer(args[0] == "--import", args[1], args.Length > 2 ? args[2] : Settings.FindGameDir());

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 1 && args[0] == "--screenshot-picker")
            {
                var main = new MainForm();
                main.Shown += (s, e) =>
                {
                    var t = new Timer { Interval = 1500 };
                    t.Tick += (s2, e2) =>
                    {
                        t.Stop();
                        Form dlg = main.DebugPicker(args.Length > 2 ? args[2] : null);
                        var t2 = new Timer { Interval = 1500 };
                        t2.Tick += (s3, e3) =>
                        {
                            t2.Stop();
                            using (var bmp = new System.Drawing.Bitmap(dlg.Width, dlg.Height))
                            {
                                dlg.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, dlg.Width, dlg.Height));
                                bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                            }
                            dlg.Close(); main.Close();
                        };
                        dlg.Shown += (s3, e3) => t2.Start();
                        dlg.ShowDialog(main);
                    };
                    t.Start();
                };
                Application.Run(main);
                return 0;
            }
            if (args.Length > 1 && args[0] == "--screenshot")
            {
                var form = new MainForm();
                var timer = new Timer { Interval = 2000 };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    if (args.Length > 3) form.SetGameDir(args[3]);
                    if (args.Length > 2) form.ShowPage(args[2]);
                    var t2 = new Timer { Interval = 600 };
                    t2.Tick += (s2, e2) =>
                    {
                        t2.Stop();
                        using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                            bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                        }
                        form.Close();
                    };
                    t2.Start();
                };
                form.Shown += (s, e) => timer.Start();
                Application.Run(form);
                return 0;
            }
            Application.Run(new MainForm());
            return 0;
        }

        private static int Transfer(bool import, string file, string gameDir)
        {
            var files = new ModFiles(gameDir);
            string problem = files.Missing();
            if (problem != null) { Console.Error.WriteLine("ERROR: " + problem); return 2; }
            var heroes = files.LoadHeroes();
            var byKey = heroes.ToDictionary(h => h.Key);
            if (!import)
            {
                var roster = files.LoadRoster(false);
                var assignment = roster.Where(kv => !string.IsNullOrEmpty(kv.Value) && byKey.ContainsKey(kv.Value)).ToDictionary(kv => kv.Key, kv => byKey[kv.Value]);
                files.ExportRoster(file, assignment);
                Console.WriteLine($"exported {assignment.Count} assigned slots to {file}");
                return 0;
            }
            var map = files.ImportRoster(file, heroes, out var unknown, out int rows);
            int locked = map.Values.Where(k => k.Length > 0 && byKey.ContainsKey(k)).Count(k => byKey[k].Locked);
            Console.WriteLine($"rows     : {rows}");
            Console.WriteLine($"slots    : {map.Count} ({map.Values.Count(v => v.Length > 0)} heroes, {map.Values.Count(v => v.Length == 0)} vanilla)");
            Console.WriteLine($"unknown  : {unknown.Count}{(unknown.Count > 0 ? " - " + string.Join(", ", unknown.Distinct()) : "")}");
            Console.WriteLine($"locked   : {locked}");
            Console.WriteLine("(headless import only validates the file; use the Mod Manager to load and apply it)");
            return 0;
        }

        private static int Headless(bool apply, string gameDir)
        {
            var files = new ModFiles(gameDir);
            string problem = files.Missing();
            if (problem != null) { Console.Error.WriteLine("ERROR: " + problem); return 2; }
            var heroes = files.LoadHeroes();
            var roster = files.LoadRoster(false);
            var byKey = heroes.ToDictionary(h => h.Key);
            var assignment = roster.Where(kv => !string.IsNullOrEmpty(kv.Value) && byKey.ContainsKey(kv.Value))
                                   .ToDictionary(kv => kv.Key, kv => byKey[kv.Value]);
            Console.WriteLine($"game dir : {files.GameDir}");
            Console.WriteLine($"heroes   : {heroes.Count} ({files.CountLocked(heroes)} locked{(ModFiles.UnlockAll ? ", unlock all is on" : ", unlocks read from " + files.ShardSourceInfo)})");
            foreach (var s in files.LoadSaves()) Console.WriteLine($"save     : {s.Label}  -  {s.KeyItems.Count} key items, {s.ReleasedJobs.Count} jobs");
            Console.WriteLine($"roster   : {roster.Count} slots, {assignment.Count} assigned");
            foreach (var it in Installer.Status(files)) Console.WriteLine($"install  : {(it.Present ? "ok     " : "MISSING")} {it.Name} - {it.Detail}");
            if (!apply) return 0;
            Console.WriteLine(files.Apply(assignment));
            return 0;
        }
    }
}
