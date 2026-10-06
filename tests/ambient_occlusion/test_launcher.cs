// Compiled with the production launcher. Uses isolated fixtures; never launches a game.
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace BlackXboxLauncher
{
    internal static class AmbientOcclusionTests
    {
        private static int checks;
        private static void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); checks++; }
        private static Control Find(Control root, string accessibleName)
        {
            if (root.AccessibleName == accessibleName) return root;
            foreach (Control child in root.Controls) { var found = Find(child, accessibleName); if (found != null) return found; }
            return null;
        }
        private static void Select(ComboBox box, string label)
        {
            for (int i = 0; i < box.Items.Count; i++)
                if (box.Items[i].ToString() == label) { box.SelectedIndex = i; return; }
            throw new Exception("Missing English option: " + label);
        }
        private static LaunchOptions Options(LauncherForm form)
        { return (LaunchOptions)typeof(LauncherForm).GetMethod("Options", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null); }
        private static void Invalid(LaunchOptions options, string expected)
        {
            try { options.Validate(); }
            catch (ArgumentException error) { Check(error.Message.Contains(expected), "Expected invalid " + expected); return; }
            throw new Exception("Accepted invalid " + expected);
        }
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Check(args.Length == 1, "Expected an isolated build fixture directory.");
                string fixture = Path.Combine(Path.GetFullPath(args[0]), "fixture-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(fixture);
                var defaults = new LaunchOptions();
                Check(defaults.AoMethod == "off" && defaults.AoQuality == "high", "AO defaults are Off and High.");
                foreach (string method in new[] { "off", "ssao", "hbao", "hbao_plus", "gtao" })
                    foreach (string quality in new[] { "low", "medium", "high", "ultra" })
                    {
                        var chosen = new LaunchOptions { AoMethod = method, AoQuality = quality };
                        chosen.Validate();
                        Check(GameSession.CreateStartInfo(fixture, chosen) != null, "Accepted AO " + method + "/" + quality);
                        var parsed = new LaunchOptions();
                        Check(parsed.ApplyVideoIni(chosen.VideoIni()) == 17 && parsed.AoMethod == method && parsed.AoQuality == quality,
                            "Shared video.ini round trip: " + method + "/" + quality);
                        var restored = LaunchOptions.FromSettingsJson(new JavaScriptSerializer().Serialize(chosen));
                        Check(restored.AoMethod == method && restored.AoQuality == quality, "Launcher JSON preserves AO choices.");
                        Check(chosen.RasterSize()[0] == defaults.RasterSize()[0] && chosen.RasterSize()[1] == defaults.RasterSize()[1],
                            "AO does not change internal resolution or the SSAA raster.");
                    }
                var read = new LaunchOptions();
                foreach (string quality in new[] { "low", "medium", "high", "ultra" })
                    Check(read.ApplyVideoIni("ssao=" + quality + "\n") == 1 && read.AoMethod == "ssao" && read.AoQuality == quality,
                        "Legacy SSAO migration: " + quality);
                Check(read.ApplyVideoIni("ssao=off\n") == 1 && read.AoMethod == "off" && read.AoQuality == "ultra", "Legacy Off retains quality.");
                Check(read.ApplyVideoIni("ssao=low\nao_method=HBAO_PLUS\nao_quality=HIGH\n") == 3 && read.AoMethod == "hbao_plus" && read.AoQuality == "high",
                    "Explicit AO keys override an earlier legacy key case-insensitively.");
                Check(read.ApplyVideoIni("ao_method=invalid\nao_quality=off\nssao=invalid\n") == 0 && read.AoMethod == "hbao_plus" && read.AoQuality == "high",
                    "Malformed AO entries preserve existing choices.");
                Invalid(new LaunchOptions { AoMethod = "invalid" }, "AoMethod");
                Invalid(new LaunchOptions { AoMethod = null }, "AoMethod");
                Invalid(new LaunchOptions { AoQuality = "off" }, "AoQuality");
                Invalid(new LaunchOptions { AoQuality = null }, "AoQuality");
                var old = LaunchOptions.FromSettingsJson("{\"Threads\":7}");
                Check(old.AoMethod == "off" && old.AoQuality == "high" && old.Threads == 7, "Legacy launcher files acquire AO defaults.");
                var repaired = LaunchOptions.FromSettingsJson("{\"AoMethod\":\"gtao\",\"AoQuality\":\"invalid\"}");
                Check(repaired.AoMethod == "gtao" && repaired.AoQuality == "high", "Malformed quality is repaired independently.");
                var culture = Thread.CurrentThread.CurrentCulture;
                try
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                    var chosen = new LaunchOptions { AoMethod = "gtao", AoQuality = "ultra", VerticalFov = 72.5 };
                    var parsed = new LaunchOptions(); parsed.ApplyVideoIni(chosen.VideoIni());
                    Check(parsed.AoMethod == "gtao" && parsed.AoQuality == "ultra" && parsed.VerticalFov == 72.5, "AO and FOV file values are culture-independent.");
                }
                finally { Thread.CurrentThread.CurrentCulture = culture; }
                var process = GameSession.CreateStartInfo(fixture, new LaunchOptions { AoMethod = "gtao", AoQuality = "ultra" });
                Check(process.EnvironmentVariables["RECOMP_VIDEO_INI"] == LaunchOptions.VideoIniPath(fixture), "Launcher uses the shared video.ini.");
                Check(!process.EnvironmentVariables.ContainsKey("RECOMP_AO_METHOD") && !process.EnvironmentVariables.ContainsKey("RECOMP_AO_QUALITY"),
                    "Launcher choices remain editable from the in-game Video Settings page.");

                string project = Path.Combine(fixture, "AO UI");
                Directory.CreateDirectory(Path.Combine(project, "local"));
                using (var form = new LauncherForm(project, true))
                {
                    var method = Find(form, "Ambient occlusion method") as ComboBox;
                    var quality = Find(form, "Ambient occlusion quality") as ComboBox;
                    Check(method != null && quality != null && method.Items.Count == 5 && quality.Items.Count == 4, "All English AO options are exposed.");
                    Check(!quality.Enabled && quality.SelectedItem.ToString() == "High", "Quality is remembered while AO is Off.");
                    foreach (string label in new[] { "SSAO Classic", "HBAO", "HBAO+", "GTAO" })
                    { Select(method, label); Check(quality.Enabled, "Quality is enabled for " + label); }
                    Select(quality, "Ultra");
                    Check(Options(form).AoMethod == "gtao" && Options(form).AoQuality == "ultra", "UI choices reach the production settings model.");
                    File.WriteAllText(LaunchOptions.VideoIniPath(project), "ao_method=hbao_plus\nao_quality=medium\n");
                    typeof(LauncherForm).GetMethod("ReloadVideo", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
                    Check(Options(form).AoMethod == "hbao_plus" && Options(form).AoQuality == "medium" && quality.Enabled, "Reload AO choices saved by the game.");
                    Select(method, "Off");
                    Check(!quality.Enabled && Options(form).AoQuality == "medium", "Turning AO Off preserves quality.");
                    var tip = (ToolTip)typeof(LauncherForm).GetField("settingsTip", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    Check(tip.GetToolTip(quality).Contains("sampling density and radius") && tip.GetToolTip(quality).Contains("scene resolution"),
                        "Quality help describes actual sampling behavior.");
                }
                Console.WriteLine("PASS: " + checks + " actual-launcher AO checks.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        }
    }
}
