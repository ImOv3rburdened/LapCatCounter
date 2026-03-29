using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Linq;

namespace LapCatCounter;

public sealed class LapCatCounterPlugin : IDalamudPlugin
{
    public string Name => "Lap Cat Counter";

    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IObjectTable objects;
    private readonly IFramework framework;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly IClientState clientState;

    private readonly WindowSystem ws = new("LapCatCounter");

    private readonly Configuration cfg;
    private readonly LapTracker tracker;
    private readonly MainWindow window;
    private readonly EmoteHook emoteHook;

    internal bool DebugEnabled { get; private set; }
    internal bool DebugOverlayEnabled { get; set; } = true;
    internal bool LastHookActive { get; private set; }
    internal LapDebugInfo? LastDebugInfo { get; private set; }

    private float debugNextChatAt;
    private float debugNextLogAt;
    private const float DebugChatInterval = 1.0f;
    private const float DebugLogInterval = 0.25f;
    private bool wasInGpose;
    private DateTime gposeEnteredUtc = DateTime.MinValue;
    private const double GposeSuppressSeconds = 1.5;

    public LapCatCounterPlugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IObjectTable objectTable,
        IGameGui gameGui,
        IFramework framework,
        IClientState clientState,
        IChatGui chatGui,
        ISigScanner sigScanner,
        IPluginLog log,
        IDataManager dataManager,
        IGameInteropProvider interopProvider)
    {
        pi = pluginInterface;
        commands = commandManager;
        objects = objectTable;
        this.framework = framework;
        chat = chatGui;
        this.clientState = clientState;
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

        if (cfg.RequireSitEmote && (cfg.SitEmoteId == 0 || cfg.GroundSitEmoteId == 0))
        {
            if (EmoteIdResolver.TryResolveSitIds(dataManager, log, out var sitId, out var groundId))
            {
                if (cfg.SitEmoteId == 0)
                    cfg.SitEmoteId = sitId;
                if (cfg.GroundSitEmoteId == 0)
                    cfg.GroundSitEmoteId = groundId;

                changed = true;
            }
            else
            {
                log.Warning("[LapCatCounter] Could not auto-resolve /sit and /groundsit IDs (Lumina lookup failed).");
            }
        }

        if (changed)
        {
            pi.SavePluginConfig(cfg);
            log.Information($"[LapCatCounter] Using sit IDs: /sit={cfg.SitEmoteId}, /groundsit={cfg.GroundSitEmoteId}");
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
        args = (args ?? string.Empty).Trim().ToLowerInvariant();

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
        if (dt <= 0)
            dt = 1f / 60f;

        bool inGpose = clientState.IsGPosing;
        if (inGpose && !wasInGpose)
            gposeEnteredUtc = DateTime.UtcNow;

        wasInGpose = inGpose;

        bool suppressGpose = inGpose && (DateTime.UtcNow - gposeEnteredUtc).TotalSeconds < GposeSuppressSeconds;
        if (suppressGpose || inGpose)
        {
            tracker.ResetCurrent();
            LastDebugInfo = null;
            LastHookActive = false;
            return;
        }

        var others = objects
            .OfType<IPlayerCharacter>()
            .Where(p => p.Address != local.Address)
            .ToList();

        tracker.Update(dt, local, others, emoteHook, () =>
        {
            SaveConfig();
            var who = tracker.CurrentLapDisplayName;
            var countOnThem = tracker.GetCountFor(tracker.CurrentLapKey);

            if (tracker.CurrentRole == LapInteractionRole.SittingInOtherLap)
                chat.Print($"[Lap Cat Counter] You sat in {who}'s lap! You have {countOnThem} recorded lap session(s) with them.");
            else if (tracker.CurrentRole == LapInteractionRole.OtherSittingInMyLap)
                chat.Print($"[Lap Cat Counter] {who} sat in your lap! You have {countOnThem} recorded lap session(s) with them.");
        }, out var dbg);

        LastHookActive = emoteHook.HookReady;
        LastDebugInfo = dbg;

        if (!DebugEnabled)
            return;

        var now = (float)ImGui.GetTime();
        var recentEmote = emoteHook.LastRelevantEvent;
        var recentEmoteAge = recentEmote.HasValue ? (DateTime.UtcNow - recentEmote.Value.TimestampUtc).TotalSeconds : -1d;
        var direction = !recentEmote.HasValue
            ? "none"
            : recentEmote.Value.InstigatorIsLocal
                ? "local->target"
                : recentEmote.Value.TargetIsLocal
                    ? "other->local"
                    : "non-local";

        if (now >= debugNextLogAt)
        {
            debugNextLogAt = now + DebugLogInterval;

            if (dbg.HasValue)
            {
                var d = dbg.Value;
                log.Debug($"[LapCatCounter DBG] hookReady={emoteHook.HookReady} role={d.CurrentRole} status={d.CurrentStatus} partner={d.CandidateName} " +
                          $"dist3={d.Distance3D:0.00} horizXZ={d.HorizontalXZ:0.00} vertical={d.VerticalDelta:0.00} " +
                          $"passR={d.PassRadius} passXY={d.PassXY} passVertical={d.PassVertical} localState={d.LocalStateOk} partnerState={d.PartnerStateOk} " +
                          $"localMode={d.LocalMode} partnerMode={d.PartnerMode} stable={d.StableSeconds:0.00}/{cfg.StableSecondsToCount:0.00} " +
                          $"missing={d.MissingSeconds:0.00}/{cfg.SessionBreakGraceSeconds:0.00} reason={d.Reason} " +
                          $"lastEmote={(recentEmote?.EmoteId ?? 0)} emoteAge={recentEmoteAge:0.00}s direction={direction} instigator={recentEmote?.InstigatorName ?? ""} target={recentEmote?.TargetName ?? ""}");
            }
            else
            {
                log.Debug($"[LapCatCounter DBG] hookReady={emoteHook.HookReady} lastEmote={(recentEmote?.EmoteId ?? 0)} emoteAge={recentEmoteAge:0.00}s direction={direction} (no detector state)");
            }
        }

        if (now >= debugNextChatAt)
        {
            debugNextChatAt = now + DebugChatInterval;

            if (dbg.HasValue)
            {
                var d = dbg.Value;
                chat.Print($"[LapDBG] role={d.CurrentRole} status={d.CurrentStatus} partner={d.CandidateName} dist3={d.Distance3D:0.00} " +
                           $"horizXZ={d.HorizontalXZ:0.00} vertical={d.VerticalDelta:0.00} checks(R/XY/V)={d.PassRadius}/{d.PassXY}/{d.PassVertical} " +
                           $"state(local/partner)={d.LocalStateOk}/{d.PartnerStateOk} stable={d.StableSeconds:0.0}/{cfg.StableSecondsToCount:0.0} " +
                           $"missing={d.MissingSeconds:0.0}/{cfg.SessionBreakGraceSeconds:0.0} event={(recentEmote?.EmoteId ?? 0)} {direction} reason={d.Reason}");
            }
            else
            {
                chat.Print($"[LapDBG] hookReady={emoteHook.HookReady} event={(recentEmote?.EmoteId ?? 0)} {direction} (no detector state)");
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

