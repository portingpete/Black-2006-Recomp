using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace BlackXboxLauncher
{
    internal static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        internal static string FindProject(string directory)
        {
            foreach (string candidate in new[] { directory })
                if (File.Exists(Path.Combine(candidate, "src", "main.c")) && Directory.Exists(Path.Combine(candidate, "scripts")))
                    return Path.GetFullPath(candidate);
            throw new DirectoryNotFoundException("Place BLACK PC Launcher.exe in the Black 2006 Recomp project folder.");
        }

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                string root = FindProject(AppDomain.CurrentDomain.BaseDirectory);
                if (args.Length > 0 && args[0] == "--check")
                {
                    GameSession.Validate(root);
                    Console.WriteLine("PC build and retail BLACK game files verified: " + root);
                    return 0;
                }
                if ((args.Length == 2 || args.Length == 3) && args[0] == "--preview")
                {
                    SetProcessDPIAware();
                    Application.EnableVisualStyles();
                    using (var form = new LauncherForm(root, true))
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        if (args.Length == 3) form.SelectSettingsTab(int.Parse(args[2], CultureInfo.InvariantCulture));
                        form.Show();
                        Application.DoEvents();
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(Path.GetFullPath(args[1]), System.Drawing.Imaging.ImageFormat.Png);
                        form.Close();
                    }
                    return 0;
                }
                if (args.Length == 2 && args[0] == "--preview-bindings")
                {
                    SetProcessDPIAware();
                    Application.EnableVisualStyles();
                    using (var dialog = new BindingsDialog(new LaunchOptions()))
                    using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                    {
                        dialog.Show();
                        Application.DoEvents();
                        dialog.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(Path.GetFullPath(args[1]), System.Drawing.Imaging.ImageFormat.Png);
                        dialog.Close();
                    }
                    return 0;
                }
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool created;
                using (var mutex = new Mutex(true, "Local\\BlackXboxLauncher-" + GameSession.Identity(root), out created))
                {
                    if (!created) return 0;
                    Application.Run(new LauncherForm(root));
                }
                return 0;
            }
            catch (Exception error)
            {
                if (args.Length > 0) Console.Error.WriteLine(error.Message);
                else MessageBox.Show(error.Message, "BLACK PC launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    internal sealed class LaunchOptions
    {
        public int SettingsVersion = 7;
        // GpuRenderer draws the game's NV2A graphics on the GPU (Direct3D 11) and presents it directly;
        // off keeps the CPU Kelvin renderer. Smooth60 runs the game's clock at 60 steps per second so
        // 60 presented frames are 60 simulated frames (the original runs at 30).
        public bool GpuRenderer = true;
        public bool Smooth60 = true;
        // With Smooth60 on: 0 runs the clock at 60 steps per second; 120 or 240 runs it at that rate (RECOMP_BLACK_HZ).
        public int SmoothHz;
        public int Threads = Math.Max(1, Math.Min(16, Environment.ProcessorCount / 2));
        public bool SkipMovies;
        public bool AdvanceMenus;
        // The window's client size. The picture the game renders is a separate setting, InternalHeight: a number of
        // lines (480p..2160p) whose width follows the camera aspect, scaled to the window.
        public int OutputWidth = 1280;
        public int OutputHeight = 960;
        public int InternalHeight = 720;
        // Picture effects the GPU renderer does itself. AntiAliasing: off, fxaa (filters the finished scene, not the HUD) or
        // ssaa (renders at twice the width and height and averages down). Anisotropy is the filtering level, 1 being off.
        // DepthOfField and Sharpening are off, low, medium or high.
        public string AntiAliasing = "fxaa";
        public int Anisotropy = 16;
        public string DepthOfField = "off";
        public string Sharpening = "off";
        public string WindowMode = "windowed";
        public string ScaleMode = "fit";
        public string PresentFilter = "linear";
        public bool VSync = true;
        public int FpsLimit;
        public string CameraAspect = "original";
        // Zero preserves the game's original camera and zoom. Custom values are
        // the vertical field of view in degrees, independent of aspect ratio.
        public double VerticalFov;
        public string MotionBlur = "original";
        // Native keyboard and mouse (pc_input.c reads these as RECOMP_BLACK_*). Bindings holds
        // only the actions the player changed: action name -> "Key,Key" (up to three, "None" for
        // unbound); an absent action keeps InputBindings.Actions' default.
        public string InputMode = "auto";
        public string PromptMode = "auto";
        public double MouseSensitivity = 1.0;
        public bool InvertY;
        public Dictionary<string, string> Bindings = new Dictionary<string, string>();

        // Camera and original motion-blur controls use private guest hooks,
        // independently of output scaling and presentation pacing.
        internal static readonly bool CameraAspectAvailable = true;
        internal static readonly bool MotionBlurAvailable = true;
        internal static readonly int[] FrameLimits = { 0, 30, 60, 120, 144, 165, 240 };
        internal static readonly int[] InternalHeights = { 480, 720, 1080, 1440, 2160 };
        internal static readonly int[] AnisotropyLevels = { 1, 2, 4, 8, 16 };
        internal const double FovMin = 35.0, FovMax = 100.0;
        internal const long RenderPixelLimit = 16777216L;

        internal static bool IsFrameLimit(int value) { return Array.IndexOf(FrameLimits, value) >= 0; }
        internal static bool IsInternalHeight(int value) { return Array.IndexOf(InternalHeights, value) >= 0; }
        internal static bool IsAnisotropy(int value) { return Array.IndexOf(AnisotropyLevels, value) >= 0; }
        internal static bool IsFov(double value) { return value == 0.0 || (!double.IsNaN(value) && !double.IsInfinity(value) && value >= FovMin && value <= FovMax); }

        // The internal resolution for these options (pc_video.c computes the same): the internal height at the camera's
        // aspect ratio, with the game's own 4:3 camera for Original, rounded up to an even width. RasterSize is what the GPU
        // renderer draws at: the same, or twice the width and height under SSAA when that stays within its limits.
        internal int[] RasterSize()
        {
            int[] size = RenderSize();
            if (AntiAliasing == "ssaa" && size[0] * 2 <= 8192 && size[1] * 2 <= 8192 && (long)size[0] * 2 * size[1] * 2 <= RenderPixelLimit)
                return new[] { size[0] * 2, size[1] * 2 };
            return size;
        }

        internal int[] RenderSize()
        {
            int num = 4, den = 3;
            if (CameraAspect == "16:9") { num = 16; den = 9; }
            else if (CameraAspect == "21:9") { num = 21; den = 9; }
            else if (CameraAspect == "32:9") { num = 32; den = 9; }
            int width = (int)(((long)InternalHeight * num + den / 2) / den);
            width = (width + 1) & ~1;
            return new[] { Math.Max(width, 640), Math.Max(InternalHeight, 480) };
        }

        // The video settings live in one file the game and the launcher share (local\video.ini). The game's Video
        // Settings page edits it too, so the launcher shows what the game last saved.
        internal static string VideoIniPath(string root) { return Path.Combine(Path.GetFullPath(root), "local", "video.ini"); }

        internal string VideoIni()
        {
            var text = new StringBuilder();
            text.Append("; BLACK video settings. Edited by the in-game Video Settings page and by the launcher.\n[video]\n");
            text.Append("internal_height=").Append(InternalHeight.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("window_width=").Append(OutputWidth.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("window_height=").Append(OutputHeight.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("window_mode=").Append(WindowMode).Append('\n');
            text.Append("scale=").Append(ScaleMode).Append('\n');
            text.Append("filter=").Append(PresentFilter).Append('\n');
            text.Append("vsync=").Append(VSync ? "1" : "0").Append('\n');
            text.Append("fps_limit=").Append(FpsLimit.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("aspect=").Append(CameraAspect).Append('\n');
            text.Append("fov=").Append(VerticalFov.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            text.Append("motion_blur=").Append(MotionBlur).Append('\n');
            text.Append("aa=").Append(AntiAliasing).Append('\n');
            text.Append("af=").Append(Anisotropy.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("dof=").Append(DepthOfField).Append('\n');
            text.Append("sharpen=").Append(Sharpening).Append('\n');
            return text.ToString();
        }

        // Applies the keys of a settings file over these options with the game's own rules (pc_video.c): an unknown
        // key or an invalid value is skipped and the rest still apply. Returns how many keys applied.
        internal int ApplyVideoIni(string text)
        {
            int applied = 0;
            if (text == null) return 0;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant(), value = line.Substring(eq + 1).Trim();
                int n; double d;
                switch (key)
                {
                    case "internal_height":
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n) && IsInternalHeight(n)) { InternalHeight = n; applied++; }
                        break;
                    case "window_width":
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= 320 && n <= 7680) { OutputWidth = n; applied++; }
                        break;
                    case "window_height":
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= 240 && n <= 4320) { OutputHeight = n; applied++; }
                        break;
                    case "window_mode":
                        if (IsOneOf(value.ToLowerInvariant(), "windowed", "borderless")) { WindowMode = value.ToLowerInvariant(); applied++; }
                        break;
                    case "scale":
                        if (IsOneOf(value.ToLowerInvariant(), "fit", "stretch", "integer")) { ScaleMode = value.ToLowerInvariant(); applied++; }
                        break;
                    case "filter":
                        if (IsOneOf(value.ToLowerInvariant(), "nearest", "linear")) { PresentFilter = value.ToLowerInvariant(); applied++; }
                        break;
                    case "vsync":
                        if (value == "0" || value == "1") { VSync = value == "1"; applied++; }
                        break;
                    case "fps_limit":
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n) && IsFrameLimit(n)) { FpsLimit = n; applied++; }
                        break;
                    case "aspect":
                        if (IsOneOf(value.ToLowerInvariant(), "original", "4:3", "16:9", "21:9", "32:9")) { CameraAspect = value.ToLowerInvariant(); applied++; }
                        break;
                    case "fov":
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && IsFov(d)) { VerticalFov = d; applied++; }
                        break;
                    case "motion_blur":
                        if (IsOneOf(value.ToLowerInvariant(), "original", "off")) { MotionBlur = value.ToLowerInvariant(); applied++; }
                        break;
                    case "aa":
                        if (IsOneOf(value.ToLowerInvariant(), "off", "fxaa", "ssaa")) { AntiAliasing = value.ToLowerInvariant(); applied++; }
                        break;
                    case "af":
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n) && IsAnisotropy(n)) { Anisotropy = n; applied++; }
                        break;
                    case "dof":
                        if (IsOneOf(value.ToLowerInvariant(), "off", "low", "medium", "high")) { DepthOfField = value.ToLowerInvariant(); applied++; }
                        break;
                    case "sharpen":
                        if (IsOneOf(value.ToLowerInvariant(), "off", "low", "medium", "high")) { Sharpening = value.ToLowerInvariant(); applied++; }
                        break;
                }
            }
            return applied;
        }
        private static bool IsOneOf(string value, params string[] choices) { return Array.IndexOf(choices, value) >= 0; }

        internal void Validate()
        {
            if (Threads < 1 || Threads > 64) throw new ArgumentOutOfRangeException("Threads");
            if (SmoothHz != 0 && SmoothHz != 120 && SmoothHz != 240) throw new ArgumentOutOfRangeException("SmoothHz");
            if (OutputWidth < 320 || OutputWidth > 7680) throw new ArgumentOutOfRangeException("OutputWidth");
            if (OutputHeight < 240 || OutputHeight > 4320) throw new ArgumentOutOfRangeException("OutputHeight");
            if (!IsInternalHeight(InternalHeight)) throw new ArgumentOutOfRangeException("InternalHeight");
            if (!IsOneOf(AntiAliasing, "off", "fxaa", "ssaa")) throw new ArgumentException("Invalid AntiAliasing.");
            if (!IsAnisotropy(Anisotropy)) throw new ArgumentOutOfRangeException("Anisotropy");
            if (!IsOneOf(DepthOfField, "off", "low", "medium", "high")) throw new ArgumentException("Invalid DepthOfField.");
            if (!IsOneOf(Sharpening, "off", "low", "medium", "high")) throw new ArgumentException("Invalid Sharpening.");
            if (!IsOneOf(WindowMode, "windowed", "borderless")) throw new ArgumentException("Invalid WindowMode.");
            if (!IsOneOf(ScaleMode, "fit", "stretch", "integer")) throw new ArgumentException("Invalid ScaleMode.");
            if (!IsOneOf(PresentFilter, "nearest", "linear")) throw new ArgumentException("Invalid PresentFilter.");
            if (!IsFrameLimit(FpsLimit)) throw new ArgumentOutOfRangeException("FpsLimit");
            if (!IsOneOf(CameraAspect, "original", "4:3", "16:9", "21:9", "32:9")) throw new ArgumentException("Invalid CameraAspect.");
            if (!CameraAspectAvailable && CameraAspect != "original") throw new InvalidOperationException("Camera aspect changes are not supported by this build.");
            if (!IsFov(VerticalFov)) throw new ArgumentOutOfRangeException("VerticalFov");
            if (!IsOneOf(MotionBlur, "original", "off")) throw new ArgumentException("Invalid MotionBlur.");
            if (!MotionBlurAvailable && MotionBlur != "original") throw new InvalidOperationException("Motion blur controls are not supported by this build.");
            if (!IsOneOf(InputMode, "auto", "keyboard_mouse", "controller")) throw new ArgumentException("Invalid InputMode.");
            if (!IsOneOf(PromptMode, "auto", "keyboard_mouse", "controller")) throw new ArgumentException("Invalid PromptMode.");
            if (double.IsNaN(MouseSensitivity) || double.IsInfinity(MouseSensitivity) ||
                MouseSensitivity < InputBindings.SensitivityMin || MouseSensitivity > InputBindings.SensitivityMax)
                throw new ArgumentOutOfRangeException("MouseSensitivity");
            if (Bindings != null)
                foreach (KeyValuePair<string, string> binding in Bindings)
                {
                    if (InputBindings.Find(binding.Key) == null) throw new ArgumentException("Invalid binding: unknown action " + binding.Key + ".");
                    string problem = InputBindings.Problem(binding.Value);
                    if (problem != null) throw new ArgumentException("Invalid binding for " + binding.Key + ": " + problem);
                }
        }

        internal static LaunchOptions FromSettingsJson(string json)
        {
            var serializer = new JavaScriptSerializer();
            var fields = serializer.Deserialize<Dictionary<string, object>>(json);
            object oldBlur;
            // Previous previews persisted a disabled boolean control. Either
            // boolean retains the actual original game effect on migration.
            if (fields != null && fields.TryGetValue("MotionBlur", out oldBlur) && !(oldBlur is string))
                fields.Remove("MotionBlur");
            object rawFov;
            if (fields != null && fields.TryGetValue("VerticalFov", out rawFov) &&
                !(rawFov is int || rawFov is decimal || rawFov is double)) fields.Remove("VerticalFov");
            if (fields != null) InputBindings.SanitizeFields(fields);
            var settings = fields == null ? new LaunchOptions() : serializer.Deserialize<LaunchOptions>(serializer.Serialize(fields));
            var defaults = new LaunchOptions();
            // Old files have only the original three fields; initializers give
            // them the new defaults. Repair bad new fields independently.
            settings.SettingsVersion = defaults.SettingsVersion;
            if (settings.Threads < 1 || settings.Threads > 64) settings.Threads = defaults.Threads;
            if (settings.SmoothHz != 0 && settings.SmoothHz != 120 && settings.SmoothHz != 240) settings.SmoothHz = defaults.SmoothHz;
            if (settings.OutputWidth < 320 || settings.OutputWidth > 7680) settings.OutputWidth = defaults.OutputWidth;
            if (settings.OutputHeight < 240 || settings.OutputHeight > 4320) settings.OutputHeight = defaults.OutputHeight;
            if (!IsInternalHeight(settings.InternalHeight)) settings.InternalHeight = defaults.InternalHeight;
            if (!IsOneOf(settings.AntiAliasing, "off", "fxaa", "ssaa")) settings.AntiAliasing = defaults.AntiAliasing;
            if (!IsAnisotropy(settings.Anisotropy)) settings.Anisotropy = defaults.Anisotropy;
            if (!IsOneOf(settings.DepthOfField, "off", "low", "medium", "high")) settings.DepthOfField = defaults.DepthOfField;
            if (!IsOneOf(settings.Sharpening, "off", "low", "medium", "high")) settings.Sharpening = defaults.Sharpening;
            if (!IsOneOf(settings.WindowMode, "windowed", "borderless")) settings.WindowMode = defaults.WindowMode;
            if (!IsOneOf(settings.ScaleMode, "fit", "stretch", "integer")) settings.ScaleMode = defaults.ScaleMode;
            if (!IsOneOf(settings.PresentFilter, "nearest", "linear")) settings.PresentFilter = defaults.PresentFilter;
            if (!IsFrameLimit(settings.FpsLimit)) settings.FpsLimit = defaults.FpsLimit;
            if (!CameraAspectAvailable || !IsOneOf(settings.CameraAspect, "original", "4:3", "16:9", "21:9", "32:9")) settings.CameraAspect = defaults.CameraAspect;
            if (!IsFov(settings.VerticalFov)) settings.VerticalFov = defaults.VerticalFov;
            if (!MotionBlurAvailable || !IsOneOf(settings.MotionBlur, "original", "off")) settings.MotionBlur = defaults.MotionBlur;
            if (!IsOneOf(settings.InputMode, "auto", "keyboard_mouse", "controller")) settings.InputMode = defaults.InputMode;
            if (!IsOneOf(settings.PromptMode, "auto", "keyboard_mouse", "controller")) settings.PromptMode = defaults.PromptMode;
            if (double.IsNaN(settings.MouseSensitivity) || double.IsInfinity(settings.MouseSensitivity) ||
                settings.MouseSensitivity < InputBindings.SensitivityMin || settings.MouseSensitivity > InputBindings.SensitivityMax)
                settings.MouseSensitivity = defaults.MouseSensitivity;
            settings.Bindings = InputBindings.Repair(settings.Bindings);
            return settings;
        }
    }

    // The keyboard/mouse binding model: the same action names and key names pc_input.c parses
    // (xboxrecomp/src/input/pc_input.c; a test compares the two tables). Keys are named by their
    // QWERTY position and stored as physical scancodes by the game, so a binding means the same
    // key position on every layout. Escape (pause / back) and F11 (window mode) are the host's.
    internal static class InputBindings
    {
        internal const double SensitivityMin = 0.1, SensitivityMax = 10.0;
        internal const int MaxPerAction = 3;

        internal sealed class InputAction
        {
            internal readonly string Name, Label, Default;
            internal InputAction(string name, string label, string defaultBinding) { Name = name; Label = label; Default = defaultBinding; }
        }
        internal sealed class KeyDef
        {
            internal readonly string Token, Friendly; internal readonly int Scancode;   // scancode | 0x100 when extended; -1 for the mouse
            internal KeyDef(string token, int scancode, string friendly) { Token = token; Scancode = scancode; Friendly = friendly; }
        }

        internal static readonly InputAction[] Actions =
        {
            new InputAction("MOVE_FORWARD", "Move forward", "W"), new InputAction("MOVE_BACK", "Move back", "S"),
            new InputAction("MOVE_LEFT", "Move left", "A"), new InputAction("MOVE_RIGHT", "Move right", "D"),
            new InputAction("LOOK_LEFT", "Look left (keys)", "None"), new InputAction("LOOK_RIGHT", "Look right (keys)", "None"),
            new InputAction("LOOK_UP", "Look up (keys)", "None"), new InputAction("LOOK_DOWN", "Look down (keys)", "None"),
            new InputAction("FIRE", "Fire", "Mouse1"), new InputAction("AIM", "Aim / zoom", "Mouse2"),
            new InputAction("RELOAD", "Reload", "R"), new InputAction("CROUCH", "Crouch", "LCtrl,C"),
            new InputAction("USE", "Use / pick up", "E"), new InputAction("NEXT_WEAPON", "Switch weapon", "Q,WheelUp"),
            new InputAction("PREV_WEAPON", "Switch weapon (other way)", "WheelDown"), new InputAction("GRENADE", "Throw grenade", "G"),
            new InputAction("FIRE_MODE", "Fire mode", "B"), new InputAction("SUPPRESSOR", "Suppressor", "F"),
            new InputAction("MELEE", "Melee attack", "V,Mouse3"), new InputAction("HEALTH_PACK", "Use health pack", "H"),
            new InputAction("CYCLE_ITEM", "Cycle item (White)", "X"), new InputAction("BACK", "Back / objectives", "Tab"),
            new InputAction("PAUSE", "Start (pause)", "P")
        };

        private static KeyDef[] BuildKeys()
        {
            var keys = new List<KeyDef>();
            string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            int[] letterCodes = { 0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32, 0x31, 0x18, 0x19, 0x10, 0x13, 0x1F, 0x14, 0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C };
            for (int i = 0; i < 26; i++) keys.Add(new KeyDef(letters[i].ToString(), letterCodes[i], letters[i].ToString()));
            for (int i = 1; i <= 10; i++) { string digit = (i % 10).ToString(CultureInfo.InvariantCulture); keys.Add(new KeyDef(digit, 0x01 + i, digit)); }
            for (int i = 1; i <= 10; i++) keys.Add(new KeyDef("F" + i.ToString(CultureInfo.InvariantCulture), 0x3A + i, "F" + i.ToString(CultureInfo.InvariantCulture)));
            keys.Add(new KeyDef("F12", 0x58, "F12"));
            keys.Add(new KeyDef("Space", 0x39, "Space")); keys.Add(new KeyDef("Enter", 0x1C, "Enter")); keys.Add(new KeyDef("Tab", 0x0F, "Tab"));
            keys.Add(new KeyDef("Backspace", 0x0E, "Backspace")); keys.Add(new KeyDef("CapsLock", 0x3A, "Caps Lock"));
            keys.Add(new KeyDef("Up", 0x148, "Up arrow")); keys.Add(new KeyDef("Down", 0x150, "Down arrow"));
            keys.Add(new KeyDef("Left", 0x14B, "Left arrow")); keys.Add(new KeyDef("Right", 0x14D, "Right arrow"));
            keys.Add(new KeyDef("Insert", 0x152, "Insert")); keys.Add(new KeyDef("Delete", 0x153, "Delete"));
            keys.Add(new KeyDef("Home", 0x147, "Home")); keys.Add(new KeyDef("End", 0x14F, "End"));
            keys.Add(new KeyDef("PageUp", 0x149, "Page Up")); keys.Add(new KeyDef("PageDown", 0x151, "Page Down"));
            keys.Add(new KeyDef("Shift", -2, "Shift (either)")); keys.Add(new KeyDef("LShift", 0x2A, "Left Shift")); keys.Add(new KeyDef("RShift", 0x36, "Right Shift"));
            keys.Add(new KeyDef("Ctrl", -2, "Ctrl (either)")); keys.Add(new KeyDef("LCtrl", 0x1D, "Left Ctrl")); keys.Add(new KeyDef("RCtrl", 0x11D, "Right Ctrl"));
            keys.Add(new KeyDef("Alt", -2, "Alt (either)")); keys.Add(new KeyDef("LAlt", 0x38, "Left Alt")); keys.Add(new KeyDef("RAlt", 0x138, "Right Alt"));
            int[] pad = { 0x52, 0x4F, 0x50, 0x51, 0x4B, 0x4C, 0x4D, 0x47, 0x48, 0x49 };
            for (int i = 0; i < 10; i++) keys.Add(new KeyDef("Numpad" + i.ToString(CultureInfo.InvariantCulture), pad[i], "Numpad " + i.ToString(CultureInfo.InvariantCulture)));
            keys.Add(new KeyDef("NumpadEnter", 0x11C, "Numpad Enter")); keys.Add(new KeyDef("NumpadPlus", 0x4E, "Numpad +"));
            keys.Add(new KeyDef("NumpadMinus", 0x4A, "Numpad -")); keys.Add(new KeyDef("NumpadMultiply", 0x37, "Numpad *"));
            keys.Add(new KeyDef("NumpadDivide", 0x135, "Numpad /")); keys.Add(new KeyDef("NumpadDecimal", 0x53, "Numpad ."));
            keys.Add(new KeyDef("Minus", 0x0C, "-")); keys.Add(new KeyDef("Equals", 0x0D, "=")); keys.Add(new KeyDef("LBracket", 0x1A, "["));
            keys.Add(new KeyDef("RBracket", 0x1B, "]")); keys.Add(new KeyDef("Semicolon", 0x27, ";")); keys.Add(new KeyDef("Quote", 0x28, "'"));
            keys.Add(new KeyDef("Grave", 0x29, "`"));
            keys.Add(new KeyDef("Comma", 0x33, ",")); keys.Add(new KeyDef("Period", 0x34, ".")); keys.Add(new KeyDef("Slash", 0x35, "/"));
            keys.Add(new KeyDef("Backslash", 0x2B, "\\"));
            keys.Add(new KeyDef("Mouse1", -1, "Left mouse button")); keys.Add(new KeyDef("Mouse2", -1, "Right mouse button"));
            keys.Add(new KeyDef("Mouse3", -1, "Middle mouse button")); keys.Add(new KeyDef("Mouse4", -1, "Mouse button 4"));
            keys.Add(new KeyDef("Mouse5", -1, "Mouse button 5"));
            keys.Add(new KeyDef("WheelUp", -1, "Mouse wheel up")); keys.Add(new KeyDef("WheelDown", -1, "Mouse wheel down"));
            return keys.ToArray();
        }
        internal static readonly KeyDef[] Keys = BuildKeys();

        internal static InputAction Find(string name)
        {
            if (name == null) return null;
            foreach (InputAction action in Actions) if (string.Equals(action.Name, name, StringComparison.OrdinalIgnoreCase)) return action;
            return null;
        }
        internal static KeyDef FindKey(string token)
        {
            if (token == null) return null;
            foreach (KeyDef key in Keys) if (string.Equals(key.Token, token, StringComparison.OrdinalIgnoreCase)) return key;
            return null;
        }
        internal static KeyDef KeyForScancode(int scancode)
        {
            foreach (KeyDef key in Keys) if (key.Scancode == scancode) return key;
            return null;
        }
        internal static bool IsReserved(string token)
        {
            return string.Equals(token, "Esc", StringComparison.OrdinalIgnoreCase) || string.Equals(token, "Escape", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "F11", StringComparison.OrdinalIgnoreCase);
        }

        // Null if every token is a bindable key; otherwise why not.
        internal static string Problem(string value)
        {
            if (value == null) return "no value";
            var seen = new List<string>();
            foreach (string raw in value.Split(','))
            {
                string token = raw.Trim();
                if (token.Length == 0 || token.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsReserved(token)) return token + " is reserved (Escape pauses, F11 toggles the window)";
                KeyDef key = FindKey(token);
                if (key == null) return "unknown key " + token;
                if (seen.Contains(key.Token)) return "duplicate key " + key.Token;
                seen.Add(key.Token);
            }
            if (seen.Count > MaxPerAction) return "at most " + MaxPerAction + " keys per action";
            return null;
        }

        // The valid, canonical-case tokens of a binding string, in order, without duplicates.
        internal static List<string> Tokens(string value)
        {
            var result = new List<string>();
            if (value == null) return result;
            foreach (string raw in value.Split(','))
            {
                KeyDef key = IsReserved(raw.Trim()) ? null : FindKey(raw.Trim());
                if (key != null && !result.Contains(key.Token) && result.Count < MaxPerAction) result.Add(key.Token);
            }
            return result;
        }
        internal static string Join(IEnumerable<string> tokens)
        {
            string joined = string.Join(",", new List<string>(tokens).ToArray());
            return joined.Length == 0 ? "None" : joined;
        }

        internal static string Effective(LaunchOptions options, InputAction action)
        {
            string value;
            if (options.Bindings != null)
                foreach (KeyValuePair<string, string> pair in options.Bindings)
                    if (string.Equals(pair.Key, action.Name, StringComparison.OrdinalIgnoreCase)) { value = pair.Value; return Join(Tokens(value)); }
            return Join(Tokens(action.Default));
        }

        // Drop unknown actions, reserved/unknown/duplicate keys and overflow; an entry equal to the
        // default is not kept. A binding the player cleared entirely stays as "None".
        internal static Dictionary<string, string> Repair(Dictionary<string, string> bindings)
        {
            var repaired = new Dictionary<string, string>();
            if (bindings == null) return repaired;
            foreach (KeyValuePair<string, string> pair in bindings)
            {
                InputAction action = Find(pair.Key);
                if (action == null || pair.Value == null) continue;
                string joined = Join(Tokens(pair.Value));
                bool intentionallyNone = pair.Value.Trim().Length == 0 || pair.Value.Trim().Equals("None", StringComparison.OrdinalIgnoreCase);
                if (joined == "None" && !intentionallyNone) continue;        // nothing in it was usable: keep the default
                if (joined == Join(Tokens(action.Default))) continue;
                repaired[action.Name] = joined;
            }
            return repaired;
        }

        // JavaScriptSerializer rejects a wrongly typed field outright; remove each bad new field
        // instead so the rest of the file survives.
        internal static void SanitizeFields(Dictionary<string, object> fields)
        {
            foreach (string name in new[] { "InputMode", "PromptMode" })
                if (fields.ContainsKey(name) && !(fields[name] is string)) fields.Remove(name);
            if (fields.ContainsKey("MouseSensitivity") && !(fields["MouseSensitivity"] is int || fields["MouseSensitivity"] is decimal || fields["MouseSensitivity"] is double))
                fields.Remove("MouseSensitivity");
            if (fields.ContainsKey("InvertY") && !(fields["InvertY"] is bool)) fields.Remove("InvertY");
            object raw;
            if (fields.TryGetValue("Bindings", out raw))
            {
                var clean = new Dictionary<string, object>();
                var map = raw as Dictionary<string, object>;
                if (map != null)
                    foreach (KeyValuePair<string, object> pair in map)
                        if (pair.Value is string) clean[pair.Key] = pair.Value;
                fields["Bindings"] = clean;
            }
        }

        // What a person reads for one binding string ("Left Ctrl or C").
        internal static string Describe(string value)
        {
            var names = new List<string>();
            foreach (string token in Tokens(value)) names.Add(FindKey(token).Friendly);
            return names.Count == 0 ? "unbound" : string.Join(" or ", names.ToArray());
        }

        // The Controls help, generated from the same bindings the game is launched with.
        internal static string Help(LaunchOptions options)
        {
            var text = new StringBuilder();
            if (options.InputMode != "controller")
            {
                text.AppendLine("KEYBOARD AND MOUSE (no controller needed)");
                text.AppendLine();
                text.AppendLine("Look: move the mouse (raw input, no acceleration).");
                foreach (InputAction action in Actions)
                    text.AppendLine(action.Label + ": " + Describe(Effective(options, action)));
                text.AppendLine();
                text.AppendLine("Menus: arrow keys or the mouse wheel to move, Enter / Space / left click to select,");
                text.AppendLine("Backspace / right click / Escape to go back. Enter also confirms \"to continue\" prompts.");
                text.AppendLine("Escape pauses a mission.");
                text.AppendLine();
            }
            if (options.InputMode != "keyboard_mouse")
            {
                text.AppendLine(options.InputMode == "controller" ? "CONTROLLER ONLY" : "CONTROLLER (optional)");
                text.AppendLine("Any XInput controller works as before.");
                if (options.InputMode == "controller") text.AppendLine("Keyboard and mouse gameplay and menu controls are disabled.");
            }
            else text.AppendLine("Controller input is disabled.");
            text.AppendLine();
            string prompts = options.PromptMode == "auto" ? options.InputMode : options.PromptMode;
            text.AppendLine(prompts == "auto" ? "Prompts follow whichever device you used last." :
                            prompts == "controller" ? "Prompts show controller buttons." : "Prompts show keyboard and mouse bindings.");
            text.AppendLine("Leaving the game window pauses a mission.");
            text.AppendLine("F11 toggles fullscreen. Movies: Escape skips the current one.");
            return text.ToString();
        }
    }

    internal sealed class GameSession : IDisposable
    {
        internal const string RetailHash = "DF2739C372D254A90AEECCF5971D12097F22D89FBB90DB021FDA96DD4DEB68F6";
        private Process process;
        private StreamWriter log;
        private Mutex gameLock;
        private readonly object logGate = new object();
        internal string DirectoryPath { get; private set; }
        internal string LogFailure { get; private set; }
        internal int ExitCode { get { return process.ExitCode; } }

        internal static string Executable(string root)
        {
            return Path.Combine(root, "build-vs2022", "Release", "black_xbox_recomp.exe");
        }

        internal static string Identity(string root)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd('\\', '/').ToUpperInvariant()))).Replace("-", "");
        }

        internal static void Validate(string root)
        {
            if (!File.Exists(Executable(root)))
                throw new FileNotFoundException("The PC Release build is missing. Run scripts\\setup.ps1 to build it.", Executable(root));
            string xbe = Path.Combine(root, "game", "default.xbe");
            if (!File.Exists(xbe)) throw new FileNotFoundException("The original BLACK game executable is missing: game\\default.xbe.", xbe);
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(xbe))
                if (!BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").Equals(RetailHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("default.xbe does not match the retail BLACK release used by this PC recompilation.");
            if (!File.Exists(Path.Combine(root, "game", "GlobData.bin")) ||
                !Directory.Exists(Path.Combine(root, "game", "levels")) ||
                !Directory.Exists(Path.Combine(root, "game", "sound")) ||
                !Directory.Exists(Path.Combine(root, "game", "videos")))
                throw new DirectoryNotFoundException("Game assets are incomplete. Restore the extracted BLACK disc files under game\\.");
        }

        internal static int ExistingProcess(string root)
        {
            foreach (var candidate in Process.GetProcessesByName("black_xbox_recomp"))
            using (candidate)
            {
                try
                {
                    if (!candidate.HasExited && string.Equals(candidate.MainModule.FileName, Executable(root), StringComparison.OrdinalIgnoreCase))
                        return candidate.Id;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { return candidate.Id; }
            }
            return 0;
        }

        // Remove inherited diagnostics, scripted input and
        // watchdogs before installing the play profile; do not mutate the shell.
        // Rasters of at least this many pixels (3440x1440 is 4.95 million) get the queue-assistance profile.
        internal const long HeavyRasterPixels = 4000000L;

        internal static ProcessStartInfo CreateStartInfo(string root, LaunchOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");
            options.Validate();
            var info = new ProcessStartInfo
            {
                FileName = Executable(root), WorkingDirectory = Path.GetFullPath(root),
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            var remove = new List<string>();
            foreach (DictionaryEntry entry in info.EnvironmentVariables)
                if (((string)entry.Key).StartsWith("RECOMP_", StringComparison.OrdinalIgnoreCase)) remove.Add((string)entry.Key);
            foreach (string key in remove) info.EnvironmentVariables.Remove(key);
            info.EnvironmentVariables["RECOMP_AC97_READY"] = "1";
            info.EnvironmentVariables["RECOMP_APU_DSP_ACK"] = "0x83218810";
            info.EnvironmentVariables["RECOMP_NV2A_KELVIN"] = "1";
            info.EnvironmentVariables["RECOMP_VBLANK"] = "1";
            info.EnvironmentVariables["RECOMP_USB"] = "1";
            info.EnvironmentVariables["RECOMP_FB_WINDOW"] = "1";
            info.EnvironmentVariables["RECOMP_KELVIN_THREADS"] = options.Threads.ToString(CultureInfo.InvariantCulture);
            if (options.GpuRenderer)
            {
                info.EnvironmentVariables["RECOMP_KELVIN_GPU"] = "1";
                // Compiled vertex programs persist here, so a program is translated once per install.
                info.EnvironmentVariables["RECOMP_KGPU_CACHE"] = Path.Combine(Path.GetFullPath(root), "local", "kgpu-cache");
            }
            if (options.Smooth60 && options.SmoothHz != 0) info.EnvironmentVariables["RECOMP_BLACK_HZ"] = options.SmoothHz.ToString(CultureInfo.InvariantCulture);
            else if (options.Smooth60) info.EnvironmentVariables["RECOMP_BLACK_60HZ"] = "1";
            // The video settings (window, internal resolution, camera, presenter) are not exported: the game reads
            // them from the settings file, which its own Video Settings page edits too, and a variable would pin
            // a value there. GameSession.Start writes the file from these options just before the game starts.
            info.EnvironmentVariables["RECOMP_VIDEO_INI"] = LaunchOptions.VideoIniPath(root);
            // The measured heavy-raster profile lets the submitter help at queue
            // capacity and reduces idle spinning. Raster quality and timing
            // semantics are unchanged; lighter rasters keep their profile.
            int[] raster = options.RasterSize();
            if ((long)raster[0] * raster[1] >= HeavyRasterPixels)
            {
                info.EnvironmentVariables["RECOMP_KELVIN_ASSIST_QUEUE"] = "1";
                info.EnvironmentVariables["RECOMP_KELVIN_SPIN_US"] = "250";
            }
            info.EnvironmentVariables["RECOMP_BLACK_INPUT_MODE"] = options.InputMode;
            info.EnvironmentVariables["RECOMP_BLACK_PROMPT_MODE"] = options.PromptMode;
            info.EnvironmentVariables["RECOMP_BLACK_MOUSE_SENSITIVITY"] = options.MouseSensitivity.ToString("R", CultureInfo.InvariantCulture);
            info.EnvironmentVariables["RECOMP_BLACK_MOUSE_INVERT_Y"] = options.InvertY ? "1" : "0";
            foreach (InputBindings.InputAction action in InputBindings.Actions)
                info.EnvironmentVariables["RECOMP_BLACK_BIND_" + action.Name] = InputBindings.Effective(options, action);
            if (options.SkipMovies) info.EnvironmentVariables["RECOMP_BLACK_SKIP_MOVIES"] = "1";
            if (options.AdvanceMenus)
            {
                var presses = new List<string>();
                for (int seconds = 20; seconds <= 65; seconds += 3) presses.Add((seconds * 1000).ToString(CultureInfo.InvariantCulture) + ":A/600");
                info.EnvironmentVariables["RECOMP_PAD_SCRIPT"] = string.Join(",", presses.ToArray());
            }
            return info;
        }

        internal static GameSession Start(string root, LaunchOptions options)
        {
            root = Path.GetFullPath(root);
            Validate(root);
            var session = new GameSession();
            try
            {
                session.gameLock = new Mutex(false, "Local\\BlackXboxGame-" + Identity(root));
                bool acquired;
                try { acquired = session.gameLock.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    session.gameLock.Dispose(); session.gameLock = null;
                    throw new InvalidOperationException("This PC build is already running from another launcher.");
                }
                int existing = ExistingProcess(root);
                if (existing != 0) throw new InvalidOperationException("The PC game is already running (PID " + existing + "). Close that instance before starting another.");
                Directory.CreateDirectory(Path.Combine(root, "save"));
                session.DirectoryPath = Path.Combine(root, "reports", "launcher-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(session.DirectoryPath);
                session.log = new StreamWriter(Path.Combine(session.DirectoryPath, "session.log"), false, new UTF8Encoding(false));
                session.log.AutoFlush = true;
                var info = CreateStartInfo(root, options);
                Directory.CreateDirectory(Path.Combine(root, "local"));
                File.WriteAllText(LaunchOptions.VideoIniPath(root), options.VideoIni(), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(session.DirectoryPath, "video.ini"), options.VideoIni(), new UTF8Encoding(false));
                var environment = new Dictionary<string, string>();
                foreach (DictionaryEntry entry in info.EnvironmentVariables)
                    if (((string)entry.Key).StartsWith("RECOMP_", StringComparison.OrdinalIgnoreCase)) environment[(string)entry.Key] = (string)entry.Value;
                var metadata = new Dictionary<string, object>();
                metadata["startedUtc"] = DateTime.UtcNow.ToString("o");
                metadata["executable"] = info.FileName;
                using (var hash = SHA256.Create())
                using (var stream = File.OpenRead(info.FileName))
                    metadata["exeSha256"] = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                metadata["workingDirectory"] = info.WorkingDirectory;
                metadata["xbeSha256"] = RetailHash;
                metadata["environment"] = environment;
                File.WriteAllText(Path.Combine(session.DirectoryPath, "launch.json"), new JavaScriptSerializer().Serialize(metadata));
                session.Write("[launcher] BLACK PC / " + (options.GpuRenderer ? "GPU Kelvin (Direct3D 11)" : "CPU Kelvin") + (options.Smooth60 ? " / " + (options.SmoothHz != 0 ? options.SmoothHz : 60) + " FPS timing" : "") + " / " + options.Threads + " threads");
                session.process = new Process { StartInfo = info };
                session.process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) session.Write(e.Data); };
                session.process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) session.Write("[stderr] " + e.Data); };
                session.process.Start();
                session.process.BeginOutputReadLine();
                session.process.BeginErrorReadLine();
                return session;
            }
            catch
            {
                session.Stop();
                session.Dispose();
                throw;
            }
        }

        private void Write(string line)
        {
            lock (logGate)
            {
                try { if (log != null) log.WriteLine(line); }
                catch (Exception error) { LogFailure = error.Message; }
            }
        }

        internal int WaitForExit()
        {
            process.WaitForExit(); // Also drains redirected output.
            int code = process.ExitCode;
            Write("[launcher] Exit code: " + code);
            return code;
        }

        internal void ArchiveKernelLog(string root)
        {
            string source = Path.Combine(root, "xbox_kernel.log");
            if (File.Exists(source)) File.Copy(source, Path.Combine(DirectoryPath, "kernel.log"), true);
            File.WriteAllText(Path.Combine(DirectoryPath, "exit.json"), new JavaScriptSerializer().Serialize(new { exitCode = ExitCode, finishedUtc = DateTime.UtcNow.ToString("o"), logFailure = LogFailure }));
        }

        internal void Stop()
        {
            if (process == null) return;
            try { if (!process.HasExited) { Write("[launcher] Stop requested"); process.Kill(); } }
            catch (InvalidOperationException) { }
        }

        public void Dispose()
        {
            if (process != null) { process.Dispose(); process = null; }
            lock (logGate) { if (log != null) { log.Dispose(); log = null; } }
            if (gameLock != null) { gameLock.ReleaseMutex(); gameLock.Dispose(); gameLock = null; }
        }
    }

    // "Press a key or mouse button": keys are read as physical scancodes (ProcessCmdKey gets the
    // original message), so the stored binding is the key position, whatever the layout says.
    internal sealed class CaptureDialog : Form
    {
        private readonly Label prompt = new Label();
        private readonly Label problem = new Label();
        private readonly Stopwatch opened = Stopwatch.StartNew();
        internal string Token { get; private set; }     // null: cancelled; "" : clear this slot

        internal CaptureDialog(string actionLabel)
        {
            Text = "Bind " + actionLabel;
            ClientSize = new Size(480, 190);
            BackColor = Color.FromArgb(19, 23, 22); ForeColor = Color.FromArgb(239, 242, 236);
            Font = new Font("Segoe UI", 10);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            prompt.SetBounds(20, 18, 440, 70); prompt.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            prompt.Text = "Press a key or click a mouse button, or turn the wheel, for\n" + actionLabel + ".";
            problem.SetBounds(20, 92, 440, 24); problem.ForeColor = Color.FromArgb(255, 170, 120);
            var clear = new Button { Text = "Clear this binding", Location = new Point(20, 136), Size = new Size(170, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 46, 41), ForeColor = ForeColor };
            var cancel = new Button { Text = "Cancel", Location = new Point(360, 136), Size = new Size(100, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 46, 41), ForeColor = ForeColor };
            clear.FlatAppearance.BorderSize = 0; cancel.FlatAppearance.BorderSize = 0;
            clear.TabStop = false; cancel.TabStop = false;
            clear.Click += delegate { Token = ""; DialogResult = DialogResult.OK; };
            cancel.Click += delegate { Token = null; DialogResult = DialogResult.Cancel; };
            Controls.AddRange(new Control[] { prompt, problem, clear, cancel });
            MouseDown += OnMouse; prompt.MouseDown += OnMouse; problem.MouseDown += OnMouse;
            MouseWheel += delegate(object sender, MouseEventArgs e) { Accept(e.Delta > 0 ? "WheelUp" : "WheelDown"); };
            KeyPreview = true;
        }

        private void OnMouse(object sender, MouseEventArgs e)
        {
            if (opened.ElapsedMilliseconds < 400) return;         // the click that opened this is not a binding
            string token = e.Button == MouseButtons.Left ? "Mouse1" : e.Button == MouseButtons.Right ? "Mouse2" :
                           e.Button == MouseButtons.Middle ? "Mouse3" : e.Button == MouseButtons.XButton1 ? "Mouse4" :
                           e.Button == MouseButtons.XButton2 ? "Mouse5" : null;
            if (token != null) Accept(token);
        }

        private void Accept(string token)
        {
            Token = token; DialogResult = DialogResult.OK;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (msg.Msg == 0x100 || msg.Msg == 0x104)            // WM_KEYDOWN, WM_SYSKEYDOWN
            {
                int lparam = unchecked((int)(long)msg.LParam);
                int scancode = ((lparam >> 16) & 0xFF) | (((lparam >> 24) & 1) << 8);
                if (scancode == 0x01) { Token = null; DialogResult = DialogResult.Cancel; return true; }
                if (scancode == 0x57) { problem.Text = "F11 toggles fullscreen and cannot be bound."; return true; }
                InputBindings.KeyDef key = InputBindings.KeyForScancode(scancode);
                if (key == null) { problem.Text = "That key cannot be bound."; return true; }
                Accept(key.Token);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    internal sealed class BindingsDialog : Form
    {
        private readonly DataGridView grid = new DataGridView();
        private readonly Label note = new Label();
        private readonly Dictionary<string, List<string>> work = new Dictionary<string, List<string>>();
        internal Dictionary<string, string> Result { get; private set; }

        internal BindingsDialog(LaunchOptions options)
        {
            Text = "Keyboard and mouse bindings";
            ClientSize = new Size(760, 684);
            BackColor = Color.FromArgb(19, 23, 22); ForeColor = Color.FromArgb(239, 242, 236);
            Font = new Font("Segoe UI", 10);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            foreach (InputBindings.InputAction action in InputBindings.Actions)
                work[action.Name] = InputBindings.Tokens(InputBindings.Effective(options, action));

            var title = new Label { Text = "Double-click a slot (or select it and press Enter), then press the key or mouse button.", Location = new Point(20, 14), Size = new Size(720, 24), ForeColor = Color.FromArgb(155, 165, 169) };
            grid.SetBounds(20, 44, 720, 536);
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false; grid.ReadOnly = true; grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.BackgroundColor = Color.FromArgb(27, 32, 30); grid.GridColor = Color.FromArgb(47, 54, 49); grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; grid.ColumnHeadersHeight = 30;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(39, 46, 41); grid.ColumnHeadersDefaultCellStyle.ForeColor = ForeColor;
            grid.DefaultCellStyle.BackColor = Color.FromArgb(27, 32, 30); grid.DefaultCellStyle.ForeColor = ForeColor;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(151, 224, 66); grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(19, 23, 22);
            grid.RowTemplate.Height = 28;
            grid.AccessibleName = "Key bindings";
            grid.Columns.Add("action", "Action"); grid.Columns.Add("k1", "Binding 1"); grid.Columns.Add("k2", "Binding 2"); grid.Columns.Add("k3", "Binding 3");
            grid.Columns[0].Width = 240; grid.Columns[1].Width = 158; grid.Columns[2].Width = 158;
            grid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            foreach (InputBindings.InputAction action in InputBindings.Actions) grid.Rows.Add(action.Label, "", "", "");
            Refill();
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { Edit(e.RowIndex, e.ColumnIndex); };
            grid.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && grid.CurrentCell != null) { e.Handled = true; e.SuppressKeyPress = true; Edit(grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex); }
            };
            grid.SelectionChanged += delegate { ShowConflicts(); };
            note.SetBounds(20, 588, 720, 44); note.ForeColor = Color.FromArgb(155, 165, 169);
            var reset = new Button { Text = "Reset to defaults", Location = new Point(20, 634), Size = new Size(170, 36) };
            var ok = new Button { Text = "OK", Location = new Point(520, 634), Size = new Size(100, 36), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Location = new Point(640, 634), Size = new Size(100, 36), DialogResult = DialogResult.Cancel };
            foreach (Button button in new[] { reset, ok, cancel })
            {
                button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0;
                button.BackColor = button == ok ? Color.FromArgb(151, 224, 66) : Color.FromArgb(39, 46, 41);
                button.ForeColor = button == ok ? Color.FromArgb(19, 23, 22) : ForeColor;
            }
            reset.Click += delegate
            {
                foreach (InputBindings.InputAction action in InputBindings.Actions) work[action.Name] = InputBindings.Tokens(action.Default);
                Refill(); ShowConflicts();
            };
            ok.Click += delegate
            {
                var chosen = new Dictionary<string, string>();
                foreach (InputBindings.InputAction action in InputBindings.Actions) chosen[action.Name] = InputBindings.Join(work[action.Name]);
                Result = InputBindings.Repair(chosen);
            };
            AcceptButton = null; CancelButton = cancel;
            Controls.AddRange(new Control[] { title, grid, note, reset, ok, cancel });
            ShowConflicts();
        }

        private void Refill()
        {
            for (int row = 0; row < InputBindings.Actions.Length; row++)
            {
                List<string> tokens = work[InputBindings.Actions[row].Name];
                for (int slot = 0; slot < InputBindings.MaxPerAction; slot++)
                    grid.Rows[row].Cells[1 + slot].Value = slot < tokens.Count ? InputBindings.FindKey(tokens[slot]).Friendly : "";
            }
        }

        private void Edit(int row, int column)
        {
            if (row < 0 || column < 1) return;
            InputBindings.InputAction action = InputBindings.Actions[row];
            using (var capture = new CaptureDialog(action.Label))
            {
                if (capture.ShowDialog(this) != DialogResult.OK || capture.Token == null) return;
                List<string> tokens = work[action.Name];
                int slot = column - 1;
                if (capture.Token.Length == 0) { if (slot < tokens.Count) tokens.RemoveAt(slot); }
                else
                {
                    tokens.Remove(capture.Token);
                    if (slot >= tokens.Count) tokens.Add(capture.Token); else tokens[slot] = capture.Token;
                    while (tokens.Count > InputBindings.MaxPerAction) tokens.RemoveAt(tokens.Count - 1);
                }
            }
            Refill(); ShowConflicts();
        }

        // A key used by two actions is allowed (both fire) but worth saying.
        private void ShowConflicts()
        {
            var lines = new List<string>();
            foreach (InputBindings.InputAction a in InputBindings.Actions)
                foreach (string token in work[a.Name])
                    foreach (InputBindings.InputAction b in InputBindings.Actions)
                        if (b != a && string.CompareOrdinal(a.Name, b.Name) < 0 && work[b.Name].Contains(token))
                            lines.Add(InputBindings.FindKey(token).Friendly + ": " + a.Label + " and " + b.Label);
            note.Text = lines.Count == 0 ? "Escape (pause / back) and F11 (fullscreen) are fixed. Enter always confirms; arrow keys and the wheel move through menus."
                                         : "Shared keys: " + string.Join("; ", lines.ToArray()) + ". Both actions will fire.";
        }
    }

    internal sealed class LauncherForm : Form
    {
        private readonly string root;
        private readonly bool previewOnly;
        private readonly Color accent = Color.FromArgb(151, 224, 66);
        private readonly Color muted = Color.FromArgb(155, 165, 169);
        private readonly List<Font> fonts = new List<Font>();
        private readonly NumericUpDown threads = new NumericUpDown();
        private readonly CheckBox gpuRenderer = new CheckBox();
        private readonly CheckBox smooth60 = new CheckBox();
        private readonly ComboBox smoothRate = new ComboBox();
        private readonly CheckBox skipMovies = new CheckBox();
        private readonly CheckBox advanceMenus = new CheckBox();
        private readonly TabControl settingsTabs = new TabControl();
        private readonly ComboBox outputResolution = new ComboBox();
        private readonly ComboBox internalResolution = new ComboBox();
        private readonly ComboBox antiAliasing = new ComboBox();
        private readonly ComboBox anisotropy = new ComboBox();
        private readonly ComboBox depthOfField = new ComboBox();
        private readonly ComboBox sharpening = new ComboBox();
        private readonly ComboBox windowMode = new ComboBox();
        private readonly ComboBox scaleMode = new ComboBox();
        private readonly ComboBox presentFilter = new ComboBox();
        private readonly CheckBox vsync = new CheckBox();
        private readonly ComboBox fpsLimit = new ComboBox();
        private readonly ComboBox cameraAspect = new ComboBox();
        private readonly CheckBox originalFov = new CheckBox();
        private readonly NumericUpDown verticalFov = new NumericUpDown();
        private readonly ComboBox motionBlur = new ComboBox();
        private readonly ComboBox inputMode = new ComboBox();
        private readonly ComboBox promptMode = new ComboBox();
        private readonly NumericUpDown mouseSensitivity = new NumericUpDown();
        private readonly CheckBox invertY = new CheckBox();
        private Dictionary<string, string> bindings = new Dictionary<string, string>();
        private readonly ToolTip settingsTip = new ToolTip();
        private readonly Button play = new Button();
        private readonly Button stop = new Button();
        private readonly Label status = new Label();
        private readonly Label detail = new Label();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private GameSession session;
        private string logs;
        private bool closing, stopped;

        internal LauncherForm(string project, bool previewOnly = false)
        {
            root = project;
            this.previewOnly = previewOnly;
            logs = Path.Combine(root, "reports");
            Text = "BLACK | PC Launcher";
            ClientSize = new Size(700, 660);
            BackColor = Color.FromArgb(19, 23, 22);
            ForeColor = Color.FromArgb(239, 242, 236);
            Font = OwnFont(10, FontStyle.Regular);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Controls.Add(new Panel { BackColor = accent, Location = new Point(0, 0), Size = new Size(700, 4) });
            AddText("B L A C K", 30, 23, 640, 77, 43, FontStyle.Bold, ForeColor);
            AddText("BLACK  /  RECOMPILED FOR PC", 34, 107, 630, 23, 10, FontStyle.Bold, accent);
            AddText("Development build", 34, 155, 630, 26, 15, FontStyle.Bold, ForeColor);
            AddText("First-mission gameplay works with keyboard and mouse or a controller.\nThis is an experimental build; crashes and inaccuracies remain.", 34, 190, 630, 50, 11, FontStyle.Regular, muted);
            settingsTabs.SetBounds(34, 264, 632, 218);
            var display = new TabPage("Display") { BackColor = BackColor, ForeColor = ForeColor };
            var game = new TabPage("Game") { BackColor = BackColor, ForeColor = ForeColor };
            var graphics = new TabPage("Graphics") { BackColor = BackColor, ForeColor = ForeColor };
            var input = new TabPage("Input") { BackColor = BackColor, ForeColor = ForeColor };
            settingsTabs.TabPages.Add(display); settingsTabs.TabPages.Add(graphics); settingsTabs.TabPages.Add(input); settingsTabs.TabPages.Add(game);
            Controls.Add(settingsTabs);
            AddOptionLabel(display, "Window size", 14, 16, 95);
            outputResolution.SetBounds(114, 12, 200, 30);
            outputResolution.DropDownStyle = ComboBoxStyle.DropDownList;
            outputResolution.BackColor = Color.FromArgb(37, 43, 39); outputResolution.ForeColor = ForeColor;
            outputResolution.AccessibleName = "Output resolution";
            outputResolution.DropDownWidth = 220; outputResolution.MaxDropDownItems = 12;
            outputResolution.Items.AddRange(new ResolutionChoice[] {
                new ResolutionChoice(640, 480), new ResolutionChoice(800, 600), new ResolutionChoice(1024, 768),
                new ResolutionChoice(1152, 864), new ResolutionChoice(1280, 720), new ResolutionChoice(1280, 800),
                new ResolutionChoice(1280, 960), new ResolutionChoice(1280, 1024), new ResolutionChoice(1366, 768),
                new ResolutionChoice(1400, 1050), new ResolutionChoice(1440, 900), new ResolutionChoice(1600, 900),
                new ResolutionChoice(1600, 1200), new ResolutionChoice(1680, 1050), new ResolutionChoice(1920, 1080),
                new ResolutionChoice(1920, 1200), new ResolutionChoice(1920, 1440), new ResolutionChoice(2048, 1536),
                new ResolutionChoice(2560, 1080), new ResolutionChoice(2560, 1440), new ResolutionChoice(2560, 1600),
                new ResolutionChoice(3440, 1440), new ResolutionChoice(3840, 1080), new ResolutionChoice(3840, 1600),
                new ResolutionChoice(3840, 2160), new ResolutionChoice(3840, 2400), new ResolutionChoice(5120, 1440),
                new ResolutionChoice(5120, 2160), new ResolutionChoice(7680, 2160)
            });
            var defaultOutput = new LaunchOptions(); SelectResolution(defaultOutput.OutputWidth, defaultOutput.OutputHeight);
            display.Controls.Add(outputResolution);
            AddOptionLabel(display, "Window", 327, 16, 80);
            ConfigureChoices(windowMode, "Window mode", 417, 12, 185,
                new DisplayChoice("windowed", "Windowed"), new DisplayChoice("borderless", "Borderless fullscreen")); display.Controls.Add(windowMode);
            AddOptionLabel(display, "Scaling", 14, 53, 95);
            ConfigureChoices(scaleMode, "Scaling mode", 114, 49, 200,
                new DisplayChoice("fit", "Keep aspect ratio"), new DisplayChoice("stretch", "Stretch to fill"), new DisplayChoice("integer", "Integer scaling")); display.Controls.Add(scaleMode);
            AddOptionLabel(display, "Filter", 327, 53, 80);
            ConfigureChoices(presentFilter, "Scaling filter", 417, 49, 185,
                new DisplayChoice("linear", "Smooth"), new DisplayChoice("nearest", "Sharp")); display.Controls.Add(presentFilter);
            vsync.SetBounds(14, 87, 295, 26); vsync.Text = "VSync"; vsync.Checked = true; vsync.AccessibleName = "VSync"; display.Controls.Add(vsync);
            AddOptionLabel(display, "Frame limit", 327, 90, 88);
            var limits = new List<DisplayChoice>();
            foreach (int limit in LaunchOptions.FrameLimits) limits.Add(new DisplayChoice(limit.ToString(CultureInfo.InvariantCulture), limit == 0 ? "Unlimited" : limit + " FPS"));
            ConfigureChoices(fpsLimit, "Presentation frame limit", 417, 86, 185, limits.ToArray()); display.Controls.Add(fpsLimit);
            AddOptionLabel(display, "Camera", 14, 127, 95);
            ConfigureChoices(cameraAspect, "Camera aspect", 114, 123, 200,
                new DisplayChoice("original", "Original"), new DisplayChoice("4:3", "Standard (4:3)"), new DisplayChoice("16:9", "Widescreen (16:9)"),
                new DisplayChoice("21:9", "Ultrawide (21:9)"), new DisplayChoice("32:9", "Ultrawide (32:9)"));
            cameraAspect.Enabled = LaunchOptions.CameraAspectAvailable; display.Controls.Add(cameraAspect);
            AddOptionLabel(display, "Motion blur", 327, 127, 88);
            ConfigureChoices(motionBlur, "Motion blur", 417, 123, 185,
                new DisplayChoice("original", "Original"), new DisplayChoice("off", "Off"));
            motionBlur.Enabled = LaunchOptions.MotionBlurAvailable; display.Controls.Add(motionBlur);
            AddOptionLabel(display, "Field of view", 14, 163, 95);
            originalFov.SetBounds(114, 159, 100, 28); originalFov.Text = "Original"; originalFov.Checked = true;
            originalFov.AccessibleName = "Original field of view"; display.Controls.Add(originalFov);
            verticalFov.SetBounds(218, 159, 96, 30); verticalFov.DecimalPlaces = 2; verticalFov.Increment = 1.0M;
            verticalFov.Minimum = (decimal)LaunchOptions.FovMin; verticalFov.Maximum = (decimal)LaunchOptions.FovMax;
            verticalFov.Value = 60.0M; verticalFov.Enabled = false;
            verticalFov.BackColor = Color.FromArgb(37, 43, 39); verticalFov.ForeColor = ForeColor;
            verticalFov.AccessibleName = "Vertical field of view in degrees"; display.Controls.Add(verticalFov);
            originalFov.CheckedChanged += delegate { verticalFov.Enabled = !originalFov.Checked; };
            AddOptionLabel(display, "Internal", 327, 163, 88);
            ConfigureChoices(internalResolution, "Internal resolution", 417, 159, 185,
                new DisplayChoice("480", "480p"), new DisplayChoice("720", "720p (default)"), new DisplayChoice("1080", "1080p"),
                new DisplayChoice("1440", "1440p"), new DisplayChoice("2160", "2160p"));
            SelectChoice(internalResolution, new LaunchOptions().InternalHeight.ToString(CultureInfo.InvariantCulture));
            display.Controls.Add(internalResolution);
            settingsTip.SetToolTip(originalFov, "Keep BLACK's original field of view and aim zoom. Uncheck to choose a vertical angle in degrees.");
            settingsTip.SetToolTip(verticalFov, "Vertical field of view in vertical degrees. Higher values show more above and below; the camera aspect sets how wide the view is.");
            settingsTip.SetToolTip(scaleMode, "Controls how the picture fits a resized window or a movie. Keep aspect ratio adds bars; Stretch fills the window.");
            settingsTip.SetToolTip(outputResolution, "The size of the game window when windowed. A saved nonstandard size is retained as a saved choice.");
            settingsTip.SetToolTip(internalResolution, "How many lines the game renders; the width follows the camera aspect, and the picture is scaled to the window. Higher is sharper and slower. The game's own Video Settings page (Options) changes this too.");
            settingsTip.SetToolTip(presentFilter, "Sizing and filtering are independent. Smooth blends neighboring pixels; Sharp keeps distinct pixel edges.");
            settingsTip.SetToolTip(fpsLimit, "Limits display updates. This does not change the game's simulation speed.");
            settingsTip.SetToolTip(cameraAspect, "Changes how wide the camera can see. Resolution controls picture detail; field of view controls its vertical angle.");
            settingsTip.SetToolTip(motionBlur, "Original keeps BLACK's own motion blur. Off disables that effect while preserving other post effects.");
            AddOptionLabel(graphics, "Anti-aliasing", 14, 16, 120);
            ConfigureChoices(antiAliasing, "Anti-aliasing", 140, 12, 174,
                new DisplayChoice("off", "Off"), new DisplayChoice("fxaa", "FXAA"), new DisplayChoice("ssaa", "SSAA 4x (2 x 2)"));
            SelectChoice(antiAliasing, new LaunchOptions().AntiAliasing); graphics.Controls.Add(antiAliasing);
            AddOptionLabel(graphics, "Anisotropic", 327, 16, 90);
            var levels = new List<DisplayChoice>();
            foreach (int level in LaunchOptions.AnisotropyLevels) levels.Add(new DisplayChoice(level.ToString(CultureInfo.InvariantCulture), level == 1 ? "Off" : level + "x"));
            ConfigureChoices(anisotropy, "Anisotropic filtering", 417, 12, 185, levels.ToArray());
            SelectChoice(anisotropy, new LaunchOptions().Anisotropy.ToString(CultureInfo.InvariantCulture)); graphics.Controls.Add(anisotropy);
            AddOptionLabel(graphics, "Depth of field", 14, 53, 120);
            ConfigureChoices(depthOfField, "Depth of field", 140, 49, 174,
                new DisplayChoice("off", "Off"), new DisplayChoice("low", "Low"), new DisplayChoice("medium", "Medium"), new DisplayChoice("high", "High"));
            graphics.Controls.Add(depthOfField);
            AddOptionLabel(graphics, "Sharpening", 327, 53, 90);
            ConfigureChoices(sharpening, "Sharpening", 417, 49, 185,
                new DisplayChoice("off", "Off"), new DisplayChoice("low", "Low"), new DisplayChoice("medium", "Medium"), new DisplayChoice("high", "High"));
            graphics.Controls.Add(sharpening);
            AddOptionLabel(graphics, "Effects of the GPU renderer, applied to the picture before the HUD. The game's Video Settings page", 14, 100, 592).ForeColor = muted;
            AddOptionLabel(graphics, "(Options > V) changes them while playing. Internal resolution is on the Display tab.", 14, 125, 592).ForeColor = muted;
            settingsTip.SetToolTip(antiAliasing, "FXAA smooths jagged edges at almost no cost. SSAA 4x renders twice as wide and high and averages down: the cleanest and the heaviest. Needs the GPU renderer.");
            settingsTip.SetToolTip(anisotropy, "Keeps textures sharp on floors and walls seen at a slant. Costs almost nothing. Needs the GPU renderer.");
            settingsTip.SetToolTip(depthOfField, "Blurs what is behind the point you are looking at; the weapon and the HUD stay sharp. Needs the GPU renderer.");
            settingsTip.SetToolTip(sharpening, "Brings back fine detail that filtering and scaling soften. Needs the GPU renderer.");
            AddOptionLabel(input, "Devices", 14, 16, 95);
            ConfigureChoices(inputMode, "Input devices", 114, 12, 200,
                new DisplayChoice("auto", "Keyboard, mouse, pad"), new DisplayChoice("keyboard_mouse", "Keyboard and mouse only"), new DisplayChoice("controller", "Controller only")); input.Controls.Add(inputMode);
            AddOptionLabel(input, "Prompts", 327, 16, 80);
            ConfigureChoices(promptMode, "Button prompts", 417, 12, 185,
                new DisplayChoice("auto", "Follow the device I use"), new DisplayChoice("keyboard_mouse", "Keyboard and mouse"), new DisplayChoice("controller", "Controller")); input.Controls.Add(promptMode);
            AddOptionLabel(input, "Sensitivity", 14, 53, 95);
            mouseSensitivity.SetBounds(114, 49, 86, 30); mouseSensitivity.DecimalPlaces = 2; mouseSensitivity.Increment = 0.05M;
            mouseSensitivity.Minimum = (decimal)InputBindings.SensitivityMin; mouseSensitivity.Maximum = (decimal)InputBindings.SensitivityMax; mouseSensitivity.Value = 1.0M;
            mouseSensitivity.BackColor = Color.FromArgb(37, 43, 39); mouseSensitivity.ForeColor = ForeColor; mouseSensitivity.AccessibleName = "Mouse sensitivity";
            input.Controls.Add(mouseSensitivity);
            AddOptionLabel(input, "0.06\u00B0 / count", 208, 53, 110).ForeColor = muted;
            invertY.SetBounds(327, 51, 280, 26); invertY.Text = "Invert vertical mouse look"; invertY.AccessibleName = invertY.Text; input.Controls.Add(invertY);
            var editBindings = new Button { Text = "Key bindings...", Location = new Point(14, 90), Size = new Size(200, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 46, 41), ForeColor = ForeColor, AccessibleName = "Key bindings" };
            editBindings.FlatAppearance.BorderSize = 0;
            editBindings.Click += delegate
            {
                using (var dialog = new BindingsDialog(Options()))
                    if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result != null) bindings = dialog.Result;
            };
            input.Controls.Add(editBindings);
            AddOptionLabel(input, "WASD moves, the mouse looks, the left button fires.", 227, 95, 380).ForeColor = muted;
            AddOptionLabel(input, "Escape pauses a mission (and goes back in menus). Leaving the window pauses too.", 14, 135, 592).ForeColor = muted;
            AddOptionLabel(input, "F11 toggles fullscreen. In-game prompts show your real bindings.", 14, 160, 592).ForeColor = muted;
            settingsTip.SetToolTip(inputMode, "Controller only ignores the keyboard and mouse in the game; keyboard and mouse only ignores the pad.");
            settingsTip.SetToolTip(mouseSensitivity, "Raw mouse input, no acceleration. 1.00 is 0.06 degrees per mouse count.");
            gpuRenderer.SetBounds(14, 6, 590, 28);
            gpuRenderer.Text = "GPU renderer (Direct3D 11)"; gpuRenderer.Checked = true;
            gpuRenderer.AccessibleName = gpuRenderer.Text;
            game.Controls.Add(gpuRenderer);
            smooth60.SetBounds(14, 36, 360, 28);
            smooth60.Text = "Faster timing (the original game ran at 30)"; smooth60.Checked = true;
            smooth60.AccessibleName = smooth60.Text;
            game.Controls.Add(smooth60);
            ConfigureChoices(smoothRate, "Timing rate", 390, 34, 212,
                new DisplayChoice("0", "60 steps per second"), new DisplayChoice("120", "120 steps per second"), new DisplayChoice("240", "240 steps per second"));
            game.Controls.Add(smoothRate);
            smooth60.CheckedChanged += delegate { smoothRate.Enabled = smooth60.Checked; };
            settingsTip.SetToolTip(smoothRate, "How often the game's clock steps. Presented frames match it, so 240 needs a fast PC and a 240 Hz display to show.");
            settingsTip.SetToolTip(gpuRenderer, "Draws the game on the graphics card, which holds 60 FPS at 3440x1440. Off uses the CPU renderer, which is far slower at high resolutions.");
            settingsTip.SetToolTip(smooth60, "Runs the game clock faster so every presented frame is a simulated frame. Off keeps the original 30 Hz clock.");
            AddOptionLabel(game, "Renderer threads", 14, 76, 180);
            threads.SetBounds(194, 72, 90, 31);
            threads.Minimum = 1; threads.Maximum = 64;
            threads.BackColor = Color.FromArgb(37, 43, 39); threads.ForeColor = ForeColor;
            threads.Value = new LaunchOptions().Threads;
            threads.AccessibleName = "Renderer threads";
            game.Controls.Add(threads);
            AddOptionLabel(game, new LaunchOptions().Threads + " default on this " + Environment.ProcessorCount + "-thread PC", 298, 77, 300).ForeColor = muted;
            skipMovies.SetBounds(14, 110, 590, 28);
            skipMovies.Text = "Skip movies"; skipMovies.Checked = false;
            skipMovies.AccessibleName = skipMovies.Text;
            game.Controls.Add(skipMovies);
            advanceMenus.SetBounds(14, 142, 590, 28);
            advanceMenus.Text = "Advance startup menus automatically (first 65 seconds)";
            advanceMenus.AccessibleName = advanceMenus.Text;
            game.Controls.Add(advanceMenus);
            status.SetBounds(34, 489, 630, 27); status.Font = OwnFont(11, FontStyle.Bold);
            Controls.Add(status);
            detail.SetBounds(34, 519, 630, 43); detail.ForeColor = muted;
            Controls.Add(detail);
            StyleButton(play, "PLAY", 34, 572, 190, true);
            StyleButton(stop, "Stop game", 236, 572, 122, false); stop.Enabled = false;
            var openLogs = new Button(); StyleButton(openLogs, "Open logs", 370, 572, 140, false);
            var controls = new Button(); StyleButton(controls, "Controls", 522, 572, 144, false);
            AddText("BLACK PC  /  Local saves", 34, 633, 630, 22, 9, FontStyle.Regular, muted);
            LoadSettings();
            play.Click += delegate { StartGame(); };
            stop.Click += delegate { StopGame(); };
            openLogs.Click += delegate
            {
                try { Directory.CreateDirectory(logs); using (Process.Start(new ProcessStartInfo { FileName = logs, UseShellExecute = true })) { } }
                catch (Exception error) { MessageBox.Show(this, error.Message, "Could not open logs"); }
            };
            controls.Click += delegate
            {
                MessageBox.Show(this, InputBindings.Help(Options()) + "\nClose the game window or use Stop game to end the session. Finish saving before closing.", "BLACK PC controls", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (session == null) { if (!previewOnly) SaveSettings(); return; }
                e.Cancel = true; closing = true; StopGame();
            };
            timer.Interval = 2000;
            timer.Tick += delegate { if (session == null) RefreshReady(); };
            timer.Start();
            RefreshReady();
        }

        internal void SelectSettingsTab(int index)
        {
            if (index >= 0 && index < settingsTabs.TabPages.Count) settingsTabs.SelectedIndex = index;
        }

        private Font OwnFont(float size, FontStyle style)
        {
            var font = new Font("Segoe UI", size, style); fonts.Add(font); return font;
        }
        private sealed class DisplayChoice
        {
            internal readonly string Value;
            private readonly string label;
            internal DisplayChoice(string value, string text) { Value = value; label = text; }
            public override string ToString() { return label; }
        }
        private sealed class ResolutionChoice
        {
            internal readonly int Width, Height;
            private readonly bool saved;
            internal ResolutionChoice(int width, int height, bool saved = false) { Width = width; Height = height; this.saved = saved; }
            public override string ToString()
            {
                return Width.ToString(CultureInfo.InvariantCulture) + " × " + Height.ToString(CultureInfo.InvariantCulture) + (saved ? " (saved)" : "");
            }
        }
        private void SelectResolution(int width, int height)
        {
            for (int index = 0; index < outputResolution.Items.Count; index++)
            {
                var choice = (ResolutionChoice)outputResolution.Items[index];
                if (choice.Width == width && choice.Height == height) { outputResolution.SelectedIndex = index; return; }
            }
            outputResolution.SelectedIndex = outputResolution.Items.Add(new ResolutionChoice(width, height, true));
        }
        private Label AddOptionLabel(Control parent, string text, int x, int y, int width)
        {
            var label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, 25), ForeColor = ForeColor };
            parent.Controls.Add(label); return label;
        }
        private void ConfigureChoices(ComboBox box, string name, int x, int y, int width, params DisplayChoice[] choices)
        {
            box.SetBounds(x, y, width, 30); box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.BackColor = Color.FromArgb(37, 43, 39); box.ForeColor = ForeColor; box.AccessibleName = name;
            box.Items.AddRange(choices); box.SelectedIndex = 0;
        }
        private static string ChoiceValue(ComboBox box) { return ((DisplayChoice)box.SelectedItem).Value; }
        private static void SelectChoice(ComboBox box, string value)
        {
            for (int index = 0; index < box.Items.Count; index++)
                if (((DisplayChoice)box.Items[index]).Value == value) { box.SelectedIndex = index; return; }
            box.SelectedIndex = 0;
        }
        private void AddText(string text, int x, int y, int width, int height, float size, FontStyle style, Color color)
        {
            Controls.Add(new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), Font = OwnFont(size, style), ForeColor = color });
        }
        private void StyleButton(Button button, string text, int x, int y, int width, bool primary)
        {
            button.Text = text; button.SetBounds(x, y, width, 46); button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = primary ? accent : Color.FromArgb(39, 46, 41);
            button.ForeColor = primary ? BackColor : ForeColor;
            button.Font = OwnFont(10, FontStyle.Bold); button.Cursor = Cursors.Hand;
            Controls.Add(button);
        }
        private string SettingsPath { get { return Path.Combine(root, "local", "xbox-launcher.json"); } }
        // The video settings are one file shared with the game (local\video.ini): what the player last saved in the
        // game's Video Settings page shows here, and what is set here is what the game starts with.
        private string VideoPath { get { return LaunchOptions.VideoIniPath(root); } }
        private void ShowVideo(LaunchOptions settings)
        {
            SelectResolution(settings.OutputWidth, settings.OutputHeight);
            SelectChoice(internalResolution, settings.InternalHeight.ToString(CultureInfo.InvariantCulture));
            SelectChoice(windowMode, settings.WindowMode); SelectChoice(scaleMode, settings.ScaleMode);
            SelectChoice(presentFilter, settings.PresentFilter); vsync.Checked = settings.VSync;
            SelectChoice(fpsLimit, settings.FpsLimit.ToString(CultureInfo.InvariantCulture));
            SelectChoice(cameraAspect, settings.CameraAspect); SelectChoice(motionBlur, settings.MotionBlur);
            originalFov.Checked = settings.VerticalFov == 0.0;
            if (!originalFov.Checked) verticalFov.Value = (decimal)settings.VerticalFov;
            SelectChoice(antiAliasing, settings.AntiAliasing); SelectChoice(anisotropy, settings.Anisotropy.ToString(CultureInfo.InvariantCulture));
            SelectChoice(depthOfField, settings.DepthOfField); SelectChoice(sharpening, settings.Sharpening);
        }
        private void ReloadVideo()
        {
            try
            {
                var current = Options();
                if (File.Exists(VideoPath) && current.ApplyVideoIni(File.ReadAllText(VideoPath)) > 0) ShowVideo(current);
            }
            catch { }
        }
        private void LoadSettings()
        {
            try
            {
                bool haveJson = File.Exists(SettingsPath), haveVideo = File.Exists(VideoPath);
                if (!haveJson && !haveVideo) return;
                var settings = haveJson ? LaunchOptions.FromSettingsJson(File.ReadAllText(SettingsPath)) : new LaunchOptions();
                if (haveVideo) settings.ApplyVideoIni(File.ReadAllText(VideoPath));
                threads.Value = settings.Threads;
                skipMovies.Checked = settings.SkipMovies; advanceMenus.Checked = settings.AdvanceMenus;
                gpuRenderer.Checked = settings.GpuRenderer; smooth60.Checked = settings.Smooth60;
                SelectChoice(smoothRate, settings.SmoothHz.ToString(CultureInfo.InvariantCulture)); smoothRate.Enabled = smooth60.Checked;
                ShowVideo(settings);
                SelectChoice(inputMode, settings.InputMode); SelectChoice(promptMode, settings.PromptMode);
                mouseSensitivity.Value = (decimal)Math.Max(InputBindings.SensitivityMin, Math.Min(InputBindings.SensitivityMax, settings.MouseSensitivity));
                invertY.Checked = settings.InvertY; bindings = settings.Bindings;
            }
            catch { }
        }
        private void SaveSettings()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)); File.WriteAllText(SettingsPath, new JavaScriptSerializer().Serialize(Options())); }
            catch (Exception error) { detail.Text = "Settings could not be saved: " + error.Message; }
        }
        private LaunchOptions Options()
        {
            var resolution = (ResolutionChoice)outputResolution.SelectedItem;
            return new LaunchOptions {
                Threads = (int)threads.Value, SkipMovies = skipMovies.Checked, AdvanceMenus = advanceMenus.Checked,
                GpuRenderer = gpuRenderer.Checked, Smooth60 = smooth60.Checked, SmoothHz = int.Parse(ChoiceValue(smoothRate), CultureInfo.InvariantCulture),
                OutputWidth = resolution.Width, OutputHeight = resolution.Height,
                InternalHeight = int.Parse(ChoiceValue(internalResolution), CultureInfo.InvariantCulture),
                AntiAliasing = ChoiceValue(antiAliasing), Anisotropy = int.Parse(ChoiceValue(anisotropy), CultureInfo.InvariantCulture),
                DepthOfField = ChoiceValue(depthOfField), Sharpening = ChoiceValue(sharpening),
                WindowMode = ChoiceValue(windowMode), ScaleMode = ChoiceValue(scaleMode), PresentFilter = ChoiceValue(presentFilter),
                VSync = vsync.Checked, FpsLimit = int.Parse(ChoiceValue(fpsLimit), CultureInfo.InvariantCulture),
                CameraAspect = ChoiceValue(cameraAspect), MotionBlur = ChoiceValue(motionBlur),
                VerticalFov = originalFov.Checked ? 0.0 : (double)verticalFov.Value,
                InputMode = ChoiceValue(inputMode), PromptMode = ChoiceValue(promptMode),
                MouseSensitivity = (double)mouseSensitivity.Value, InvertY = invertY.Checked,
                Bindings = new Dictionary<string, string>(bindings)
            };
        }
        internal static string ReadinessText(int existing, bool haveBuild)
        {
            return existing != 0 ? "PC game already running (PID " + existing + ")" : haveBuild ? "Ready to play" : "PC Release build is missing";
        }
        private void RefreshReady()
        {
            int existing = GameSession.ExistingProcess(root);
            bool haveBuild = File.Exists(GameSession.Executable(root));
            play.Enabled = !previewOnly && existing == 0 && haveBuild;
            status.Text = ReadinessText(existing, haveBuild);
            detail.Text = existing != 0 ? "Close the existing game before Play. This launcher can stop sessions it starts." : "Keyboard and mouse are ready; a controller is optional. Startup may take a minute.\nSaves stay in the local save folder.";
        }
        private void StartGame()
        {
            if (previewOnly || session != null) return;
            try
            {
                SaveSettings();
                session = GameSession.Start(root, Options()); logs = session.DirectoryPath;
                play.Enabled = settingsTabs.Enabled = false;
                stop.Enabled = true; stopped = false;
                status.Text = "Game running";
                detail.Text = "Use the BLACK PC window to play.\nStop ends the game process; finish saving first.";
                var running = session;
                Task.Factory.StartNew(delegate
                {
                    int code = -1; string error = null;
                    try { code = running.WaitForExit(); running.ArchiveKernelLog(root); error = running.LogFailure; }
                    catch (Exception failure) { error = failure.Message; }
                    BeginInvoke(new Action(delegate { GameExited(running, code, error); }));
                });
            }
            catch (Exception error)
            {
                status.Text = "Unable to launch";
                MessageBox.Show(this, error.Message, "BLACK PC", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void StopGame()
        {
            if (session == null) return;
            try { session.Stop(); stopped = true; stop.Enabled = false; status.Text = "Stopping game..."; }
            catch (Exception error) { closing = false; MessageBox.Show(this, error.Message, "Could not stop the game"); }
        }
        private void GameExited(GameSession running, int code, string error)
        {
            running.Dispose(); session = null;
            settingsTabs.Enabled = true; stop.Enabled = false;
            ReloadVideo();                                  // the game's Video Settings page may have changed them
            RefreshReady();
            status.Text = stopped ? "Game stopped" : code == 0 && error == null ? "Game closed" : "Game exited with an error (" + code + ")";
            detail.Text = error == null ? "Open logs for this session's output and launch settings." : "Session logging error: " + error;
            if (closing) Close();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); settingsTip.Dispose(); base.Dispose(disposing); foreach (var font in fonts) font.Dispose(); fonts.Clear(); }
            else base.Dispose(disposing);
        }
    }
}
