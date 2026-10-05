using Microsoft.Extensions.Logging;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>logic_branch_listener</c>. Watches up to sixteen <see cref="LogicBranch"/> entities and reports
/// whether they are all true, all false, or a mix, each time that answer changes.
/// </summary>
public sealed class LogicBranchListener : BaseEntity
{
    // The FGD's Branch01 to Branch16
    private const int BranchNameCount = 16;

    private enum State
    {
        Unknown,
        AllTrue,
        AllFalse,
        Mixed,
    }

    // Duplicates are kept: a branch matched by two names is listed twice, as in the engine
    private readonly List<LogicBranch> branches = [];
    private State lastState;

    /// <summary>Gets the branches being watched.</summary>
    public IReadOnlyList<LogicBranch> Branches => branches;

    /// <summary>Initializes a <c>logic_branch_listener</c> from its keyvalues.</summary>
    public LogicBranchListener(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        for (var i = 0; i < BranchNameCount; i++)
        {
            var name = KeyValues.GetStringProperty($"branch{i + 1:00}");

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            foreach (var entity in EntitySystem.FindAllByTargetName(name))
            {
                if (entity is not LogicBranch branch)
                {
                    EntitySystem.Logger.LogWarning("logic_branch_listener '{TargetName}' refers to '{Target}', which is not a logic_branch",
                        TargetName, entity.TargetName);
                    continue;
                }

                branch.AddListener(this);
                branches.Add(branch);
            }
        }
    }

    [EntityInput("Test")]
    private void InputTest(EntityInputData data)
    {
        // Forgetting the last answer makes the test fire even when nothing changed
        lastState = State.Unknown;
        UpdateOutputs(data.Activator);
    }

    [EntityInput("_OnLogicBranchChanged")]
    private void InputOnLogicBranchChanged(EntityInputData data) => UpdateOutputs(data.Activator);

    // The engine sends this one to the removed branch itself rather than to its listeners, so in practice
    // only a map firing it by hand reaches here. A removed branch still counts as not true either way.
    [EntityInput("_OnLogicBranchRemoved")]
    private void InputOnLogicBranchRemoved(EntityInputData data)
    {
        if (data.Activator is LogicBranch branch)
        {
            branches.Remove(branch);
        }

        UpdateOutputs(data.Activator);
    }

    private void UpdateOutputs(BaseEntity? activator)
    {
        var anyTrue = false;
        var anyNotTrue = false;

        foreach (var branch in branches)
        {
            if (!branch.IsRemoved && branch.Value)
            {
                anyTrue = true;
            }
            else
            {
                anyNotTrue = true;
            }
        }

        // With no branches at all this reports a mix, not all true
        var state = (anyTrue, anyNotTrue) switch
        {
            (true, false) => State.AllTrue,
            (false, true) => State.AllFalse,
            _ => State.Mixed,
        };

        if (state == lastState)
        {
            return;
        }

        lastState = state;

        var output = state switch
        {
            State.AllTrue => "OnAllTrue",
            State.AllFalse => "OnAllFalse",
            _ => "OnMixed",
        };

        EntitySystem.TriggerOutput(this, output, activator);
    }
}
