using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Linq;
using System.Numerics;

namespace LapCatCounter;

public sealed class LapCatCounterPlugin : IDalamudPlugin
{
    public string Name => "Lap Cat Counter";

    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IObjectTable objects;
    private readonly IGameGui gui;
    private readonly IFramework framework;
    private readonly IChatGui chat;
    private readonly IPluginLog log;

    private readonly WindowSystem ws = new("LapCatCounter");

    private readonly Configuration cfg;
    private readonly LapTracker tracker;
    private readonly LapTracker satOnYouTracker;
    private readonly MainWindow window;
    private readonly EmoteHook emoteHook;

    private Vector3 lastLocalPos = Vector3.Zero;

    internal bool DebugEnabled { get; private set; } = false;
    internal bool DebugOverlayEnabled { get; set; } = true;
    internal bool LastHookActive { get; private set; } = false;
    internal LapDebugInfo? LastDebugInfo { get; private set; }

    private float debugNextChatAt = 0f;
    private float debugNextLogAt = 0f;
    private const float DebugChatInterval = 1.0f;
    private const float DebugLogInterval = 0.25f;
    private float sitGateRemaining = 0f;
    private float sitPostDelayRemaining = 0f;
    private Vector3 lastPosForGate = Vector3.Zero;
    private bool hasLastPosForGate = false;

    private float satOnYouGateRemaining = 0f;
    private float satOnYouPostDelayRemaining = 0f;
    private Vector3 lastPosForSatOnYouGate = Vector3.Zero;
    private bool hasLastPosForSatOnYouGate = false;
    private ulong satOnYouInstigatorObjectId = 0;

    public LapCatCounterPlugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IObjectTable objectTable,
        IGameGui gameGui,
        IFramework framework,
        IChatGui chatGui,
        ISigScanner sigScanner,
        IPluginLog log,
        IDataManager dataManager,
        IGameInteropProvider interopProvider)
    {
        pi = pluginInterface;
        commands = commandManager;
        objects = objectTable;
        gui = gameGui;
        this.framework = framework;
        chat = chatGui;
        this.log = log;

        cfg = pi.GetPluginConfig() as Configuration ?? new Configuration();
        tracker = new LapTracker(cfg, LapTracker.Mode.SatIn);
        satOnYouTracker = new LapTracker(cfg, LapTracker.Mode.SatOnYou);

        if (cfg.RequireSitEmote && (cfg.SitEmoteId == 0 || cfg.GroundSitEmoteId == 0))
        {
            if (EmoteIdResolver.TryResolveSitIds(dataManager, log, out var sitId, out var groundId))
            {
                if (cfg.SitEmoteId == 0) cfg.SitEmoteId = sitId;
                if (cfg.GroundSitEmoteId == 0) cfg.GroundSitEmoteId = groundId;

                pi.SavePluginConfig(cfg);
                log.Information($"[LapCatCounter] Auto-resolved sit IDs: /sit={cfg.SitEmoteId}, /groundsit={cfg.GroundSitEmoteId}");
            }
            else
            {
                log.Warning("[LapCatCounter] Could not auto-resolve /sit and /groundsit IDs (Lumina lookup failed).");
            }
        }

        emoteHook = new EmoteHook(sigScanner, interopProvider, objects, log);

        window = new MainWindow(cfg, tracker, satOnYouTracker, SaveConfig, emoteHook, this);
        ws.AddWindow(window);

        commands.AddHandler("/lapcat", new CommandInfo((_, _) => window.IsOpen = true)
        {
            HelpMessage = "Open Lap Cat Counter"
        });

        commands.AddHandler("/lapcatcount", new CommandInfo((_, _) =>
        {
            var totalSatIn = tracker.TotalLaps;
            var totalSatOnYou = satOnYouTracker.TotalSatOnYou;
            var unique = tracker.UniquePeople;
            chat.Print($"[Lap Cat Counter] Sat in laps: {totalSatIn} - Sat on your lap: {totalSatOnYou}  - Unique people: {unique}");
        })
        {
            HelpMessage = "Print your total lap count to chat"
        });

        commands.AddHandler("/lapdebug", new CommandInfo(OnLapDebugCommand)
        {
            HelpMessage = "Toggle LapCatCounter debug. Usage: /lapdebug [on|off|overlay]"
        });

        pi.UiBuilder.Draw += Draw;
        pi.UiBuilder.OpenMainUi += () => window.IsOpen = true;

        framework.Update += OnUpdate;
    }

    private void SaveConfig() => pi.SavePluginConfig(cfg);

    private void OnLapDebugCommand(string cmd, string args)
    {
        args = (args ?? "").Trim().ToLowerInvariant();

        if (args == "overlay")
        {
            DebugOverlayEnabled = !DebugOverlayEnabled;
            chat.Print($"[Lap Cat Counter] Debug overlay: {(DebugOverlayEnabled ? "ON" : "OFF")}");
            return;
        }

        if (args == "on")
            DebugEnabled = true;
        else if (args == "off")
            DebugEnabled = false;
        else
            DebugEnabled = !DebugEnabled;

        chat.Print($"[Lap Cat Counter] Debug: {(DebugEnabled ? "ON" : "OFF")} (overlay {(DebugOverlayEnabled ? "ON" : "OFF")})");
    }

    private void OnUpdate(IFramework _)
    {
        var local = objects.LocalPlayer;
        if (local is null)
        {
            tracker.ResetCurrent();
            LastDebugInfo = null;
            LastHookActive = false;
            return;
        }

        float dt = ImGui.GetIO().DeltaTime;
        if (dt <= 0) dt = 1f / 60f;

        lastLocalPos = local.Position;

        bool emoteOk = true;

        if (cfg.RequireSitEmote)
        {
            if (!emoteHook.HookReady)
            {
                emoteOk = false;
                sitGateRemaining = 0f;
                sitPostDelayRemaining = 0f;
                hasLastPosForGate = false;
                satOnYouGateRemaining = 0f;
                satOnYouPostDelayRemaining = 0f;
                hasLastPosForSatOnYouGate = false;
                satOnYouInstigatorObjectId = 0;
            }
            else
            {
                satOnYouGateRemaining = MathF.Max(0f, satOnYouGateRemaining - dt);
                satOnYouPostDelayRemaining = MathF.Max(0f, satOnYouPostDelayRemaining - dt);

                const float sitDetectWindow = 0.25f;

                bool sawSitLocalInstigatorNow =
                    (cfg.SitEmoteId != 0 && emoteHook.WasRecentlyLocalInstigator(cfg.SitEmoteId, sitDetectWindow)) ||
                    (cfg.GroundSitEmoteId != 0 && emoteHook.WasRecentlyLocalInstigator(cfg.GroundSitEmoteId, sitDetectWindow));

                ulong instigatorObjId = 0;
                bool sawSitTargetingMeNow =
                    (cfg.SitEmoteId != 0 && emoteHook.WasRecentlyLocalTargetedByOther(cfg.SitEmoteId, sitDetectWindow, out instigatorObjId)) ||
                    (cfg.GroundSitEmoteId != 0 && emoteHook.WasRecentlyLocalTargetedByOther(cfg.GroundSitEmoteId, sitDetectWindow, out instigatorObjId));


                if (sawSitLocalInstigatorNow)
                {
                    sitGateRemaining = cfg.EmoteHookSeconds;
                    sitPostDelayRemaining = 0.60f;

                    hasLastPosForGate = true;
                    lastPosForGate = local.Position;
                }

                if (sawSitTargetingMeNow)
                {
                    satOnYouGateRemaining = cfg.EmoteHookSeconds;
                    satOnYouPostDelayRemaining = 0.60f;
                    satOnYouInstigatorObjectId = instigatorObjId;

                    hasLastPosForSatOnYouGate = true;
                    lastPosForSatOnYouGate = local.Position;
                }

                if (hasLastPosForGate && sitGateRemaining > 0f)
                {
                    const float moveCancelDist = 0.04f;
                    var moved = Vector3.Distance(local.Position, lastPosForGate);

                    lastPosForGate = local.Position;

                    if (moved > moveCancelDist)
                    {
                        sitGateRemaining = 0f;
                    }
                }

                if (hasLastPosForSatOnYouGate && satOnYouGateRemaining > 0f)
                {
                    const float moveCancelDist = 0.04f;
                    var moved = Vector3.Distance(local.Position, lastPosForSatOnYouGate);

                    lastPosForSatOnYouGate = local.Position;

                    if (moved > moveCancelDist)
                    {
                        satOnYouGateRemaining = 0f;
                    }
                }
            }

            emoteOk = sitGateRemaining > 0f && sitPostDelayRemaining <= 0f;
        }

        bool hookActive = emoteOk;
        LastHookActive = hookActive;


        var others = objects
            .OfType<IPlayerCharacter>()
            .Where(p => p.Address != local.Address);

        tracker.Update(dt, hookActive, local.Position, others, () =>
        {
            SaveConfig();
            var who = tracker.CurrentLapDisplayName;
            var countOnThem = tracker.GetCountFor(tracker.CurrentLapKey);
            var total = tracker.TotalLaps;
            var unique = tracker.UniquePeople;

            chat.Print($"[Lap Cat Counter] You sat in {who}'s lap! You have sat in their lap {countOnThem} time(s).");
        }, out var dbg);

        LastDebugInfo = dbg;

        bool satOnYouOk = satOnYouGateRemaining > 0f && satOnYouPostDelayRemaining <= 0f;

        satOnYouTracker.Update(dt, satOnYouOk, local.Position, others, requiredObjectId: satOnYouInstigatorObjectId, () =>
        {
            SaveConfig();
            var who = satOnYouTracker.CurrentLapDisplayName;
            var count = satOnYouTracker.GetSatOnYouCountFor(satOnYouTracker.CurrentLapKey);

            chat.Print($"[Lap Cat Counter] {who} sat in your lap! They have sat on you {count} time(s).");
        }, out var _);

        if (DebugEnabled)
        {
            var now = (float)ImGui.GetTime();

            if (now >= debugNextLogAt)
            {
                debugNextLogAt = now + DebugLogInterval;

                if (dbg.HasValue)
                {
                    var d = dbg.Value;
                    log.Debug($"[LapCatCounter DBG] hookActive={hookActive} hookReady={emoteHook.HookReady} " +
                              $"cand={d.CandidateName} dist3={d.Distance3D:0.00} horizXZ={d.HorizontalXZ:0.00} " +
                              $"dx={d.Dx:0.00} dz={d.Dz:0.00} dy={d.Dy:0.00} " +
                              $"passR={d.PassRadius} passXY={d.PassXY} passZ={d.PassZ} " +
                              $"stable={d.StableSeconds:0.00}/{cfg.StableSecondsToCount:0.00} counted={d.CountedThisGate} " +
                              $"reason={d.Reason}");
                }
                else
                {
                    log.Debug($"[LapCatCounter DBG] hookActive={hookActive} hookReady={emoteHook.HookReady} (no debug info)");
                }
            }

            if (now >= debugNextChatAt)
            {
                debugNextChatAt = now + DebugChatInterval;

                if (dbg.HasValue)
                {
                    var d = dbg.Value;
                    chat.Print($"[LapDBG] hookActive={hookActive} hookReady={emoteHook.HookReady} best={d.CandidateName} " +
                               $"dist3={d.Distance3D:0.00} horizXZ={d.HorizontalXZ:0.00} dx={d.Dx:0.00} dz={d.Dz:0.00} dy={d.Dy:0.00} " +
                               $"R={cfg.Radius:0.00} XY={cfg.XYThreshold:0.00} Z=[{cfg.MinZAbove:0.00},{cfg.MaxZAbove:0.00}] " +
                               $"pass(R/XY/Z)={d.PassRadius}/{d.PassXY}/{d.PassZ} stable={d.StableSeconds:0.0}/{cfg.StableSecondsToCount:0.0}");
                }
                else
                {
                    chat.Print($"[LapDBG] hookActive={hookActive} gate={sitGateRemaining:0.00}s hookReady={emoteHook.HookReady} (no candidate)");
                }
            }
        }
    }

    private void Draw()
    {
        ws.Draw();
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        pi.UiBuilder.Draw -= Draw;

        commands.RemoveHandler("/lapcat");
        commands.RemoveHandler("/lapcatcount");
        commands.RemoveHandler("/lapdebug");

        ws.RemoveAllWindows();
        emoteHook.Dispose();
    }
}
