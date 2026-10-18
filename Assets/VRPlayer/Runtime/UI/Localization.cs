using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VRPlayer
{
    public enum AppLanguage { System, Chinese, English, Japanese }

    // Chinese source keys keep diagnostics, cached evidence and navigation IDs stable.
    // Only app-owned presentation text is passed here; media and server text is untouched.
    public static class Localization
    {
        [Serializable] public sealed class Entry { public string zh, en, ja; public bool message, localizeArguments; }
        [Serializable] public sealed class Catalog { public Entry[] entries; }
        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly List<(Entry entry, Regex pattern)> messages = new List<(Entry, Regex)>();
        private static bool loaded;
        public static AppLanguage Preference { get; private set; } = AppLanguage.Chinese;
        public static AppLanguage Language { get; private set; } = AppLanguage.Chinese;
        public static int Revision { get; private set; }
        public static bool EnglishLegalFirst => Language != AppLanguage.Chinese;
        public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language == AppLanguage.Chinese ? "zh-CN" : Language == AppLanguage.Japanese ? "ja-JP" : "en-US");

        public static AppLanguage Normalize(AppLanguage language) => Enum.IsDefined(typeof(AppLanguage), language) ? language : AppLanguage.System;
        public static AppLanguage Resolve(AppLanguage preference, SystemLanguage systemLanguage)
        {
            preference = Normalize(preference);
            if (preference != AppLanguage.System) return preference;
            if (systemLanguage == SystemLanguage.Chinese || systemLanguage == SystemLanguage.ChineseSimplified || systemLanguage == SystemLanguage.ChineseTraditional) return AppLanguage.Chinese;
            return systemLanguage == SystemLanguage.Japanese ? AppLanguage.Japanese : AppLanguage.English;
        }
        public static bool SetLanguage(AppLanguage preference) => SetLanguage(preference, Application.systemLanguage);
        public static bool SetLanguage(AppLanguage preference, SystemLanguage systemLanguage)
        {
            Load();
            preference = Normalize(preference);
            var language = Resolve(preference, systemLanguage);
            bool changed = preference != Preference || language != Language;
            Preference = preference; Language = language;
            if (changed) Revision++;
            return changed;
        }
        private static void Load()
        {
            if (loaded) return;
            var asset = Resources.Load<TextAsset>("Localization/Strings");
            if (asset == null) throw new InvalidOperationException("Localization catalog is missing.");
            var catalog = JsonUtility.FromJson<Catalog>(asset.text);
            foreach (var entry in catalog.entries)
            {
                entries.Add(entry.zh, entry);
                if (!entry.message) continue;
                var pattern = new StringBuilder("\\A");
                int start = 0;
                foreach (Match field in Regex.Matches(entry.zh, @"\{(\d+)(?:[^{}]*?)\}"))
                {
                    pattern.Append(Regex.Escape(entry.zh.Substring(start, field.Index - start)));
                    pattern.Append("(?<arg").Append(field.Groups[1].Value).Append(">.*?)");
                    start = field.Index + field.Length;
                }
                pattern.Append(Regex.Escape(entry.zh.Substring(start))).Append("\\z");
                messages.Add((entry, new Regex(pattern.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant)));
            }
            // Specific whole-message templates take precedence over shorter prefixes.
            messages.Sort((a, b) => b.entry.zh.Length.CompareTo(a.entry.zh.Length));
            loaded = true;
        }
        public static string T(string source)
        {
            if (string.IsNullOrEmpty(source) || Language == AppLanguage.Chinese) return source;
            Load();
            return entries.TryGetValue(source, out var entry) ? Translate(entry) : source;
        }
        private static string Translate(Entry entry) => Language == AppLanguage.Japanese ? entry.ja : Language == AppLanguage.English ? entry.en : entry.zh;
        public static string F(string source, params object[] arguments) => string.Format(Culture, T(source), arguments);

        // Native errors and existing diagnostic messages arrive in canonical Chinese.
        // Match a whole controlled message and preserve captured filenames/addresses.
        public static string Message(string source)
        {
            if (string.IsNullOrEmpty(source) || Language == AppLanguage.Chinese) return source;
            Load();
            if (entries.TryGetValue(source, out var entry)) return Translate(entry);
            foreach (var candidate in messages)
            {
                var match = candidate.pattern.Match(source);
                if (!match.Success) continue;
                var arguments = new object[match.Groups.Count - 1];
                for (int i = 0; i < arguments.Length; i++)
                {
                    string value = match.Groups["arg" + i].Value;
                    arguments[i] = candidate.entry.localizeArguments ? Message(value) : value;
                }
                return string.Format(Culture, Translate(candidate.entry), arguments);
            }
            return source;
        }
        public static string Bilingual(string chinese, string english, string separator = " / ") => EnglishLegalFirst ? english + separator + chinese : chinese + separator + english;
        public static string LanguageName(AppLanguage language) => language switch
        {
            AppLanguage.Chinese => "简体中文", AppLanguage.English => "English", AppLanguage.Japanese => "日本語", _ => T("跟随系统")
        };
    }
}
