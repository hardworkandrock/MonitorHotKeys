using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace MonitorHotKeys
{
    public class LanguageManager
    {
        private const string LANG_FILE = "current_lang.txt";
        private static readonly string _langDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Languages");
        private static Dictionary<string, string> _strings = new();
        public static string CurrentLanguage { get; private set; } = "ru";

        static LanguageManager()
        {
            LoadLanguage();
        }

        public static void SetLanguage(string langCode)
        {
            if (langCode != "ru" && langCode != "en")
                throw new ArgumentException("Unsupported language code");

            CurrentLanguage = langCode;
            SaveCurrentLanguage();
            LoadLanguage();
        }

        private static void LoadLanguage()
        {
            string langFile = Path.Combine(_langDir, $"{CurrentLanguage}.json");
            if (!File.Exists(langFile))
            {
                // Fallback to embedded resource or default
                CurrentLanguage = "en";
                langFile = Path.Combine(_langDir, "en.json");
            }

            try
            {
                string json = File.ReadAllText(langFile);
                _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch
            {
                _strings = new Dictionary<string, string>();
            }
        }

        private static void SaveCurrentLanguage()
        {
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LANG_FILE), CurrentLanguage);
        }

        public static string GetString(string key, params object[] args)
        {
            if (_strings.TryGetValue(key, out string value))
            {
                return args.Length > 0 ? string.Format(value, args) : value;
            }
            return $"[{key}]"; // Fallback for missing keys
        }

        public static string GetCurrentLanguageName()
        {
            return CurrentLanguage == "ru" ? "Русский" : "English";
        }
    }
}