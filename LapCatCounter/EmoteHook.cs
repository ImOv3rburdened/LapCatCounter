using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using System;
using System.Linq;

namespace LapCatCounter;

public sealed class EmoteHook : IDisposable
{
    private delegate void EmoteExecuteDelegate(
        ulong unk,
        ulong instigatorAddr,
        ushort emoteId,
        ulong targetId,
        ulong unk2);

    private readonly IObjectTable objects;
    private readonly IPluginLog log;
    private readonly Hook<EmoteExecuteDelegate>? hook;

    public bool HookReady { get; private set; }

    public ushort LastEmoteId { get; private set; }
    public ushort LastLocalInstigatorEmoteId { get; private set; }
    public DateTime LastLocalInstigatorEmoteUtc { get; private set; } = DateTime.MinValue;
    public ushort LastLocalTargetEmoteId { get; private set; }
    public DateTime LastLocalTargetEmoteUtc { get; private set; } = DateTime.MinValue;
    public ulong LastLocalTargetInstigatorObjectId { get; private set; }

    public EmoteHook(ISigScanner sigScanner, IGameInteropProvider interop, IObjectTable objects, IPluginLog log)
    {
        this.objects = objects;
        this.log = log;

        try
        {
            var addr = sigScanner.ScanText("E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ?? 4C 89 74 24");

            hook = interop.HookFromAddress<EmoteExecuteDelegate>(addr, OnEmoteDetour);
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
            if (local != null)
            {
                var instigatorObj = objects.FirstOrDefault(o => (ulong)o.Address == instigatorAddr);
                var now = DateTime.UtcNow;

                if (instigatorObj != null && instigatorObj.GameObjectId == local.GameObjectId)
                {
                    LastLocalInstigatorEmoteId = emoteId;
                    LastLocalInstigatorEmoteUtc = now;
                }

                if (targetId == local.GameObjectId && (instigatorObj == null || instigatorObj.GameObjectId != local.GameObjectId))
                {
                    LastLocalTargetEmoteId = emoteId;
                    LastLocalTargetEmoteUtc = now;
                    LastLocalTargetInstigatorObjectId = instigatorObj?.GameObjectId ?? 0;
                }
            }
        }
        catch
        {

        }

        hook?.Original(unk, instigatorAddr, emoteId, targetId, unk2);
    }

    public bool WasRecentlyLocalInstigator(ushort emoteId, double seconds)
    {
        if (emoteId == 0) return false;
        if (LastLocalInstigatorEmoteId != emoteId) return false;
        return (DateTime.UtcNow - LastLocalInstigatorEmoteUtc).TotalSeconds <= seconds;
    }

    public bool WasRecentlyLocalTargetedByOther(ushort emoteId, double seconds, out ulong instigatorObjectId)
    {
        instigatorObjectId = 0;
        if (emoteId == 0) return false;
        if (LastLocalTargetEmoteId != emoteId) return false;
        if ((DateTime.UtcNow - LastLocalTargetEmoteUtc).TotalSeconds > seconds) return false;

        instigatorObjectId = LastLocalTargetInstigatorObjectId;
        return true;
    }

    public void Dispose()
    {
        try
        {
            hook?.Disable();
            hook?.Dispose();
        }
        catch { }

        HookReady = false;
    }
}
