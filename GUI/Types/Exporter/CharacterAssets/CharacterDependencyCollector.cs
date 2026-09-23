using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// What to export for a character besides the selected items' own models.
    /// </summary>
    sealed class CharacterExportOptions
    {
        /// <summary>The hero's base model from its npc script.</summary>
        public bool HeroModel { get; set; } = true;

        /// <summary>Models the selected items wear or swap in.</summary>
        public bool ItemModels { get; set; } = true;

        /// <summary>Particles the selected items create or swap in.</summary>
        public bool ItemParticles { get; set; } = true;

        /// <summary>Every particle in the hero's particle folder, which covers its abilities.</summary>
        public bool HeroParticles { get; set; }

        /// <summary>Sound events the selected items swap in, with their sounds.</summary>
        public bool ItemSounds { get; set; } = true;

        /// <summary>The hero's game sound events, with their sounds.</summary>
        public bool HeroSounds { get; set; } = true;

        /// <summary>The hero's voice lines.</summary>
        public bool HeroVoice { get; set; } = true;

        /// <summary>Panorama images: the hero portraits, ability icons and item icons.</summary>
        public bool Icons { get; set; } = true;
    }

    /// <summary>
    /// Every file a character export writes, grouped by how it gets decompiled. Paths are package paths of compiled files
    /// (ending in "_c"), except <see cref="RawFiles"/>.
    /// </summary>
    sealed class CharacterExportPlan
    {
        /// <summary>Models, decompiled with the custom model extractor, which also writes their meshes, animations and physics.</summary>
        public List<string> Models { get; } = [];

        /// <summary>Materials, decompiled with the custom material exporter, which also writes their textures.</summary>
        public List<string> Materials { get; } = [];

        /// <summary>Everything else that is compiled, decompiled with the built-in decompiler.</summary>
        public List<string> Resources { get; } = [];

        /// <summary>Files that are not compiled, copied as they are.</summary>
        public List<string> RawFiles { get; } = [];

        /// <summary>Meshes, animations and physics found under the models, written by the model extractor.</summary>
        public int ModelDependencyCount { get; set; }

        /// <summary>Textures only used by the materials, written by the material exporter.</summary>
        public int MaterialTextureCount { get; set; }

        /// <summary>References that could not be found in the game files.</summary>
        public List<string> Missing { get; } = [];
    }

    /// <summary>
    /// Works out which files make up a hero with a set of items: the models, particles, sound events and icons the items
    /// name in items_game.txt, and everything those resources reference in turn.
    /// </summary>
    sealed class CharacterDependencyCollector
    {
        // Written by the model extractor next to the model that uses them
        private static readonly HashSet<string> ModelOwnedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".vmesh", ".vmorf", ".vphys", ".vagrp", ".vanim", ".vmodel", ".vseq", ".vpulse",
        };

        // Resources whose references are not worth opening them for
        private static readonly HashSet<string> LeafExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".vsnd", ".vtex",
        };

        private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".vmdl", ".vpcf", ".vsnap", ".vmat", ".vtex", ".vsndevts", ".vsnd",
        };

        // Sound event files that never hold hero or cosmetic sounds
        private static readonly string[] IgnoredSoundEventFolders =
        [
            "soundevents/music/",
            "soundevents/teamfandom/",
            "soundevents/team_fandom/",
            "soundevents/stickers/",
        ];

        private const string VoiceScriptsFolder = "soundevents/voscripts/";
        private const string SoundEventsTypeName = "vsndevts_c";

        private readonly Package package;
        private readonly GameFileLoader fileLoader;
        private readonly IProgress<string>? progress;

        private readonly Queue<(string Path, bool FollowReferences)> queue = new();
        private readonly HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> optional = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> textures = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> standaloneTextures = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, (string File, KVObject Data)>? soundEvents;

        public CharacterDependencyCollector(Package package, GameFileLoader fileLoader, IProgress<string>? progress)
        {
            this.package = package;
            this.fileLoader = fileLoader;
            this.progress = progress;
        }

        public CharacterExportPlan Collect(HeroDefinition hero, IReadOnlyList<EconItem> items, CharacterExportOptions options, CancellationToken cancellationToken)
        {
            var plan = new CharacterExportPlan();

            AddHeroRoots(hero, options);

            var equippedAssets = GetEquippedAssets(hero, items);

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddItemRoots(hero, item, options, equippedAssets, plan);
            }

            progress?.Report($"Following references of {queue.Count} assets...");

            while (queue.TryDequeue(out var next))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Visit(next.Path, next.FollowReferences, plan);
            }

            foreach (var texture in textures)
            {
                if (standaloneTextures.Contains(texture))
                {
                    plan.Resources.Add(texture);
                }
                else
                {
                    plan.MaterialTextureCount++;
                }
            }

            return plan;
        }

        private void AddHeroRoots(HeroDefinition hero, CharacterExportOptions options)
        {
            if (options.HeroModel && hero.Model != null)
            {
                Enqueue(hero.Model);
            }

            if (options.HeroSounds && hero.GameSoundsFile != null)
            {
                Enqueue(hero.GameSoundsFile);
            }

            if (options.HeroVoice && hero.VoiceFile != null)
            {
                Enqueue(hero.VoiceFile);
            }

            if (options.HeroParticles && hero.ParticleFolder != null)
            {
                var folder = NormalizePath(hero.ParticleFolder).TrimEnd('/');

                foreach (var entry in GetPackageEntries("vpcf_c"))
                {
                    var directory = entry.DirectoryName.Replace('\\', '/');

                    if (directory.Equals(folder, StringComparison.OrdinalIgnoreCase)
                        || directory.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        Enqueue(entry.GetFullPath());
                    }
                }
            }

            if (options.Icons)
            {
                EnqueueImage($"heroes/{hero.Name}");
                EnqueueImage($"heroes/icons/{hero.Name}");
                EnqueueImage($"heroes/selection/{hero.Name}");
                Enqueue($"panorama/videos/heroes/{hero.Name}.webm", isOptional: true);

                foreach (var ability in hero.Abilities)
                {
                    EnqueueImage($"spellicons/{ability}");
                }
            }
        }

        /// <summary>
        /// Models and particles the hero ends up with, which decides whether swaps that only apply on top of another item
        /// are needed.
        /// </summary>
        private static HashSet<string> GetEquippedAssets(HeroDefinition hero, IReadOnlyList<EconItem> items)
        {
            var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (hero.Model != null)
            {
                assets.Add(NormalizePath(hero.Model));
            }

            foreach (var item in items)
            {
                if (item.ModelPlayer != null)
                {
                    assets.Add(NormalizePath(item.ModelPlayer));
                }

                foreach (var modifier in item.AssetModifiers)
                {
                    if (!IsConditionalSwap(modifier.Type) && IsAssetPath(modifier.Modifier))
                    {
                        assets.Add(NormalizePath(modifier.Modifier));
                    }
                }
            }

            return assets;
        }

        /// <summary>
        /// Swaps that replace another item's asset with a version made to go with this item, e.g. a refit of a head that
        /// clips with an arcana. They only matter when that other item is equipped too.
        /// </summary>
        private static bool IsConditionalSwap(string type) => type is "model" or "particle_combined";

        private void AddItemRoots(HeroDefinition hero, EconItem item, CharacterExportOptions options, HashSet<string> equippedAssets, CharacterExportPlan plan)
        {
            if (options.ItemModels && item.ModelPlayer != null)
            {
                Enqueue(item.ModelPlayer);
            }

            if (options.Icons && item.ImageInventory != null)
            {
                EnqueueImage(item.ImageInventory);
            }

            foreach (var modifier in item.AssetModifiers)
            {
                switch (modifier.Type)
                {
                    case "sound":
                        if (options.ItemSounds && modifier.Modifier != null)
                        {
                            AddSoundEvent(hero, modifier.Modifier, plan);
                        }

                        continue;

                    case "ability_icon" when options.Icons && modifier.Modifier != null:
                        EnqueueImage($"spellicons/{modifier.Modifier}");
                        continue;

                    case "icon_replacement_hero" when options.Icons && modifier.Modifier != null:
                        EnqueueImage($"heroes/{modifier.Modifier}");
                        EnqueueImage($"heroes/selection/{modifier.Modifier}");
                        continue;

                    case "icon_replacement_hero_minimap" when options.Icons && modifier.Modifier != null:
                        EnqueueImage($"heroes/icons/{modifier.Modifier}");
                        continue;

                    case "inventory_icon" when options.Icons && modifier.Modifier != null:
                        EnqueueImage($"items/{modifier.Modifier}");
                        continue;
                }

                if (IsConditionalSwap(modifier.Type) && (modifier.Asset == null || !equippedAssets.Contains(NormalizePath(modifier.Asset))))
                {
                    continue;
                }

                // What gets swapped in is the modifier; the asset is what it replaces, unless the item only names an asset
                var path = IsAssetPath(modifier.Modifier) ? modifier.Modifier : IsAssetPath(modifier.Asset) ? modifier.Asset : null;

                if (path == null)
                {
                    continue;
                }

                var wanted = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".vmdl" => options.ItemModels,
                    ".vpcf" or ".vsnap" => options.ItemParticles,
                    ".vsndevts" or ".vsnd" => options.ItemSounds,
                    _ => true,
                };

                if (wanted)
                {
                    Enqueue(path);
                }
            }
        }

        /// <summary>
        /// Adds the sounds a single sound event plays. Its sound event file is exported for its text, but not followed,
        /// since it usually holds a lot of other events too.
        /// </summary>
        private void AddSoundEvent(HeroDefinition hero, string eventName, CharacterExportPlan plan)
        {
            soundEvents ??= BuildSoundEventIndex(hero);

            if (!soundEvents.TryGetValue(eventName, out var soundEvent))
            {
                plan.Missing.Add($"sound event {eventName}");
                return;
            }

            Enqueue(soundEvent.File, followReferences: false);

            var sounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectEventSounds(soundEvent.Data, sounds, depth: 0);

            foreach (var sound in sounds)
            {
                Enqueue(sound);
            }
        }

        private void CollectEventSounds(KVObject soundEvent, HashSet<string> sounds, int depth)
        {
            CollectSoundPaths(soundEvent, sounds);

            // An event may inherit its sounds from a base event
            if (depth < 8
                && soundEvent.GetStringProperty("base") is { Length: > 0 } baseName
                && soundEvents!.TryGetValue(baseName, out var baseEvent))
            {
                CollectEventSounds(baseEvent.Data, sounds, depth + 1);
            }
        }

        private static void CollectSoundPaths(KVObject value, HashSet<string> sounds)
        {
            switch (value.ValueType)
            {
                case KVValueType.String:
                    var text = value.ToString();

                    if (text != null && text.EndsWith(".vsnd", StringComparison.OrdinalIgnoreCase))
                    {
                        sounds.Add(text);
                    }

                    break;

                case KVValueType.Collection:
                case KVValueType.Array:
                    foreach (var child in value.Values)
                    {
                        CollectSoundPaths(child, sounds);
                    }

                    break;
            }
        }

        private Dictionary<string, (string File, KVObject Data)> BuildSoundEventIndex(HeroDefinition hero)
        {
            progress?.Report("Indexing sound events...");

            var index = new Dictionary<string, (string File, KVObject Data)>(StringComparer.OrdinalIgnoreCase);
            var heroVoiceFile = hero.VoiceFile != null ? NormalizePath(hero.VoiceFile) + GameFileLoader.CompiledFileSuffix : null;

            // Hero files go first so their definitions win over same named events elsewhere
            var entries = GetPackageEntries(SoundEventsTypeName)
                .Where(entry =>
                {
                    var path = entry.GetFullPath();

                    if (path.StartsWith(VoiceScriptsFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        return path.Equals(heroVoiceFile, StringComparison.OrdinalIgnoreCase);
                    }

                    return !IgnoredSoundEventFolders.Any(folder => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
                })
                .OrderByDescending(entry => entry.GetFullPath().Contains(hero.ShortName, StringComparison.OrdinalIgnoreCase));

            foreach (var entry in entries)
            {
                var path = entry.GetFullPath();

                try
                {
                    using var resource = new Resource { FileName = path };
                    resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));

                    if (resource.DataBlock == null)
                    {
                        continue;
                    }

                    var sourcePath = path[..^GameFileLoader.CompiledFileSuffix.Length];

                    foreach (var (eventName, eventData) in resource.DataBlock.AsKeyValueCollection())
                    {
                        if (eventData.ValueType == KVValueType.Collection)
                        {
                            index.TryAdd(eventName, (sourcePath, eventData));
                        }
                    }
                }
                catch (Exception e)
                {
                    progress?.Report($"  ! failed to read sound events from \"{path}\": {e.Message}");
                }
            }

            return index;
        }

        private void Visit(string path, bool followReferences, CharacterExportPlan plan)
        {
            var sourcePath = path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase)
                ? path[..^GameFileLoader.CompiledFileSuffix.Length]
                : path;
            var compiledPath = sourcePath + GameFileLoader.CompiledFileSuffix;
            var extension = Path.GetExtension(sourcePath);

            if (!Exists(compiledPath))
            {
                if (Exists(sourcePath))
                {
                    plan.RawFiles.Add(sourcePath);
                }
                else if (!optional.Contains(sourcePath))
                {
                    plan.Missing.Add(sourcePath);
                }

                return;
            }

            if (extension.Equals(".vmdl", StringComparison.OrdinalIgnoreCase))
            {
                plan.Models.Add(compiledPath);
            }
            else if (extension.Equals(".vmat", StringComparison.OrdinalIgnoreCase))
            {
                plan.Materials.Add(compiledPath);
            }
            else if (extension.Equals(".vtex", StringComparison.OrdinalIgnoreCase))
            {
                textures.Add(compiledPath);
            }
            else if (ModelOwnedExtensions.Contains(extension))
            {
                plan.ModelDependencyCount++;
            }
            else
            {
                plan.Resources.Add(compiledPath);
            }

            if (!followReferences || LeafExtensions.Contains(extension))
            {
                return;
            }

            foreach (var reference in ReadReferences(compiledPath, extension))
            {
                var referenceExtension = Path.GetExtension(reference);

                // Textures a material uses are written by the material exporter, anything else using one needs it on its own
                if (referenceExtension.Equals(".vtex", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".vmat", StringComparison.OrdinalIgnoreCase))
                {
                    standaloneTextures.Add(reference + GameFileLoader.CompiledFileSuffix);
                }

                Enqueue(reference);
            }
        }

        private List<string> ReadReferences(string compiledPath, string extension)
        {
            var references = new List<string>();
            var stream = fileLoader.GetFileStream(compiledPath);

            if (stream == null)
            {
                return references;
            }

            try
            {
                using var resource = new Resource { FileName = compiledPath };
                resource.Read(stream);

                if (resource.ExternalReferences is { } externalReferences)
                {
                    foreach (var reference in externalReferences.ResourceRefInfoList)
                    {
                        if (!string.IsNullOrEmpty(reference.Name))
                        {
                            references.Add(reference.Name);
                        }
                    }
                }

                // Sound event files do not always list the sounds they play as references
                if (extension.Equals(".vsndevts", StringComparison.OrdinalIgnoreCase) && resource.DataBlock != null)
                {
                    var sounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    CollectSoundPaths(resource.DataBlock.AsKeyValueCollection(), sounds);
                    references.AddRange(sounds);
                }
            }
            catch (Exception e)
            {
                progress?.Report($"  ! failed to read references of \"{compiledPath}\": {e.Message}");
            }

            return references;
        }

        private void Enqueue(string path, bool followReferences = true, bool isOptional = false)
        {
            path = NormalizePath(path);

            if (path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^GameFileLoader.CompiledFileSuffix.Length];
            }

            if (isOptional)
            {
                optional.Add(path);
            }

            if (visited.Add(path))
            {
                queue.Enqueue((path, followReferences));
            }
        }

        /// <summary>
        /// Queues a panorama image, named the way items_game.txt names them: relative to panorama/images and without extension.
        /// </summary>
        private void EnqueueImage(string image)
        {
            var path = $"panorama/images/{NormalizePath(image)}_png.vtex";

            standaloneTextures.Add(path + GameFileLoader.CompiledFileSuffix);
            Enqueue(path, isOptional: true);
        }

        private bool Exists(string path)
        {
            var (pathOnDisk, _, packageEntry) = fileLoader.FindFile(path, logNotFound: false);
            return pathOnDisk != null || packageEntry != null;
        }

        private List<PackageEntry> GetPackageEntries(string typeName)
            => package.Entries != null && package.Entries.TryGetValue(typeName, out var entries) ? entries : [];

        private static bool IsAssetPath([NotNullWhen(true)] string? value)
            => value != null && value.Contains('/', StringComparison.Ordinal) && AssetExtensions.Contains(Path.GetExtension(value));

        private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');
    }
}
