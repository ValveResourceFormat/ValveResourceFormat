using System.Linq;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// An item equipped in one of the hero's slots, in one of its styles.
    /// </summary>
    sealed record EquippedItem(EconItem Item, int Style)
    {
        /// <summary>
        /// The item's asset modifiers that apply in the chosen style.
        /// </summary>
        public IEnumerable<AssetModifier> Modifiers => Item.AssetModifiers.Where(modifier => modifier.Style == null || modifier.Style == Style);

        /// <summary>
        /// The material group the chosen style shows the item's model with.
        /// </summary>
        public int Skin => Item.Styles.FirstOrDefault(style => style.Index == Style)?.Skin ?? 0;

        /// <summary>
        /// The chosen style's name, or null when the item has only the one look.
        /// </summary>
        public string? StyleName => Item.Styles.Count > 1 ? Item.Styles.FirstOrDefault(style => style.Index == Style)?.Name : null;
    }

    /// <summary>
    /// A hero with the items it wears, and what that makes it look like: which model the hero ends up with, and which
    /// model each item shows once other items have swapped it.
    /// </summary>
    sealed class CharacterLoadout
    {
        private readonly Dictionary<string, string> defaultModels;

        public HeroDefinition Hero { get; }
        public IReadOnlyList<EquippedItem> Items { get; }

        private CharacterLoadout(HeroDefinition hero, IReadOnlyList<EquippedItem> items, Dictionary<string, string> defaultModels)
        {
            Hero = hero;
            Items = items;
            this.defaultModels = defaultModels;
        }

        public static CharacterLoadout Create(ItemsGameCatalog catalog, HeroDefinition hero, IReadOnlyList<EquippedItem> items)
        {
            var defaultModels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var slot in catalog.GetSlots(hero))
            {
                if (catalog.GetDefaultItem(hero, slot.Name)?.ModelPlayer is { } model)
                {
                    defaultModels[slot.Name] = model;
                }
            }

            return new CharacterLoadout(hero, items, defaultModels);
        }

        /// <summary>
        /// The model the hero wears in the slot when nothing else is equipped, if it has one.
        /// </summary>
        public string? GetDefaultModel(string slot) => defaultModels.GetValueOrDefault(slot);

        /// <summary>
        /// The item that swaps the hero's own model, such as an arcana or a persona.
        /// </summary>
        public EquippedItem? HeroModelItem => Items.LastOrDefault(item => GetHeroModelSwap(item) != null);

        /// <summary>
        /// The model the hero is shown with.
        /// </summary>
        public string? HeroModel => HeroModelItem is { } item ? GetHeroModelSwap(item) : Hero.Model;

        /// <summary>
        /// The material group of the hero's model, picked by the style of an item that has no model of its own.
        /// </summary>
        public int HeroSkin => Items.LastOrDefault(static item => item.Item.ModelPlayer == null && item.Skin != 0)?.Skin ?? 0;

        /// <summary>
        /// The model an item shows. Items can swap their own model for a style, and other items can swap it for a
        /// version made to fit them, e.g. a refit of a head that would clip with an arcana.
        /// </summary>
        public string? GetItemModel(EquippedItem item)
        {
            var model = item.Item.ModelPlayer;

            if (model == null)
            {
                return null;
            }

            foreach (var modifier in Items.SelectMany(static equipped => equipped.Modifiers))
            {
                if (modifier is { Type: "model", Modifier: not null } && IsSamePath(modifier.Asset, model))
                {
                    return modifier.Modifier;
                }
            }

            return model;
        }

        private string? GetHeroModelSwap(EquippedItem item)
            => item.Modifiers.LastOrDefault(modifier => modifier is { Type: "entity_model", Modifier: not null }
                && Hero.Name.Equals(modifier.Asset, StringComparison.OrdinalIgnoreCase))?.Modifier;

        public static bool IsSamePath(string? a, string? b)
            => a != null && b != null && NormalizePath(a).Equals(NormalizePath(b), StringComparison.OrdinalIgnoreCase);

        public static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');
    }
}
