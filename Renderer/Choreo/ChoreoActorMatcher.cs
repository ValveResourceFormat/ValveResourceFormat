using System.IO;
using System.Linq;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;

namespace ValveResourceFormat.Renderer.Choreo
{
    /// <summary>
    /// How strongly an actor of a scene belongs to a model, weakest first.
    /// </summary>
    public enum ChoreoActorMatch
    {
        /// <summary>Nothing relates the actor to the model.</summary>
        None,

        /// <summary>The actor drives flex controllers of the model. Models of a game share those names.</summary>
        FlexControllers,

        /// <summary>The name of the actor or of the folder of the scene is the name of the model.</summary>
        Name,

        /// <summary>The actor plays animations of the model, whose names are specific to it.</summary>
        Animations,
    }

    /// <summary>
    /// An actor of a scene and how it belongs to a model.
    /// </summary>
    /// <param name="Scene">The scene.</param>
    /// <param name="Actor">The actor of the scene.</param>
    /// <param name="Match">How the actor belongs to the model.</param>
    public readonly record struct ChoreoSceneActor(ChoreoScene Scene, ChoreoActor Actor, ChoreoActorMatch Match);

    /// <summary>
    /// Finds the scene actors that a model can play. Games bind actors to entities by target name, or to the entities
    /// a map passes to the scene, so nothing in a scene names its model and the content of the actor has to tell.
    /// </summary>
    public static class ChoreoActorMatcher
    {
        /// <summary>Finds the actors of scenes that a model can play, strongest matches first.</summary>
        /// <param name="sources">Where the scenes are. A scene in several sources is taken from the first.</param>
        /// <param name="node">The model.</param>
        /// <param name="modelName">The file name of the model.</param>
        /// <returns>The actors. Those that only share flex controller names come only when no other actor matches.</returns>
        public static List<ChoreoSceneActor> FindActors(IEnumerable<IChoreoSceneSource> sources, ModelSceneNode node, string modelName)
        {
            ArgumentNullException.ThrowIfNull(sources);
            ArgumentNullException.ThrowIfNull(node);

            var actors = new List<ChoreoSceneActor>();
            var controllers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var controller in node.Model.FlexControllers)
            {
                controllers.Add(controller.Name);
            }

            var sceneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var source in sources)
            {
                foreach (var scene in source.EnumerateScenes())
                {
                    if (!sceneNames.Add(scene.Name ?? string.Empty))
                    {
                        continue;
                    }

                    var folderIsModel = IsNameOfModel(GetSceneFolder(scene), modelName);

                    foreach (var actor in scene.Actors)
                    {
                        var match = GetMatch(folderIsModel, actor, node, controllers, modelName);
                        if (match != ChoreoActorMatch.None)
                        {
                            actors.Add(new ChoreoSceneActor(scene, actor, match));
                        }
                    }
                }
            }

            if (actors.Exists(static actor => actor.Match > ChoreoActorMatch.FlexControllers))
            {
                actors.RemoveAll(static actor => actor.Match == ChoreoActorMatch.FlexControllers);
            }

            actors.Sort(static (left, right) =>
            {
                var match = right.Match.CompareTo(left.Match);
                return match != 0 ? match : string.Compare(left.Scene.Name, right.Scene.Name, StringComparison.OrdinalIgnoreCase);
            });

            return actors;
        }

        /// <summary>
        /// Gets whether a name of a scene is the name of a model: the model file or its folder, either all of the name
        /// or one of its underscore separated words, as in <c>hero_ghost</c> or <c>larry_cough</c>.
        /// </summary>
        /// <param name="name">The name of an actor or of a folder of scenes.</param>
        /// <param name="modelName">The file name of the model, such as <c>models/heroes/techies/techies.vmdl</c>.</param>
        public static bool IsNameOfModel(string name, string modelName)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(modelName);

            var fileName = Path.GetFileNameWithoutExtension(modelName);
            var folder = Path.GetFileName(Path.GetDirectoryName(modelName)) ?? string.Empty;

            if (IsEither(name, fileName, folder))
            {
                return true;
            }

            foreach (var word in name.Split('_'))
            {
                if (IsEither(word, fileName, folder))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsEither(string name, string first, string second)
            => name.Equals(first, StringComparison.OrdinalIgnoreCase) || name.Equals(second, StringComparison.OrdinalIgnoreCase);

        /// <summary>Finds a model for a scene by the names of its actors and of its folder.</summary>
        /// <param name="scene">The scene.</param>
        /// <param name="modelFiles">The file names of the models to choose from.</param>
        /// <returns>The first actor with a model and that model, or null when no name leads to a model.</returns>
        public static (string Model, ChoreoActor Actor)? FindModel(ChoreoScene scene, IEnumerable<string> modelFiles)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(modelFiles);

            var folderModel = FindModelNamed(GetSceneFolder(scene), modelFiles);

            foreach (var actor in scene.Actors)
            {
                var model = FindModelNamed(actor.Name, modelFiles) ?? folderModel;

                if (model != null)
                {
                    return (model, actor);
                }
            }

            return null;
        }

        // Models that share a name differ by variant or cosmetic, the main one is named after its folder
        private static string? FindModelNamed(string name, IEnumerable<string> modelFiles)
        {
            return modelFiles
                .Where(modelFile => IsNameOfModel(name, modelFile))
                .OrderByDescending(IsNamedAfterFolder)
                .ThenBy(static modelFile => modelFile.Length)
                .FirstOrDefault();
        }

        private static bool IsNamedAfterFolder(string modelFile)
        {
            var fileName = Path.GetFileNameWithoutExtension(modelFile);
            var folder = Path.GetFileName(Path.GetDirectoryName(modelFile));

            return fileName.Equals(folder, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSceneFolder(ChoreoScene scene) => scene.Name?.Split('/', '\\')[0] ?? string.Empty;

        private static ChoreoActorMatch GetMatch(bool folderIsModel, ChoreoActor actor, ModelSceneNode node, HashSet<string> controllers, string modelName)
        {
            var drivesFlex = false;

            foreach (var channel in actor.Channels)
            {
                foreach (var choreoEvent in channel.Events)
                {
                    if (choreoEvent.Type is ChoreoEventType.Gesture or ChoreoEventType.Sequence && node.Animations.ContainsKey(choreoEvent.Param1))
                    {
                        return ChoreoActorMatch.Animations;
                    }

                    foreach (var track in choreoEvent.EventFlex.Tracks)
                    {
                        drivesFlex = drivesFlex || controllers.Contains(track.Name) || controllers.Contains($"right_{track.Name}");
                    }
                }
            }

            if (folderIsModel || IsNameOfModel(actor.Name, modelName))
            {
                return ChoreoActorMatch.Name;
            }

            return drivesFlex ? ChoreoActorMatch.FlexControllers : ChoreoActorMatch.None;
        }
    }
}
