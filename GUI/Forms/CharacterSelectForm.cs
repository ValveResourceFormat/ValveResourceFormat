using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Types.GLViewers;
using GUI.Utils;

namespace GUI.Forms
{
    /// <summary>
    /// Picks a hero and the cosmetic item it wears in each loadout slot, with a preview of the result, for
    /// <see cref="CharacterAssetsExporter"/>.
    /// </summary>
    partial class CharacterSelectForm : ThemedForm
    {
        private const string PersonaSelectorSlot = "persona_selector";

        // Remembered for the next time the dialog opens
        private static string? lastHeroName;
        private static CharacterExportOptions? lastOptions;

        private readonly ItemsGameCatalog catalog;
        private readonly VrfGuiContext guiContext;
        private readonly List<(HeroSlot Slot, ComboBox ComboBox, ComboBox StyleComboBox)> slotRows = [];
        private GLCharacterPreviewViewer? previewViewer;
        private int heroIndex = -1;
        private bool updatingSelection;
        private bool heroChangedSincePreview = true;
        private bool previewLoading;

        public HeroDefinition? SelectedHero => heroIndex >= 0 ? catalog.Heroes[heroIndex] : null;

        public CharacterExportOptions Options => new()
        {
            HeroModel = heroModelCheckBox.Checked,
            ItemModels = itemModelsCheckBox.Checked,
            ItemParticles = itemParticlesCheckBox.Checked,
            HeroParticles = heroParticlesCheckBox.Checked,
            ItemSounds = itemSoundsCheckBox.Checked,
            HeroSounds = heroSoundsCheckBox.Checked,
            HeroVoice = heroVoiceCheckBox.Checked,
            IncludeAudio = includeAudioCheckBox.Checked,
            Icons = iconsCheckBox.Checked,
            ReplaceDefaults = replaceDefaultsCheckBox.Checked,
            ReplaceSharedParticles = replaceSharedParticlesCheckBox.Checked,
        };

        public CharacterSelectForm(ItemsGameCatalog catalog, VrfGuiContext guiContext)
        {
            this.catalog = catalog;
            this.guiContext = guiContext;

            InitializeComponent();

            toolTip.SetToolTip(heroParticlesCheckBox, "Every particle in the hero's particle folder, which covers the effects of its abilities");
            toolTip.SetToolTip(iconsCheckBox, "Hero portraits, ability icons and item icons from panorama/images");
            toolTip.SetToolTip(heroSoundsCheckBox, "The hero's game_sounds file, which points at the sounds in the game");
            toolTip.SetToolTip(heroVoiceCheckBox, "The hero's game_sounds_vo file, which points at the voice lines in the game");
            toolTip.SetToolTip(itemSoundsCheckBox, "The files the sound events the items swap in are defined in");
            toolTip.SetToolTip(includeAudioCheckBox, "Also export every sound the exported sound events play, which is most of the export's size");
            toolTip.SetToolTip(replaceDefaultsCheckBox,
                "Write the chosen look over the hero's default assets, so it shows without the items being equipped:\n" +
                "the arcana or persona model as the hero's model, chosen items over the default items' models,\n" +
                "particles the items swap in over the ones they replace, and particles items create added to their models");
            toolTip.SetToolTip(replaceSharedParticlesCheckBox, "Also replace particles every hero uses, like the blink dagger, stun and status effects");

            if (lastOptions != null)
            {
                ApplyOptions(lastOptions);
            }

            replaceSharedParticlesCheckBox.Enabled = replaceDefaultsCheckBox.Checked;

            var searchNames = new AutoCompleteStringCollection();
            searchNames.AddRange([.. catalog.Heroes.Select(static hero => hero.DisplayName)]);
            heroSearchTextBox.AutoCompleteCustomSource = searchNames;
            heroSearchTextBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            heroSearchTextBox.AutoCompleteSource = AutoCompleteSource.CustomSource;

            var lastHeroIndex = FindHero(hero => hero.Name.Equals(lastHeroName, StringComparison.OrdinalIgnoreCase));
            SelectHero(Math.Max(lastHeroIndex, 0));
        }

        /// <summary>
        /// The item chosen in every slot and its style, skipping slots left empty.
        /// </summary>
        public List<EquippedItem> GetEquippedItems()
        {
            var items = new List<EquippedItem>();

            foreach (var (_, comboBox, styleComboBox) in slotRows)
            {
                if (GetItem(comboBox) is { } item)
                {
                    items.Add(new EquippedItem(item, (styleComboBox.SelectedItem as ItemStyle)?.Index ?? 0));
                }
            }

            return items;
        }

        public CharacterLoadout CreateLoadout()
            => CharacterLoadout.Create(catalog, SelectedHero ?? throw new InvalidOperationException("No hero is selected"), GetEquippedItems());

        private void ApplyOptions(CharacterExportOptions options)
        {
            heroModelCheckBox.Checked = options.HeroModel;
            itemModelsCheckBox.Checked = options.ItemModels;
            itemParticlesCheckBox.Checked = options.ItemParticles;
            heroParticlesCheckBox.Checked = options.HeroParticles;
            itemSoundsCheckBox.Checked = options.ItemSounds;
            heroSoundsCheckBox.Checked = options.HeroSounds;
            heroVoiceCheckBox.Checked = options.HeroVoice;
            includeAudioCheckBox.Checked = options.IncludeAudio;
            iconsCheckBox.Checked = options.Icons;
            replaceDefaultsCheckBox.Checked = options.ReplaceDefaults;
            replaceSharedParticlesCheckBox.Checked = options.ReplaceSharedParticles;
        }

        private void ReplaceDefaultsCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            replaceSharedParticlesCheckBox.Enabled = replaceDefaultsCheckBox.Checked;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            _ = LoadPreviewAsync();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            // The render loop only resumes on its own when the main window is activated, painting reattaches it
            previewViewer?.GLControl?.Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            previewTimer.Stop();
            previewViewer?.Dispose();
            previewViewer = null;

            if (SelectedHero != null)
            {
                lastHeroName = SelectedHero.Name;
            }

            lastOptions = Options;

            base.OnFormClosed(e);
        }

        private async Task LoadPreviewAsync()
        {
            GLCharacterPreviewViewer? viewer = null;
            previewLoading = true;

            try
            {
                viewer = new GLCharacterPreviewViewer(guiContext, guiContext.CreateRendererContext());
                viewer.SetModels(GetPreviewModels(), frameCamera: true);
                heroChangedSincePreview = false;

                // Loads models and textures, which would otherwise freeze the dialog
                await Task.Run(viewer.InitializeLoad).ConfigureAwait(true);

                if (IsDisposed)
                {
                    viewer.Dispose();
                    return;
                }

                var control = viewer.InitializeUiControls(isPreview: true);
                control.Dock = DockStyle.Fill;

                previewPanel.Controls.Remove(previewStatusLabel);
                previewPanel.Controls.Add(control);

                previewViewer = viewer;
                viewer = null;

                // The selection may have changed while the preview was loading
                SchedulePreviewUpdate();
            }
            catch (Exception ex)
            {
                viewer?.Dispose();

                Log.Error(nameof(CharacterSelectForm), $"Failed to load the character preview: {ex}");

                if (!IsDisposed)
                {
                    previewStatusLabel.Text = $"Preview is not available: {ex.Message}";
                }
            }
            finally
            {
                previewLoading = false;
            }
        }

        private void SelectHero(int index)
        {
            if (catalog.Heroes.Count == 0)
            {
                return;
            }

            heroIndex = (index % catalog.Heroes.Count + catalog.Heroes.Count) % catalog.Heroes.Count;

            var hero = catalog.Heroes[heroIndex];
            heroNameLabel.Text = hero.DisplayName;

            updatingSelection = true;

            try
            {
                BuildSlotRows(hero);
                BuildItemSets(hero);
                ResetLoadout(persona: false);
            }
            finally
            {
                updatingSelection = false;
            }

            heroChangedSincePreview = true;

            UpdateSummary();
            SchedulePreviewUpdate();
        }

        private void BuildSlotRows(HeroDefinition hero)
        {
            slotsTable.SuspendLayout();

            foreach (var control in slotsTable.Controls.Cast<Control>().ToList())
            {
                control.Dispose();
            }

            slotsTable.Controls.Clear();
            slotsTable.RowStyles.Clear();
            slotsTable.RowCount = 0;
            slotRows.Clear();

            var slots = catalog.GetSlots(hero)
                .Select(slot => (Slot: slot, Items: catalog.GetItems(hero, slot.Name), Text: GetSlotText(slot)))
                .Where(static slot => slot.Items.Count > 0)
                .ToList();

            // Some heroes name two slots the same, e.g. the heads of both of Alchemist's riders
            var duplicateSlotTexts = slots
                .GroupBy(static slot => slot.Text, StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (slot, items, text) in slots)
            {
                var label = new Label
                {
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    MaximumSize = new System.Drawing.Size(this.AdjustForDPI(150), 0),
                    Text = duplicateSlotTexts.Contains(text) ? $"{text} ({slot.Name})" : text,
                    Margin = new Padding(3, 6, 6, 3),
                };

                toolTip.SetToolTip(label, slot.Name);

                var comboBox = new ThemedComboBox
                {
                    Dock = DockStyle.Fill,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    MaxDropDownItems = 20,
                    Tag = slot,
                };

                comboBox.Items.Add(ItemChoice.None);

                // Items from different years can share a name, the def index tells them apart
                var duplicateNames = items
                    .GroupBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Where(static group => group.Count() > 1)
                    .Select(static group => group.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var item in items)
                {
                    comboBox.Items.Add(new ItemChoice(item, duplicateNames.Contains(item.Name)));
                }

                comboBox.SelectedIndex = 0;
                comboBox.SelectedIndexChanged += SlotComboBox_SelectedIndexChanged;

                var styleComboBox = new ThemedComboBox
                {
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    DisplayMember = nameof(ItemStyle.Name),
                    Width = this.AdjustForDPI(110),
                    DropDownWidth = this.AdjustForDPI(240),
                    Visible = false,
                };

                styleComboBox.SelectedIndexChanged += StyleComboBox_SelectedIndexChanged;
                toolTip.SetToolTip(styleComboBox, "Item style");

                var row = slotsTable.RowCount++;
                slotsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                slotsTable.Controls.Add(label, 0, row);
                slotsTable.Controls.Add(comboBox, 1, row);
                slotsTable.Controls.Add(styleComboBox, 2, row);

                slotRows.Add((slot, comboBox, styleComboBox));
            }

            // Keeps the last row from stretching when there are only a few slots
            slotsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            slotsTable.RowCount++;

            Themer.ThemeControl(slotsTable);
            slotsTable.ResumeLayout(true);
        }

        private void BuildItemSets(HeroDefinition hero)
        {
            itemSetComboBox.BeginUpdate();
            itemSetComboBox.Items.Clear();
            itemSetComboBox.Items.Add("Default items");

            foreach (var set in catalog.GetSets(hero))
            {
                itemSetComboBox.Items.Add(set);
            }

            itemSetComboBox.SelectedIndex = 0;
            itemSetComboBox.EndUpdate();
        }

        /// <summary>
        /// Equips the default item in every slot the hero uses in its normal form or as its persona, and nothing in the
        /// slots of the other form.
        /// </summary>
        /// <param name="persona">Whether to equip the hero's persona.</param>
        /// <param name="keepPersonaSelector">Leave the persona selector as the user picked it.</param>
        private void ResetLoadout(bool persona, bool keepPersonaSelector = false)
        {
            foreach (var (slot, comboBox, _) in slotRows)
            {
                if (slot.Name.Equals(PersonaSelectorSlot, StringComparison.OrdinalIgnoreCase))
                {
                    if (!keepPersonaSelector)
                    {
                        SelectItem(comboBox, item => persona ? IsPersonaItem(item) : item.IsDefault);
                    }
                }
                else if (IsSharedSlot(slot.Name) || IsPersonaSlot(slot.Name) == persona)
                {
                    SelectItem(comboBox, static item => item.IsDefault);
                }
                else
                {
                    comboBox.SelectedIndex = 0;
                }
            }
        }

        /// <summary>
        /// Persona slots often reuse the text of the slot they mirror, e.g. both are "Ambient Effects".
        /// </summary>
        private static string GetSlotText(HeroSlot slot)
            => IsPersonaSlot(slot.Name) && !slot.DisplayName.Contains("persona", StringComparison.OrdinalIgnoreCase)
                ? $"{slot.DisplayName} (Persona)"
                : slot.DisplayName;

        /// <summary>
        /// Slots of the hero's persona, which only apply while the persona is equipped.
        /// </summary>
        private static bool IsPersonaSlot(string slotName) => PersonaSlotRegex().IsMatch(slotName);

        /// <summary>
        /// Slots that apply to both the hero and its persona.
        /// </summary>
        private static bool IsSharedSlot(string slotName) => slotName.StartsWith("ability_effects", StringComparison.OrdinalIgnoreCase);

        private static bool IsPersonaItem(EconItem? item) => item?.AssetModifiers.Any(static modifier => modifier.Type == "persona") == true;

        private static EconItem? GetItem(ComboBox comboBox) => (comboBox.SelectedItem as ItemChoice)?.Item;

        /// <summary>
        /// Selects the first item that matches, or nothing when none does.
        /// </summary>
        private static void SelectItem(ComboBox comboBox, Func<EconItem, bool> predicate)
        {
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ItemChoice { Item: { } item } && predicate(item))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            comboBox.SelectedIndex = 0;
        }

        private void ItemSetComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (updatingSelection || itemSetComboBox.SelectedIndex < 0)
            {
                return;
            }

            updatingSelection = true;

            try
            {
                var set = itemSetComboBox.SelectedItem as ItemSet;

                // A set made for the persona only makes sense with the persona equipped
                ResetLoadout(persona: set?.Items.Any(static item => IsPersonaSlot(item.Slot)) == true);

                if (set != null)
                {
                    foreach (var item in set.Items)
                    {
                        var (_, comboBox, _) = slotRows.FirstOrDefault(row => row.Slot.Name.Equals(item.Slot, StringComparison.OrdinalIgnoreCase));

                        if (comboBox != null)
                        {
                            SelectItem(comboBox, candidate => candidate == item);
                        }
                    }
                }
            }
            finally
            {
                updatingSelection = false;
            }

            UpdateSummary();
            SchedulePreviewUpdate();
        }

        private void SlotComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (sender is ComboBox changedComboBox)
            {
                UpdateStyles(changedComboBox);
            }

            if (updatingSelection)
            {
                return;
            }

            updatingSelection = true;

            try
            {
                // Switching between the hero and its persona swaps out the whole loadout
                if (sender is ComboBox { Tag: HeroSlot slot } comboBox && slot.Name.Equals(PersonaSelectorSlot, StringComparison.OrdinalIgnoreCase))
                {
                    ResetLoadout(IsPersonaItem(GetItem(comboBox)), keepPersonaSelector: true);
                }

                // A hand picked item means the loadout no longer is the set that was chosen
                itemSetComboBox.SelectedIndex = -1;
            }
            finally
            {
                updatingSelection = false;
            }

            UpdateSummary();
            SchedulePreviewUpdate();
        }

        /// <summary>
        /// Offers the styles of the item the slot's combo box now shows, starting from the first one.
        /// </summary>
        private void UpdateStyles(ComboBox comboBox)
        {
            var (_, _, styleComboBox) = slotRows.FirstOrDefault(row => row.ComboBox == comboBox);

            if (styleComboBox == null)
            {
                return;
            }

            var styles = GetItem(comboBox)?.Styles ?? [];

            styleComboBox.BeginUpdate();
            styleComboBox.Items.Clear();

            foreach (var style in styles)
            {
                styleComboBox.Items.Add(style);
            }

            styleComboBox.SelectedIndex = styles.Count > 0 ? 0 : -1;
            styleComboBox.Visible = styles.Count > 1;
            styleComboBox.EndUpdate();
        }

        private void StyleComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            SchedulePreviewUpdate();
        }

        private void PreviousHeroButton_Click(object? sender, EventArgs e) => SelectHero(heroIndex - 1);

        private void NextHeroButton_Click(object? sender, EventArgs e) => SelectHero(heroIndex + 1);

        private void HeroSearchTextBox_TextChanged(object? sender, EventArgs e)
        {
            var text = heroSearchTextBox.Text.Trim();

            if (text.Length == 0)
            {
                return;
            }

            var index = FindHero(hero => hero.DisplayName.StartsWith(text, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                index = FindHero(hero => hero.ShortName.StartsWith(text, StringComparison.OrdinalIgnoreCase));
            }

            if (index < 0)
            {
                index = FindHero(hero => hero.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase));
            }

            if (index >= 0 && index != heroIndex)
            {
                SelectHero(index);
            }
        }

        private int FindHero(Func<HeroDefinition, bool> predicate)
        {
            var heroes = catalog.Heroes;

            for (var i = 0; i < heroes.Count; i++)
            {
                if (predicate(heroes[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private void ExportButton_Click(object? sender, EventArgs e)
        {
            if (SelectedHero == null)
            {
                return;
            }

            DialogResult = DialogResult.OK;
        }

        private void UpdateSummary()
        {
            var count = slotRows.Count(static row => GetItem(row.ComboBox) != null);
            summaryLabel.Text = $"{count} item{(count == 1 ? "" : "s")} equipped";
        }

        private void SchedulePreviewUpdate()
        {
            previewTimer.Stop();
            previewTimer.Start();
        }

        private void PreviewTimer_Tick(object? sender, EventArgs e)
        {
            previewTimer.Stop();

            // Picked up once the preview finishes loading
            if (previewViewer == null || previewLoading)
            {
                return;
            }

            previewViewer.SetModels(GetPreviewModels(), heroChangedSincePreview);
            heroChangedSincePreview = false;
        }

        /// <summary>
        /// The hero's model, or the one an equipped item swaps it for, followed by the models the equipped items show.
        /// </summary>
        private List<PreviewModel> GetPreviewModels()
        {
            if (SelectedHero == null)
            {
                return [];
            }

            var loadout = CreateLoadout();
            var models = new List<PreviewModel>();

            if (loadout.HeroModel != null)
            {
                models.Add(new PreviewModel(loadout.HeroModel, loadout.HeroSkin));
            }

            foreach (var item in loadout.Items)
            {
                if (loadout.GetItemModel(item) is { } model && !models.Any(existing => CharacterLoadout.IsSamePath(existing.Path, model)))
                {
                    models.Add(new PreviewModel(model, item.Skin));
                }
            }

            return models;
        }

        [GeneratedRegex(@"_persona_\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex PersonaSlotRegex();

        private sealed class ItemChoice
        {
            public static readonly ItemChoice None = new(null, showDefIndex: false);

            public EconItem? Item { get; }

            private readonly string text;

            public ItemChoice(EconItem? item, bool showDefIndex)
            {
                Item = item;

                if (item == null)
                {
                    text = "(none)";
                    return;
                }

                text = item.Name;

                if (item.IsDefault)
                {
                    text += " (default)";
                }
                else if (item.Rarity is { Length: > 0 } rarity && !rarity.Equals("common", StringComparison.OrdinalIgnoreCase))
                {
                    text += $" [{rarity}]";
                }

                if (showDefIndex)
                {
                    text += $" #{item.DefIndex}";
                }
            }

            public override string ToString() => text;
        }
    }
}
