using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using System;

namespace LapCatCounter;

public sealed unsafe class EmoteHook : IDisposable
{
    private delegate void EmoteExecuteDelegate(
        ulong unk,
        ulong instigatorAddr,
        ushort emoteId,
        ulong targetId,
        ulong unk2);

    private readonly IObjectTable objects;
    private readonly IPluginLog log;

    [Signature("E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ?? 4C 89 74 24", Fallibility = Fallibility.Fallible)]
    private readonly nint emoteExecuteAddr = nint.Zero;

    private Hook<EmoteExecuteDelegate>? hook;

    public bool HookReady { get; private set; }

    public ushort LastEmoteId { get; private set; }
    public DateTime LastEmoteUtc { get; private set; } = DateTime.MinValue;

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
            if (local != null)
            {
                var localAddr = (ulong)local.Address;
                bool isLocalInstigator = instigatorAddr == localAddr;

                bool targetsLocal = targetId == local.GameObjectId;

                if (isLocalInstigator || targetsLocal)
                {
                    LastEmoteId = emoteId;
                    LastEmoteUtc = DateTime.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            log.Debug(ex, "[LapCatCounter] Exception in OnEmoteDetour");
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
        try { hook?.Dispose(); } catch { }
        HookReady = false;
    }
}
