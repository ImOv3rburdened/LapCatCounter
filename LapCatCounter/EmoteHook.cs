using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using System;

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
    public DateTime LastEmoteUtc { get; private set; } = DateTime.MinValue;

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

                if ((instigatorObj != null && instigatorObj.GameObjectId == local.GameObjectId) ||
                    targetId == local.GameObjectId)
                {
                    LastEmoteId = emoteId;
                    LastEmoteUtc = DateTime.UtcNow;
                }
            }
        }
        catch
        {

        }

        hook?.Original(unk, instigatorAddr, emoteId, targetId, unk2);
    }

    public bool WasRecently(ushort emoteId, double seconds)
    {
        if (emoteId == 0) return false;
        if (LastEmoteId != emoteId) return false;
        return (DateTime.UtcNow - LastEmoteUtc).TotalSeconds <= seconds;
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
