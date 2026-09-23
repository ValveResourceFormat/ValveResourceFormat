using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using GUI.Utils;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// One entry of an item's "visuals" block, describing an asset the item adds or swaps out.
    /// </summary>
    /// <param name="Type">What kind of swap this is, e.g. "particle", "entity_model" or "sound".</param>
    /// <param name="Asset">What is being replaced (a path, an entity class, an ability or a sound event).</param>
    /// <param name="Modifier">What it is replaced with.</param>
    /// <param name="Style">The item style this applies to, or null when it applies to every style.</param>
    sealed record AssetModifier(string Type, string? Asset, string? Modifier, int? Style);

    /// <summary>
    /// A cosmetic item from items_game.txt that can be equipped on a hero.
    /// </summary>
    sealed class EconItem
    {
        public required string DefIndex { get; init; }
        public required string Name { get; init; }
        public required string Slot { get; init; }
        public string? ModelPlayer { get; init; }
        public string? ImageInventory { get; init; }
        public string? Rarity { get; init; }

        /// <summary>Whether this is the item a hero wears in this slot when nothing else is equipped.</summary>
        public bool IsDefault { get; init; }

        public int StyleCount { get; init; }
        public List<string> Heroes { get; } = [];
        public List<AssetModifier> AssetModifiers { get; } = [];

        public override string ToString() => Name;
    }

    /// <summary>
    /// A loadout slot as declared in a hero's "ItemSlots".
    /// </summary>
    sealed record HeroSlot(int Index, string Name, string DisplayName);

    /// <summary>
    /// A playable hero as declared in the npc hero scripts.
    /// </summary>
    sealed class HeroDefinition
    {
        public const string NamePrefix = "npc_dota_hero_";

        /// <summary>The entity name, e.g. "npc_dota_hero_earthshaker".</summary>
        public required string Name { get; init; }

        public required string DisplayName { get; init; }
        public string? Model { get; init; }
        public string? GameSoundsFile { get; init; }
        public string? VoiceFile { get; init; }
        public string? ParticleFolder { get; init; }
        public List<string> Abilities { get; } = [];
        public List<HeroSlot> Slots { get; } = [];

        /// <summary>The entity name without the "npc_dota_hero_" prefix, e.g. "earthshaker".</summary>
        public string ShortName => Name.StartsWith(NamePrefix, StringComparison.Ordinal) ? Name[NamePrefix.Length..] : Name;

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// A named collection of items that are meant to be equipped together.
    /// </summary>
    sealed class ItemSet
    {
        public required string Key { get; init; }
        public required string DisplayName { get; init; }
        public List<EconItem> Items { get; } = [];

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Heroes and the cosmetic items they can equip, read from a Dota 2 package's items_game.txt and npc hero scripts.
    /// </summary>
    sealed class ItemsGameCatalog
    {
        public const string ItemsGamePath = "scripts/items/items_game.txt";
        public const string HeroesPath = "scripts/npc/npc_heroes.txt";

        private static readonly string[] LocalizationPaths =
        [
            "resource/localization/dota_english.txt",
            "resource/localization/items_english.txt",
        ];

        private readonly List<HeroDefinition> heroes;
        private readonly Dictionary<string, List<EconItem>> itemsByHero;
        private readonly Dictionary<string, List<ItemSet>> setsByHero;

        public IReadOnlyList<HeroDefinition> Heroes => heroes;

        private ItemsGameCatalog(List<HeroDefinition> heroes, Dictionary<string, List<EconItem>> itemsByHero, Dictionary<string, List<ItemSet>> setsByHero)
        {
            this.heroes = heroes;
            this.itemsByHero = itemsByHero;
            this.setsByHero = setsByHero;
        }

        /// <summary>
        /// Whether the package holds the scripts a catalog is read from.
        /// </summary>
        public static bool IsAvailable(Package package)
            => package.FindEntry(ItemsGamePath) != null && package.FindEntry(HeroesPath) != null;

        /// <summary>
        /// Items any hero can equip in the given slot, with the default item first.
        /// </summary>
        public IReadOnlyList<EconItem> GetItems(HeroDefinition hero, string slot)
        {
            if (!itemsByHero.TryGetValue(hero.Name, out var items))
            {
                return [];
            }

            return [.. items
                .Where(item => item.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(static item => item.IsDefault)
                .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>
        /// The hero's own loadout slots, followed by any other slot the hero's items are made for.
        /// </summary>
        public IReadOnlyList<HeroSlot> GetSlots(HeroDefinition hero)
        {
            var slots = hero.Slots.OrderBy(static slot => slot.Index).ToList();

            if (itemsByHero.TryGetValue(hero.Name, out var items))
            {
                var extraSlots = items
                    .Select(static item => item.Slot)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(slotName => !slots.Any(slot => slot.Name.Equals(slotName, StringComparison.OrdinalIgnoreCase)))
                    .Order(StringComparer.OrdinalIgnoreCase);

                var index = slots.Count > 0 ? slots.Max(static slot => slot.Index) : 0;

                foreach (var slotName in extraSlots)
                {
                    slots.Add(new HeroSlot(++index, slotName, slotName));
                }
            }

            return slots;
        }

        /// <summary>
        /// The item the hero wears in the slot when nothing else is equipped, if the slot has one.
        /// </summary>
        public EconItem? GetDefaultItem(HeroDefinition hero, string slot)
            => GetItems(hero, slot).FirstOrDefault(static item => item.IsDefault);

        public IReadOnlyList<ItemSet> GetSets(HeroDefinition hero)
            => setsByHero.TryGetValue(hero.Name, out var sets) ? sets : [];

        /// <summary>
        /// Reads the catalog from the package. items_game.txt is read on every call, so it always reflects the
        /// package as it is now.
        /// </summary>
        public static ItemsGameCatalog Load(Package package, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            progress?.Report("Reading localization...");
            var localization = LoadLocalization(package);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Reading hero scripts...");
            var heroes = LoadHeroes(package, localization);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Reading {ItemsGamePath}...");
            var itemsGame = ReadKeyValues(package, ItemsGamePath)
                ?? throw new FileNotFoundException($"\"{ItemsGamePath}\" was not found in the package");

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Collecting items...");

            var heroNames = heroes.Select(static hero => hero.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var prefabs = itemsGame.GetSubCollection("prefabs");
            var itemsByHero = new Dictionary<string, List<EconItem>>(StringComparer.OrdinalIgnoreCase);
            var itemsByName = new Dictionary<string, EconItem>(StringComparer.OrdinalIgnoreCase);

            if (itemsGame.GetSubCollection("items") is { } items)
            {
                foreach (var (defIndex, itemData) in items)
                {
                    if (itemData.ValueType != KVValueType.Collection)
                    {
                        continue;
                    }

                    var item = ReadItem(defIndex, itemData, prefabs, heroNames);

                    if (item == null)
                    {
                        continue;
                    }

                    itemsByName.TryAdd(item.Name, item);

                    foreach (var hero in item.Heroes)
                    {
                        if (!itemsByHero.TryGetValue(hero, out var heroItems))
                        {
                            heroItems = [];
                            itemsByHero.Add(hero, heroItems);
                        }

                        heroItems.Add(item);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Collecting item sets...");

            var setsByHero = ReadItemSets(itemsGame, itemsByName, localization);

            return new ItemsGameCatalog(heroes, itemsByHero, setsByHero);
        }

        private static EconItem? ReadItem(string defIndex, KVObject itemData, KVObject? prefabs, HashSet<string> heroNames)
        {
            var usedByHeroes = itemData.GetSubCollection("used_by_heroes");

            if (usedByHeroes == null || usedByHeroes.ValueType != KVValueType.Collection)
            {
                return null;
            }

            var name = GetValue(itemData, "name");
            var prefab = GetValue(itemData, "prefab");
            var slot = GetValue(itemData, "item_slot") ?? GetPrefabValue(prefabs, prefab, "item_slot", 0);

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(slot) || slot.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var isDefault = (GetValue(itemData, "baseitem") ?? GetPrefabValue(prefabs, prefab, "baseitem", 0)) == "1";
            var visuals = itemData.GetSubCollection("visuals");

            var item = new EconItem
            {
                DefIndex = defIndex,
                Name = name,
                Slot = slot,
                ModelPlayer = NullIfEmpty(GetValue(itemData, "model_player")),
                ImageInventory = NullIfEmpty(GetValue(itemData, "image_inventory")),
                Rarity = GetValue(itemData, "item_rarity") ?? GetPrefabValue(prefabs, prefab, "item_rarity", 0),
                IsDefault = isDefault,
                StyleCount = visuals?.GetSubCollection("styles") is { ValueType: KVValueType.Collection } styles ? styles.Count : 0,
            };

            foreach (var (heroName, value) in usedByHeroes)
            {
                // "0" marks a hero the item was taken away from
                if (heroNames.Contains(heroName) && value.ToString() != "0")
                {
                    item.Heroes.Add(heroName);
                }
            }

            if (item.Heroes.Count == 0)
            {
                return null;
            }

            if (visuals?.ValueType == KVValueType.Collection)
            {
                foreach (var (key, modifier) in visuals)
                {
                    // Usually a repeated "asset_modifier" key, but numbered keys ("asset_modifier0") are used too
                    if (!key.StartsWith("asset_modifier", StringComparison.OrdinalIgnoreCase) || modifier.ValueType != KVValueType.Collection)
                    {
                        continue;
                    }

                    var type = GetValue(modifier, "type");

                    if (string.IsNullOrEmpty(type))
                    {
                        continue;
                    }

                    int? style = int.TryParse(GetValue(modifier, "style"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStyle)
                        ? parsedStyle
                        : null;

                    item.AssetModifiers.Add(new AssetModifier(
                        type,
                        NullIfEmpty(GetValue(modifier, "asset")),
                        NullIfEmpty(GetValue(modifier, "modifier")),
                        style));
                }
            }

            return item;
        }

        /// <summary>
        /// Looks a key up through an item's prefabs. An item may name several prefabs, and prefabs may have prefabs themselves.
        /// </summary>
        private static string? GetPrefabValue(KVObject? prefabs, string? prefabNames, string key, int depth)
        {
            if (prefabs == null || string.IsNullOrEmpty(prefabNames) || depth > 8)
            {
                return null;
            }

            foreach (var prefabName in prefabNames.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (prefabs.GetSubCollection(prefabName) is not { ValueType: KVValueType.Collection } prefab)
                {
                    continue;
                }

                var value = GetValue(prefab, key)
                    ?? GetPrefabValue(prefabs, GetValue(prefab, "prefab"), key, depth + 1);

                if (value != null)
                {
                    return value;
                }
            }

            return null;
        }

        private static Dictionary<string, List<ItemSet>> ReadItemSets(KVObject itemsGame, Dictionary<string, EconItem> itemsByName, Dictionary<string, string> localization)
        {
            var setsByHero = new Dictionary<string, List<ItemSet>>(StringComparer.OrdinalIgnoreCase);

            if (itemsGame.GetSubCollection("item_sets") is not { ValueType: KVValueType.Collection } itemSets)
            {
                return setsByHero;
            }

            foreach (var (key, setData) in itemSets)
            {
                if (setData.ValueType != KVValueType.Collection || setData.GetSubCollection("items") is not { ValueType: KVValueType.Collection } setItems)
                {
                    continue;
                }

                var set = new ItemSet
                {
                    Key = key,
                    DisplayName = Localize(localization, GetValue(setData, "name"))
                        ?? NullIfEmpty(GetValue(setData, "store_bundle"))
                        ?? key,
                };

                foreach (var (itemName, _) in setItems)
                {
                    if (itemsByName.TryGetValue(itemName, out var item))
                    {
                        set.Items.Add(item);
                    }
                }

                if (set.Items.Count == 0)
                {
                    continue;
                }

                foreach (var hero in set.Items.SelectMany(static item => item.Heroes).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!setsByHero.TryGetValue(hero, out var heroSets))
                    {
                        heroSets = [];
                        setsByHero.Add(hero, heroSets);
                    }

                    heroSets.Add(set);
                }
            }

            foreach (var heroSets in setsByHero.Values)
            {
                heroSets.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName));
            }

            return setsByHero;
        }

        private static List<HeroDefinition> LoadHeroes(Package package, Dictionary<string, string> localization)
        {
            var options = new KVSerializerOptions
            {
                FileLoader = new PackageIncludeLoader(package, Path.GetDirectoryName(HeroesPath)!.Replace('\\', '/')),
            };

            var root = ReadKeyValues(package, HeroesPath, options)
                ?? throw new FileNotFoundException($"\"{HeroesPath}\" was not found in the package");

            var heroes = new List<HeroDefinition>();

            foreach (var (name, heroData) in root)
            {
                if (heroData.ValueType != KVValueType.Collection
                    || !name.StartsWith(HeroDefinition.NamePrefix, StringComparison.OrdinalIgnoreCase)
                    || name.Equals("npc_dota_hero_base", StringComparison.OrdinalIgnoreCase)
                    || GetValue(heroData, "Enabled") == "0"
                    || string.IsNullOrEmpty(GetValue(heroData, "Model")))
                {
                    continue;
                }

                var hero = new HeroDefinition
                {
                    Name = name,
                    DisplayName = Localize(localization, name)
                        ?? NullIfEmpty(GetValue(heroData, "workshop_guide_name"))
                        ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name[HeroDefinition.NamePrefix.Length..].Replace('_', ' ')),
                    Model = GetValue(heroData, "Model"),
                    GameSoundsFile = NullIfEmpty(GetValue(heroData, "GameSoundsFile")),
                    VoiceFile = NullIfEmpty(GetValue(heroData, "VoiceFile")),
                    ParticleFolder = NullIfEmpty(GetValue(heroData, "particle_folder")),
                };

                foreach (var (key, value) in heroData)
                {
                    if (!key.StartsWith("Ability", StringComparison.Ordinal) || !int.TryParse(key.AsSpan("Ability".Length), CultureInfo.InvariantCulture, out _))
                    {
                        continue;
                    }

                    var ability = value.ToString();

                    if (!string.IsNullOrEmpty(ability)
                        && ability != "generic_hidden"
                        && !ability.StartsWith("special_bonus_", StringComparison.Ordinal)
                        && !hero.Abilities.Contains(ability))
                    {
                        hero.Abilities.Add(ability);
                    }
                }

                if (heroData.GetSubCollection("ItemSlots") is { ValueType: KVValueType.Collection } itemSlots)
                {
                    foreach (var (_, slotData) in itemSlots)
                    {
                        if (slotData.ValueType != KVValueType.Collection)
                        {
                            continue;
                        }

                        var slotName = GetValue(slotData, "SlotName");

                        if (string.IsNullOrEmpty(slotName) || hero.Slots.Any(slot => slot.Name.Equals(slotName, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        var slotIndex = int.TryParse(GetValue(slotData, "SlotIndex"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                            ? index
                            : hero.Slots.Count;

                        hero.Slots.Add(new HeroSlot(slotIndex, slotName, Localize(localization, GetValue(slotData, "SlotText")) ?? slotName));
                    }
                }

                heroes.Add(hero);
            }

            heroes.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName));

            return heroes;
        }

        private static Dictionary<string, string> LoadLocalization(Package package)
        {
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in LocalizationPaths)
            {
                KVObject? root;

                try
                {
                    // Localized text quotes html attributes with escaped quotes
                    root = ReadKeyValues(package, path, new KVSerializerOptions { HasEscapeSequences = true });
                }
                catch (Exception e)
                {
                    // Names fall back to the raw keys, which is still usable
                    Log.Warn(nameof(ItemsGameCatalog), $"Failed to read \"{path}\": {e.Message}");
                    continue;
                }

                if (root?.GetSubCollection("Tokens") is not { ValueType: KVValueType.Collection } fileTokens)
                {
                    continue;
                }

                foreach (var (key, value) in fileTokens)
                {
                    if (ToText(value) is { } text)
                    {
                        tokens.TryAdd(key, text);
                    }
                }
            }

            return tokens;
        }

        private static string? Localize(Dictionary<string, string> localization, string? token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            var key = token.StartsWith('#') ? token[1..] : token;

            return localization.TryGetValue(key, out var text) && text.Length > 0 ? text : null;
        }

        private static KVObject? ReadKeyValues(Package package, string path, KVSerializerOptions? options = null)
        {
            var entry = package.FindEntry(path);

            if (entry == null)
            {
                return null;
            }

            using var stream = GameFileLoader.GetPackageEntryStream(package, entry);

            return KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream, options ?? KVSerializerOptions.DefaultOptions);
        }

        /// <summary>
        /// Reads a value as text. The KV1 reader turns numeric looking values into numbers, which the script's own
        /// meaning does not care about, e.g. "baseitem" "1" or "style" "0".
        /// </summary>
        private static string? GetValue(KVObject data, string key)
            => data.TryGetValue(key, out var value) ? ToText(value) : null;

        private static string? ToText(KVObject value)
            => value.ValueType is KVValueType.Collection or KVValueType.Array or KVValueType.Null ? null : value.ToString();

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        /// <summary>
        /// Resolves "#base" includes relative to the folder of the script that declares them.
        /// </summary>
        private sealed class PackageIncludeLoader(Package package, string folder) : IIncludedFileLoader
        {
            public Stream OpenFile(string filePath)
            {
                var path = $"{folder}/{filePath.Replace('\\', '/')}";
                var entry = package.FindEntry(path) ?? throw new FileNotFoundException($"\"{path}\" was not found in the package");

                return GameFileLoader.GetPackageEntryStream(package, entry);
            }
        }
    }
}
