using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LapCatCounter;

public sealed unsafe class EmoteHook : IDisposable
{
    public readonly record struct EmoteEvent(
        ushort EmoteId,
        ulong InstigatorObjectId,
        ulong TargetObjectId,
        string InstigatorName,
        string TargetName,
        bool InstigatorIsLocal,
        bool TargetIsLocal,
        DateTime TimestampUtc);

    private delegate void EmoteExecuteDelegate(
        ulong unk,
        ulong instigatorAddr,
        ushort emoteId,
        ulong targetId,
        ulong unk2);

    private readonly IObjectTable objects;
    private readonly IPluginLog log;
    private readonly Dictionary<ulong, EmoteEvent> recentSitByInstigator = new();

    [Signature("E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ?? 4C 89 74 24", Fallibility = Fallibility.Fallible)]
    private readonly nint emoteExecuteAddr = nint.Zero;

    private Hook<EmoteExecuteDelegate>? hook;

    public bool HookReady { get; private set; }

    public ushort LastEmoteId { get; private set; }
    public DateTime LastEmoteUtc { get; private set; } = DateTime.MinValue;
    public EmoteEvent? LastRelevantEvent { get; private set; }
    public EmoteEvent? LastObservedEvent { get; private set; }

    public EmoteHook(IGameInteropProvider interop, IObjectTable objects, IPluginLog log)
    {
        this.objects = objects;
        this.log = log;

        try
        {
            interop.InitializeFromAttributes(this);

            if (emoteExecuteAddr == nint.Zero)
                throw new Exception("Signature scan failed (emoteExecuteAddr == 0).");

            hook = interop.HookFromAddress<EmoteExecuteDelegate>(emoteExecuteAddr, OnEmoteDetour);
            hook.Enable();

            HookReady = true;
        }
        catch (Exception ex)
        {
            HookReady = false;
            log.Error(ex, "[LapCatCounter] Failed to hook emote execution.");
        }
    }

    private void OnEmoteDetour(ulong unk, ulong instigatorAddr, ushort emoteId, ulong targetId, ulong unk2)
    {
        try
        {
            var local = objects.LocalPlayer;
            var instigator = objects.FirstOrDefault(x => (ulong)x.Address == instigatorAddr) as IPlayerCharacter;
            var target = objects.FirstOrDefault(x => x.GameObjectId == targetId) as IPlayerCharacter;

            if (local != null)
            {
                var localAddr = (ulong)local.Address;
                bool instigatorIsLocal = instigatorAddr == localAddr;
                bool targetIsLocal = targetId == local.GameObjectId;

                if (instigator != null || target != null)
                {
                    var observed = new EmoteEvent(
                        emoteId,
                        instigator?.GameObjectId ?? 0,
                        target?.GameObjectId ?? targetId,
                        instigator?.Name.TextValue ?? string.Empty,
                        target?.Name.TextValue ?? string.Empty,
                        instigatorIsLocal,
                        targetIsLocal,
                        DateTime.UtcNow);

                    LastObservedEvent = observed;

                    if (instigator?.GameObjectId is > 0)
                    {
                        recentSitByInstigator[instigator.GameObjectId] = observed;
                        PruneRecentSitEvents(observed.TimestampUtc);
                    }

                    if (instigatorIsLocal || targetIsLocal)
                    {
                        LastEmoteId = emoteId;
                        LastEmoteUtc = observed.TimestampUtc;
                        LastRelevantEvent = observed;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Debug(ex, "[LapCatCounter] Exception in OnEmoteDetour");
        }

        hook?.Original(unk, instigatorAddr, emoteId, targetId, unk2);
    }

    private void PruneRecentSitEvents(DateTime nowUtc)
    {
        const double maxAgeSeconds = 15.0;
        var expired = recentSitByInstigator
            .Where(kvp => (nowUtc - kvp.Value.TimestampUtc).TotalSeconds > maxAgeSeconds)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expired)
            recentSitByInstigator.Remove(key);
    }

    public bool TryGetRecentLapEvent(ushort sitEmoteId, ushort groundSitEmoteId, double seconds, out EmoteEvent emoteEvent)
    {
        emoteEvent = default;

        if (LastRelevantEvent is not { } evt)
            return false;

        if (evt.EmoteId != sitEmoteId && evt.EmoteId != groundSitEmoteId)
            return false;

        if ((DateTime.UtcNow - evt.TimestampUtc).TotalSeconds > seconds)
            return false;

        emoteEvent = evt;
        return true;
    }

    public bool TryGetRecentObservedLapEvent(ushort sitEmoteId, ushort groundSitEmoteId, double seconds, out EmoteEvent emoteEvent)
    {
        emoteEvent = default;

        if (LastObservedEvent is not { } evt)
            return false;

        if (evt.EmoteId != sitEmoteId && evt.EmoteId != groundSitEmoteId)
            return false;

        if ((DateTime.UtcNow - evt.TimestampUtc).TotalSeconds > seconds)
            return false;

        emoteEvent = evt;
        return true;
    }

    public bool TryGetRecentSitForInstigator(ulong objectId, ushort sitEmoteId, ushort groundSitEmoteId, double seconds, out EmoteEvent emoteEvent)
    {
        emoteEvent = default;

        if (objectId == 0 || !recentSitByInstigator.TryGetValue(objectId, out var evt))
            return false;

        if (evt.EmoteId != sitEmoteId && evt.EmoteId != groundSitEmoteId)
            return false;

        if ((DateTime.UtcNow - evt.TimestampUtc).TotalSeconds > seconds)
            return false;

        emoteEvent = evt;
        return true;
    }

    public void ConsumeRecentSitForInstigator(ulong objectId)
    {
        if (objectId == 0)
            return;

        recentSitByInstigator.Remove(objectId);

        if (LastObservedEvent is { } observed && observed.InstigatorObjectId == objectId)
            LastObservedEvent = null;

        if (LastRelevantEvent is { } relevant && relevant.InstigatorObjectId == objectId)
        {
            LastRelevantEvent = null;
            LastEmoteId = 0;
            LastEmoteUtc = DateTime.MinValue;
        }
    }
    public bool WasRecently(ushort emoteId, double seconds)
    {
        if (emoteId == 0)
            return false;

        if (LastEmoteId != emoteId)
            return false;

        return (DateTime.UtcNow - LastEmoteUtc).TotalSeconds <= seconds;
    }

    public void Dispose()
    {
        try
        {
            hook?.Dispose();
        }
        catch
        {
        }

        HookReady = false;
    }
}

