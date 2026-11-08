using System.Diagnostics;
using System.Text.RegularExpressions;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Locales;

namespace MHServerEmu.Games.MTXStore
{
    /// <summary>
    /// The items the add G panel can give for free (MTXStore FreeItemsEnabled), searchable by their in-game names.
    /// </summary>
    public static class FreeItemCatalog
    {
        public const int MaxSearchResults = 50;

        private static readonly Logger Logger = LogManager.CreateLogger();

        // Building this loads every item prototype, so do it once, on the first search.
        private static readonly Lazy<Catalog> LazyCatalog = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

        public readonly record struct Entry(PrototypeId ProtoRef, string Name, string Category, string Path, string SearchText);

        private sealed record Catalog(Entry[] Entries, HashSet<PrototypeId> ProtoRefs);

        public static bool Contains(PrototypeId itemProtoRef)
        {
            return LazyCatalog.Value.ProtoRefs.Contains(itemProtoRef);
        }

        /// <summary>
        /// Returns up to <see cref="MaxSearchResults"/> items whose name, prototype path or category contains every word of the query,
        /// best matches first.
        /// </summary>
        public static List<Entry> Search(string query, out bool truncated)
        {
            List<Entry> results = new();
            truncated = false;

            string phrase = Normalize(query);
            string[] words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return results;

            foreach (Entry entry in LazyCatalog.Value.Entries)
            {
                bool matches = true;
                foreach (string word in words)
                {
                    if (entry.SearchText.Contains(word, StringComparison.Ordinal) == false)
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                    results.Add(entry);
            }

            results.Sort((a, b) =>
            {
                int rank = Rank(a, phrase).CompareTo(Rank(b, phrase));
                if (rank != 0) return rank;

                int name = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (name != 0) return name;

                return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });

            if (results.Count > MaxSearchResults)
            {
                results.RemoveRange(MaxSearchResults, results.Count - MaxSearchResults);
                truncated = true;
            }

            return results;
        }

        private static int Rank(in Entry entry, string phrase)
        {
            string name = Normalize(entry.Name);
            if (name == phrase) return 0;
            if (name.StartsWith(phrase, StringComparison.Ordinal)) return 1;
            if (name.Contains(phrase, StringComparison.Ordinal)) return 2;
            return 3;
        }

        private static Catalog Build()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Locale locale = LocaleManager.Instance.CurrentLocale;

            List<Entry> entries = new();
            HashSet<PrototypeId> protoRefs = new();

            foreach (PrototypeId itemProtoRef in GameDatabase.DataDirectory.IteratePrototypesInHierarchy<ItemPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
            {
                // Entity/Items/<Category>/...
                string path = GameDatabase.GetPrototypeName(itemProtoRef).Replace('\\', '/');
                string[] segments = path.Split('/');
                string category = segments.Length > 3 && segments[0] == "Entity" && segments[1] == "Items" ? segments[2] : "Other";
                if (category == "Test")
                    continue;

                ItemPrototype itemProto = itemProtoRef.As<ItemPrototype>();
                if (itemProto == null)
                    continue;

                string protoName = GameDatabase.GetFormattedPrototypeName(itemProtoRef);
                string name = StripMarkup(locale?.GetLocaleString(itemProto.DisplayName));
                if (name.Length == 0)
                    name = SplitWords(protoName);    // Locale files aren't loaded ("AncientForgottenDevice" -> "Ancient Forgotten Device")

                // Several items can share a name (one per hero, giftbox tier, etc.), so the page also shows where each one lives:
                // "Entity/Items/CharacterTokens/Prototypes/Shipping/TokenIronMan.prototype" -> "Character Tokens / Shipping / TokenIronMan"
                IEnumerable<string> folders = segments.Skip(2).SkipLast(1).Where(folder => folder != "Prototypes").Select(SplitWords);
                string displayPath = string.Join(" / ", folders.Append(protoName));

                category = SplitWords(category);
                string searchText = Normalize($"{name} {SplitWords(protoName)} {protoName} {category}");

                entries.Add(new(itemProtoRef, name, category, displayPath, searchText));
                protoRefs.Add(itemProtoRef);
            }

            Logger.Info($"Built the free item catalog with {entries.Count} items in {stopwatch.ElapsedMilliseconds} ms");
            return new(entries.ToArray(), protoRefs);
        }

        private static string SplitWords(string name)
        {
            return Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Za-z])(?=[A-Z][a-z])", " ");
        }

        /// <summary>
        /// Removes the client's text markup from a locale string: "#Relics#$currencyamount:0$ Cosmic Worldstones#/Relics#" -> "Cosmic Worldstones".
        /// </summary>
        private static string StripMarkup(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            text = Regex.Replace(text, @"#/?[A-Za-z0-9_]+#|\$[^$\s]*\$", string.Empty);
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static string Normalize(string text)
        {
            return Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();
        }
    }
}
