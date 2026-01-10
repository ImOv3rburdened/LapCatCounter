using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LapCatCounter;
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
    private Vector3 lastPosForGate = Vector3.Zero;
    private bool sitRequirementSatisfied = false;
    private bool hasLastPosForGate = false;
    private DateTime lastSitTriggerUtc = DateTime.MinValue;
    private const double SitTriggerWindowSeconds = 2.0;

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
        tracker = new LapTracker(cfg);
        bool changed = false;

        if (cfg.SitEmoteId == 0)
        {
            cfg.SitEmoteId = 50;
            changed = true;
        }
        if (cfg.GroundSitEmoteId == 0)
        {
            cfg.GroundSitEmoteId = 52;
            changed = true;
        }

        if (changed)
        {
            pi.SavePluginConfig(cfg);
            log.Information($"[LapCatCounter] Migrated sit IDs: /sit={cfg.SitEmoteId}, /groundsit={cfg.GroundSitEmoteId}");
        }

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

        emoteHook = new EmoteHook(interopProvider, objects, log);

        window = new MainWindow(cfg, tracker, SaveConfig, emoteHook, this);
        ws.AddWindow(window);

        commands.AddHandler("/lapcat", new CommandInfo((_, _) => window.IsOpen = true)
        {
            HelpMessage = "Open Lap Cat Counter"
        });

        commands.AddHandler("/lapcatcount", new CommandInfo((_, _) =>
        {
            var total = tracker.TotalLaps;
            var unique = tracker.UniquePeople;
            var totalTime = UiWidgets.FormatDuration(tracker.TotalLapTime);
            var bestTime = UiWidgets.FormatDuration(tracker.LongestLapTime);
            chat.Print($"[Lap Cat Counter] Total laps: {total} across {unique} people. Total lap time: {totalTime}. Longest lap: {bestTime}.");

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

    private void SaveConfig()
    {
        tracker.WriteTimeTotalsToConfig();
        pi.SavePluginConfig(cfg);
    }

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
            sitRequirementSatisfied = false;
            hasLastPosForGate = false;

            LastDebugInfo = null;
            LastHookActive = false;
            return;
        }

        float dt = ImGui.GetIO().DeltaTime;
        if (dt <= 0) dt = 1f / 60f;

        if (!cfg.RequireSitEmote)
        {
            sitRequirementSatisfied = false;
            hasLastPosForGate = false;
            tracker.ResetCurrent();
        }

        lastLocalPos = local.Position;

        bool emoteOk = true;

        var lastEmote = emoteHook.LastEmoteId;
        var emoteAge = (DateTime.UtcNow - emoteHook.LastEmoteUtc).TotalSeconds;

        if (cfg.RequireSitEmote)
        {
            if (!emoteHook.HookReady)
            {
                emoteOk = false;
            }
            else
            {
                bool isSitEmote = lastEmote == cfg.SitEmoteId || lastEmote == cfg.GroundSitEmoteId;

                bool isNewSitTrigger =
                    isSitEmote &&
                    emoteHook.LastEmoteUtc != lastSitTriggerUtc &&
                    emoteAge <= SitTriggerWindowSeconds;

                if (isNewSitTrigger)
                {
                    lastSitTriggerUtc = emoteHook.LastEmoteUtc;

                    sitRequirementSatisfied = true;
                    lastPosForGate = local.Position;
                    hasLastPosForGate = true;

                    tracker.ResetCurrent();
                }

                if (hasLastPosForGate && sitRequirementSatisfied)
                {
                    const float moveCancelDist = 0.25f;
                    var moved = Vector3.Distance(local.Position, lastPosForGate);
                    lastPosForGate = local.Position;

                    if (moved > moveCancelDist)
                    {
                        sitRequirementSatisfied = false;
                        hasLastPosForGate = false;
                        tracker.ResetCurrent();
                    }
                }

                emoteOk = sitRequirementSatisfied;
            }
        }

        bool hookActive = !cfg.RequireSitEmote || emoteOk;
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

        if (cfg.RequireSitEmote && sitRequirementSatisfied)
        {
            if (!dbg.HasValue || string.IsNullOrEmpty(dbg.Value.CandidateName))
            {
                sitRequirementSatisfied = false;
                hasLastPosForGate = false;
                tracker.ResetCurrent();
            }
        }

        LastDebugInfo = dbg;

        if (DebugEnabled)
        {
            var now = (float)ImGui.GetTime();

            if (now >= debugNextLogAt)
            {
                debugNextLogAt = now + DebugLogInterval;
                var dbgEmoteAge = (DateTime.UtcNow - emoteHook.LastEmoteUtc).TotalSeconds;
                var dbgLastEmote = emoteHook.LastEmoteId;

                if (dbg.HasValue)
                {
                    var d = dbg.Value;
                    log.Debug($"[LapCatCounter DBG] hookActive={hookActive} hookReady={emoteHook.HookReady} " +
                              $"cand={d.CandidateName} dist3={d.Distance3D:0.00} horizXZ={d.HorizontalXZ:0.00} " +
                              $"dx={d.Dx:0.00} dz={d.Dz:0.00} dy={d.Dy:0.00} " +
                              $"passR={d.PassRadius} passXY={d.PassXY} passZ={d.PassZ} " +
                              $"stable={d.StableSeconds:0.00}/{cfg.StableSecondsToCount:0.00} counted={d.CountedThisGate} " +
                              $"reason={d.Reason} " +
                              $" lastEmote={dbgLastEmote} age={dbgEmoteAge:0.00}s cfgSit={cfg.SitEmoteId} cfgGSit={cfg.GroundSitEmoteId}");
                }
                else
                {
                    log.Debug($"[LapCatCounter DBG] hookActive={hookActive} hookReady={emoteHook.HookReady} (no debug info)");
                }
            }

            if (now >= debugNextChatAt)
            {
                debugNextChatAt = now + DebugChatInterval;
                var dbgEmoteAge = (DateTime.UtcNow - emoteHook.LastEmoteUtc).TotalSeconds;
                var dbgLastEmote = emoteHook.LastEmoteId;

                if (dbg.HasValue)
                {
                    var d = dbg.Value;
                    chat.Print($"[LapDBG] hookActive={hookActive} hookReady={emoteHook.HookReady} best={d.CandidateName} " +
                               $"dist3={d.Distance3D:0.00} horizXZ={d.HorizontalXZ:0.00} dx={d.Dx:0.00} dz={d.Dz:0.00} dy={d.Dy:0.00} " +
                               $"R={cfg.Radius:0.00} XY={cfg.XYThreshold:0.00} Z=[{cfg.MinZAbove:0.00},{cfg.MaxZAbove:0.00}] " +
                               $"pass(R/XY/Z)={d.PassRadius}/{d.PassXY}/{d.PassZ} stable={d.StableSeconds:0.0}/{cfg.StableSecondsToCount:0.0}" +
                               $" lastEmote={dbgLastEmote} age={dbgEmoteAge:0.00}s cfgSit={cfg.SitEmoteId} cfgGSit={cfg.GroundSitEmoteId}");
                }
                else
                {
                    chat.Print($"[LapDBG] hookActive={hookActive} hookReady={emoteHook.HookReady} (no candidate)");
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
        SaveConfig();
        framework.Update -= OnUpdate;
        pi.UiBuilder.Draw -= Draw;

        commands.RemoveHandler("/lapcat");
        commands.RemoveHandler("/lapcatcount");
        commands.RemoveHandler("/lapdebug");

        ws.RemoveAllWindows();
        emoteHook.Dispose();
    }
}
