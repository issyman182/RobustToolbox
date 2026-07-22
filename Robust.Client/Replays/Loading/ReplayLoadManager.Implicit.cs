using System;
using System.Collections.Generic;
using Robust.Client.GameStates;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.Map;

namespace Robust.Client.Replays.Loading;

// This partial class contains code for generating implicit component states.
public sealed partial class ReplayLoadManager
{
    /// <summary>
    ///     Cached implicit entity states.
    /// </summary>
    private Dictionary<string, (List<ComponentChange>, HashSet<ushort>)> _implicitData = new();

    private EntityState AddImplicitData(EntityState entState)
    {
        var prototype = GetPrototype(entState);
        if (prototype == null)
            return entState;

        var (list, set) = GetImplicitData(prototype);
        return MergeStates(entState, list, set);
    }

    private (List<ComponentChange>, HashSet<ushort>) GetImplicitData(string prototype)
    {
        if (_implicitData.TryGetValue(prototype, out var result))
            return result;

        var list = new List<ComponentChange>();
        var set = new HashSet<ushort>();
        _implicitData[prototype] = (list, set);

        var entCount = _entMan.EntityCount;
        _sawmill.Debug($"Generating implicit data for prototype '{prototype}'");

        EntityUid uid;
        try
        {
            uid = _entMan.SpawnEntity(prototype, MapCoordinates.Nullspace);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to spawn prototype '{prototype}' while generating implicit replay data. Entities of this prototype may be missing data. Exception: {e}");
            return (list, set);
        }

        foreach (var (netId, component) in _entMan.GetNetComponents(uid))
        {
            if (!component.NetSyncEnabled)
                continue;

            _sawmill.Debug($"Generating implicit state for '{prototype}' component {component.GetType().Name}");

            try
            {
                var state = _entMan.GetComponentState(_entMan.EventBus, component, null, GameTick.Zero);
                DebugTools.Assert(state is not IComponentDeltaState);
                list.Add(new ComponentChange(netId, state, GameTick.Zero));
                set.Add(netId);
            }
            catch (Exception e)
            {
                _sawmill.Error($"Failed to generate implicit component state for prototype '{prototype}', component {component.GetType().Name}. Exception: {e}");
            }
        }

        _entMan.DeleteEntity(uid);
        DebugTools.Assert(entCount == _entMan.EntityCount);
        return (list, set);
    }

    private string? GetPrototype(EntityState entState)
    {
        foreach (var comp in entState.ComponentChanges.Span)
        {
            if (comp.NetID == _metaId)
            {
                var state = (MetaDataComponentState) comp.State!;
                return state.PrototypeId;
            }
        }

        if (!entState.ComponentChanges.HasContents)
        {
            // This shouldn't be possible, yet it has happened?
            // TODO this should probably also throw an exception.
            _sawmill.Error($"Encountered blank entity state? Entity: {entState.NetEntity}. Last modified: {entState.EntityLastModified}. Attempting to continue.");
            return null;
        }

        if (!_confMan.GetCVar(CVars.ReplayIgnoreErrors))
            throw new MissingMetadataException(entState.NetEntity);

        _sawmill.Error($"Missing metadata component. Entity: {entState.NetEntity}. Last modified: {entState.EntityLastModified}.");
        return null;
    }
}
