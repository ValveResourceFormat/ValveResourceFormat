using System.Linq;
using System.Text;
using GUI.Utils;
using ValveResourceFormat.DemoPlayback;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.GLViewers
{
    sealed class CsDemoEffectSceneManager : IDisposable
    {
        private const string DemoLayerName = "Demo Playback";
        private const float DroppedWeaponGroundPadding = .75f;
        private const float GrenadeProjectileGroundPadding = .75f;
        private const float GroundTraceUp = 96f;
        private const float GroundTraceDown = 192f;

        private readonly Scene scene;
        private readonly VrfGuiContext guiContext;
        private readonly Dictionary<string, Model?> modelCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ParticleSystem?> particleCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, (SceneNode Node, string Signature)> entityNodes = [];
        private readonly Dictionary<string, (SceneNode Node, string Signature)> effectNodes = [];

        public CsDemoEffectSceneManager(Scene scene, VrfGuiContext guiContext)
        {
            this.scene = scene;
            this.guiContext = guiContext;
        }

        public void ApplyFrame(CsDemoFrame frame, float particleTimeScale)
        {
            ApplyWorldEntities(frame.Tick, frame.WorldEntities);
            ApplyWorldEffects(frame.Tick, frame.WorldEffects, particleTimeScale);
        }

        public void Clear()
        {
            foreach (var item in entityNodes.Values)
            {
                scene.Remove(item.Node, dynamic: true);
            }

            foreach (var item in effectNodes.Values)
            {
                item.Node.Delete();
                scene.Remove(item.Node, dynamic: true);
            }

            entityNodes.Clear();
            effectNodes.Clear();
        }

        private void ApplyWorldEntities(int tick, IReadOnlyList<CsDemoWorldEntityState> entities)
        {
            var activeIds = new HashSet<uint>();

            foreach (var entity in entities)
            {
                activeIds.Add(entity.EntityIndex);

                var signature = $"{entity.Kind}:{entity.GrenadeType}:{entity.ModelIdentity}";
                if (!entityNodes.TryGetValue(entity.EntityIndex, out var existing) || existing.Signature != signature)
                {
                    if (existing.Node != null)
                    {
                        scene.Remove(existing.Node, dynamic: true);
                    }

                    existing = (CreateEntityNode(entity), signature);
                    scene.Add(existing.Node, dynamic: true);
                    entityNodes[entity.EntityIndex] = existing;
                }

                SetEntityTransform(existing.Node, entity);
                existing.Node.LayerEnabled = true;
            }

            foreach (var entityIndex in entityNodes.Keys.Except(activeIds).ToArray())
            {
                scene.Remove(entityNodes[entityIndex].Node, dynamic: true);
                entityNodes.Remove(entityIndex);
            }
        }

        private void ApplyWorldEffects(int tick, IReadOnlyList<CsDemoWorldEffectState> effects, float particleTimeScale)
        {
            var activeIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var effect in effects)
            {
                activeIds.Add(effect.Id);

                var signature = effect.Kind.ToString();
                if (!effectNodes.TryGetValue(effect.Id, out var existing) || existing.Signature != signature)
                {
                    if (existing.Node != null)
                    {
                        existing.Node.Delete();
                        scene.Remove(existing.Node, dynamic: true);
                    }

                    existing = (CreateEffectNode(effect), signature);
                    scene.Add(existing.Node, dynamic: true);
                    effectNodes[effect.Id] = existing;
                }

                if (existing.Node is ParticleSceneNode particle)
                {
                    particle.FrametimeMultiplier = particleTimeScale;
                }

                SetTransform(existing.Node, effect.Position, 0f, GetEffectScale(effect, tick));
            }

            foreach (var effectId in effectNodes.Keys.Except(activeIds).ToArray())
            {
                RemoveEffect(effectId);
            }
        }

        private SceneNode CreateEntityNode(CsDemoWorldEntityState entity)
        {
            var modelNode = CreateEntityModelNode(entity);

            if (modelNode != null)
            {
                return modelNode;
            }

            var scale = entity.Kind == CsDemoWorldEntityKind.Weapon
                ? new Vector3(24, 8, 8)
                : new Vector3(12, 12, 12);

            return new SimpleBoxSceneNode(scene, GetEntityColor(entity), scale)
            {
                LayerName = DemoLayerName,
                Name = entity.ClassName,
                LayerEnabled = true,
            };
        }

        private ModelSceneNode? CreateEntityModelNode(CsDemoWorldEntityState entity)
        {
            var modelPathCandidates = entity.Kind switch
            {
                CsDemoWorldEntityKind.GrenadeProjectile => GetGrenadeModelPathCandidates(entity.GrenadeType),
                CsDemoWorldEntityKind.Weapon => GetWeaponModelPathCandidates(entity),
                _ => [],
            };

            var candidatesList = modelPathCandidates.ToList();
            AgentDebugLog.Write("WM1", "CsDemoEffectSceneManager.CreateEntityModelNode", "world entity model resolve start", new
            {
                entity.Kind,
                entity.GrenadeType,
                entity.ClassName,
                entity.ModelIdentity,
                candidates = candidatesList,
            });

            foreach (var modelPath in candidatesList)
            {
                if (LoadModel(modelPath) is not { } model)
                {
                    AgentDebugLog.Write("WM1", "CsDemoEffectSceneManager.CreateEntityModelNode", "LoadModel returned null", new { modelPath });
                    continue;
                }

                var candidate = new ModelSceneNode(scene, model, isWorldPreview: true)
                {
                    LayerName = DemoLayerName,
                    Name = entity.ClassName,
                    LayerEnabled = true,
                };

                if (!candidate.HasMeshes)
                {
                    AgentDebugLog.Write("WM1", "CsDemoEffectSceneManager.CreateEntityModelNode", "model has no meshes", new { modelPath });
                    candidate.Delete();
                    continue;
                }

                AgentDebugLog.Write("WM1", "CsDemoEffectSceneManager.CreateEntityModelNode", "model resolved OK", new { modelPath });

                if (entity.Kind == CsDemoWorldEntityKind.GrenadeProjectile)
                {
                    candidate.UpgradeGrenadeMaterials();
                }

                return candidate;
            }

            return null;
        }

        private SceneNode CreateEffectNode(CsDemoWorldEffectState effect)
        {
            var particlePath = GetParticlePath(effect.Kind);
            if (particlePath != null && LoadParticle(particlePath) is { } particle)
            {
                return new ParticleSceneNode(scene, particle)
                {
                    LayerName = DemoLayerName,
                    Name = effect.Kind.ToString(),
                };
            }

            return new SimpleBoxSceneNode(scene, GetEffectColor(effect.Kind), GetFallbackEffectScale(effect.Kind))
            {
                LayerName = DemoLayerName,
                Name = effect.Kind.ToString(),
            };
        }

        private Model? LoadModel(string path)
        {
            if (modelCache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var resource = guiContext.LoadFileCompiled(path);
            cached = resource?.DataBlock as Model;
            modelCache[path] = cached;

            AgentDebugLog.Write("WM1", "CsDemoEffectSceneManager.LoadModel", "LoadFileCompiled result", new
            {
                path,
                resourceNull = resource == null,
                dataBlockType = resource?.DataBlock?.GetType().Name,
            });

            return cached;
        }

        private ParticleSystem? LoadParticle(string path)
        {
            if (particleCache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var resource = guiContext.LoadFileCompiled(path);
            cached = resource?.DataBlock as ParticleSystem;
            particleCache[path] = cached;

            return cached;
        }

        private static void SetEntityTransform(SceneNode node, CsDemoWorldEntityState entity)
        {
            if (entity.Kind == CsDemoWorldEntityKind.Weapon)
            {
                SetDroppedWeaponTransform(node, entity);
                node.Scene.MarkParentOctreeDirty(node);
                return;
            }

            var position = entity.Position + EntityOffset(entity);

            node.Transform =
                Matrix4x4.CreateRotationX(float.DegreesToRadians(entity.Roll))
                * Matrix4x4.CreateRotationY(float.DegreesToRadians(-entity.Pitch))
                * Matrix4x4.CreateRotationZ(float.DegreesToRadians(entity.Yaw))
                * Matrix4x4.CreateTranslation(position);
            node.Scene.MarkParentOctreeDirty(node);
        }

        private static void SetDroppedWeaponTransform(SceneNode node, CsDemoWorldEntityState entity)
        {
            var position = entity.Position;
            if (TryTraceGround(node.Scene, position, out var groundHit))
            {
                position = groundHit.HitPosition;
            }

            position += new Vector3(0f, 0f, DroppedWeaponGroundPadding);

            node.Transform =
                Matrix4x4.CreateRotationX(float.DegreesToRadians(entity.Roll))
                * Matrix4x4.CreateRotationY(float.DegreesToRadians(-entity.Pitch))
                * Matrix4x4.CreateRotationZ(float.DegreesToRadians(entity.Yaw))
                * Matrix4x4.CreateTranslation(position);
        }

        private static bool TryTraceGround(Scene scene, Vector3 origin, out Rubikon.TraceResult hit)
        {
            hit = default;
            var physicsWorld = scene.PhysicsWorld;
            if (physicsWorld == null)
            {
                return false;
            }

            hit = physicsWorld.TraceRayPlacement(
                origin + new Vector3(0f, 0f, GroundTraceUp),
                origin - new Vector3(0f, 0f, GroundTraceDown));

            return hit.Hit && hit.HitNormal.Z > 0.2f;
        }

        private static void SetTransform(SceneNode node, Vector3 position, float yaw, float scale)
        {
            node.Transform = Matrix4x4.CreateScale(scale)
                * Matrix4x4.CreateRotationZ(float.DegreesToRadians(yaw))
                * Matrix4x4.CreateTranslation(position);
            node.Scene.MarkParentOctreeDirty(node);
        }

        private static Vector3 EntityOffset(CsDemoWorldEntityState entity)
        {
            return entity.Kind switch
            {
                CsDemoWorldEntityKind.GrenadeProjectile => new Vector3(0, 0, GrenadeProjectileGroundPadding),
                CsDemoWorldEntityKind.Weapon => Vector3.Zero,
                _ => Vector3.Zero,
            };
        }

        private static IEnumerable<string> GetGrenadeModelPathCandidates(CsDemoGrenadeType type)
        {
            string? leaf = type switch
            {
                CsDemoGrenadeType.HE => "hegrenade/weapon_hegrenade.vmdl",
                CsDemoGrenadeType.Flash => "flashbang/weapon_flashbang.vmdl",
                CsDemoGrenadeType.Smoke => "smokegrenade/weapon_smokegrenade.vmdl",
                CsDemoGrenadeType.Molotov => "molotov/weapon_molotov.vmdl",
                CsDemoGrenadeType.Incendiary => "incendiary/weapon_incendiarygrenade.vmdl",
                CsDemoGrenadeType.Decoy => "decoy/weapon_decoy.vmdl",
                _ => null,
            };

            if (leaf == null)
            {
                yield break;
            }

            yield return $"weapons/models/grenade/{leaf}";
            yield return $"models/weapons/models/grenade/{leaf}";
        }

        private static IEnumerable<string> GetWeaponModelPathCandidates(CsDemoWorldEntityState entity)
        {
            foreach (var className in GetWeaponClassNameCandidates(entity))
            {
                var hammerEntity = HammerEntities.Get(className);
                if (hammerEntity == null)
                {
                    continue;
                }

                foreach (var icon in hammerEntity.Icons)
                {
                    if (!icon.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // HammerEntities lists CS2 weapon world models under a doubled
                    // "models/weapons/models/..." prefix, but the assets actually live at
                    // "weapons/models/...". Prefer the real path, fall back to the listed one.
                    const string redundantPrefix = "models/weapons/models/";
                    if (icon.StartsWith(redundantPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return icon["models/".Length..];
                    }

                    yield return icon;
                }
            }
        }

        private static IEnumerable<string> GetWeaponClassNameCandidates(CsDemoWorldEntityState entity)
        {
            if (!string.IsNullOrWhiteSpace(entity.ModelIdentity))
            {
                foreach (var candidate in GetHammerWeaponClassNameCandidates(entity.ModelIdentity))
                {
                    yield return candidate;
                }
            }

            foreach (var candidate in GetHammerWeaponClassNameCandidates(entity.ClassName))
            {
                yield return candidate;
            }
        }

        private static IEnumerable<string> GetHammerWeaponClassNameCandidates(string value)
        {
            var className = ToHammerWeaponClassName(value);
            yield return className;

            const string weaponPrefix = "weapon_";
            if (className.StartsWith(weaponPrefix, StringComparison.Ordinal)
                && className.IndexOf('_', weaponPrefix.Length) >= 0)
            {
                yield return weaponPrefix + className[weaponPrefix.Length..].Replace("_", string.Empty, StringComparison.Ordinal);
            }
        }

        private static string ToHammerWeaponClassName(string value)
        {
            if (value.Contains('_'))
            {
                return value.ToLowerInvariant();
            }

            var builder = new StringBuilder(value.Length + 8);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                var previous = i > 0 ? value[i - 1] : '\0';
                var next = i + 1 < value.Length ? value[i + 1] : '\0';
                var startsNewWord = char.IsUpper(c)
                    && i > 0
                    && builder[^1] != '_'
                    && ((!char.IsUpper(previous) && !char.IsDigit(previous))
                        || (char.IsDigit(previous) && char.IsLower(next)));

                if (startsNewWord)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
        }

        private static string? GetParticlePath(CsDemoWorldEffectKind kind)
        {
            return kind switch
            {
                CsDemoWorldEffectKind.HEDetonate => "particles/explosions_fx/explosion_hegrenade.vpcf",
                CsDemoWorldEffectKind.FlashDetonate => "particles/explosions_fx/explosion_flashbang.vpcf",
                CsDemoWorldEffectKind.MolotovDetonate => "particles/inferno_fx/explosion_molotov_air.vpcf",
                CsDemoWorldEffectKind.InfernoFire => "particles/inferno_fx/incendiary_child_flame01a.vpcf",
                CsDemoWorldEffectKind.DecoyDetonate => "particles/weapons/cs_weapon_fx/weapon_decoy_ground_effect_shot.vpcf",
                _ => null,
            };
        }

        private static Color32 GetEntityColor(CsDemoWorldEntityState entity)
        {
            if (entity.Kind == CsDemoWorldEntityKind.Weapon)
            {
                return new Color32(230, 220, 120);
            }

            return entity.GrenadeType switch
            {
                CsDemoGrenadeType.HE => new Color32(255, 170, 65),
                CsDemoGrenadeType.Flash => new Color32(245, 245, 245),
                CsDemoGrenadeType.Smoke => new Color32(190, 200, 195),
                CsDemoGrenadeType.Molotov => new Color32(255, 92, 20),
                CsDemoGrenadeType.Incendiary => new Color32(255, 120, 40),
                CsDemoGrenadeType.Decoy => new Color32(255, 220, 120),
                _ => new Color32(146, 238, 120),
            };
        }

        private static Color32 GetEffectColor(CsDemoWorldEffectKind kind)
        {
            return kind switch
            {
                CsDemoWorldEffectKind.InfernoFire or CsDemoWorldEffectKind.MolotovDetonate => new Color32(255, 92, 20),
                CsDemoWorldEffectKind.FlashDetonate => new Color32(245, 245, 245),
                CsDemoWorldEffectKind.HEDetonate => new Color32(255, 170, 65),
                CsDemoWorldEffectKind.DecoyDetonate => new Color32(255, 220, 120),
                _ => new Color32(190, 200, 195),
            };
        }

        private static float GetEffectScale(CsDemoWorldEffectState effect, int tick) => 1f;

        private static Vector3 GetFallbackEffectScale(CsDemoWorldEffectKind kind)
        {
            return kind switch
            {
                CsDemoWorldEffectKind.InfernoFire => new Vector3(28, 28, 18),
                CsDemoWorldEffectKind.HEDetonate => new Vector3(48, 48, 48),
                CsDemoWorldEffectKind.FlashDetonate => new Vector3(38, 38, 38),
                CsDemoWorldEffectKind.MolotovDetonate => new Vector3(44, 44, 24),
                CsDemoWorldEffectKind.DecoyDetonate => new Vector3(20, 20, 20),
                _ => new Vector3(24),
            };
        }

        private void RemoveEffect(string id)
        {
            if (effectNodes.TryGetValue(id, out var entry))
            {
                entry.Node.Delete();
                scene.Remove(entry.Node, dynamic: true);
                effectNodes.Remove(id);
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }
}
