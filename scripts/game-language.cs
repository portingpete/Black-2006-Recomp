using System;
using System.IO;

namespace BlackXboxLauncher
{
    internal static class GameLanguageBanks
    {
        internal const string English = "en-US";
        internal const string PortugueseBrazil = "pt-BR";

        internal static bool IsSupported(string language)
        {
            return language == English || language == PortugueseBrazil;
        }

        internal static string Normalize(string language)
        {
            return IsSupported(language) ? language : English;
        }

        internal static string BankPath(string root, string language)
        {
            if (!IsSupported(language)) throw new ArgumentException("Choose English or Português (Brasil).", "language");
            return Path.Combine(Path.GetFullPath(root), "local", "language-banks", language, "MainUS.bin");
        }

        internal static string GamePath(string root)
        {
            return Path.Combine(Path.GetFullPath(root), "game", "language", "strings", "MainUS.bin");
        }

        internal static void Install(string root, string language)
        {
            string source = BankPath(root, language);
            string target = GamePath(root);
            if (!File.Exists(source))
                throw new FileNotFoundException("The selected language bank is missing. Restore it under local\\language-banks\\" + language + "\\MainUS.bin.", source);
            if (!File.Exists(target))
                throw new FileNotFoundException("The game's MainUS.bin is missing from game\\language\\strings.", target);

            string temporary = target + ".launcher-tmp";
            try
            {
                File.Copy(source, temporary, true);
                File.Replace(temporary, target, null);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
