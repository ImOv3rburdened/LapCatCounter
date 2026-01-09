using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace LapCatCounter;

public sealed class MainWindow : Window
{
    private readonly Configuration cfg;
    private readonly LapTracker tracker;
    private readonly Action save;
    private readonly EmoteHook? emoteHook;
    private readonly LapCatCounterPlugin plugin;
    private readonly LapTracker satOnYouTracker;

    private string search = "";
    private bool sortByLapsDesc = true;
    private bool sortByNameAsc = true;
    private bool showOnlyRecent = false;
    private int recentMinutes = 60;
    private bool openResetPopupThisFrame;

    private bool openResetAllPopupThisFrame;
    private static bool resetAllModalOpen = true;

    private string? pendingResetKey;
    private DateTime? pendingResetAtUtc;
    private static bool resetModalOpen = true;

    public MainWindow(Configuration cfg, LapTracker tracker, LapTracker satOnYouTracker, Action save, EmoteHook emoteHook, LapCatCounterPlugin plugin)
        : base("Lap Cat Counter")
    {
        this.cfg = cfg;
        this.tracker = tracker;
        this.satOnYouTracker = satOnYouTracker;
        this.save = save;
        this.emoteHook = emoteHook;
        this.plugin = plugin;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 460),
            MaximumSize = new Vector2(1200, 1000),
        };
        RespectCloseHotkey = true;
    }

    public override void PreDraw()
    {
        UiStyle.PushWindowDefaults();
        base.PreDraw();
    }

    public override void PostDraw()
    {
        base.PostDraw();
        UiStyle.PopWindowDefaults();
    }

    public override void Draw()
    {
        DrawHeader();

        ImGui.Spacing();
        DrawTopCards();

        ImGui.Spacing();
        using var tabs = ImRaii.TabBar("lapcat.tabs", ImGuiTabBarFlags.None);
        if (tabs)
        {
            if (ImGui.BeginTabItem("People"))
            {
                DrawPeopleTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Settings"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("About"))
            {
                DrawAboutTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Debug"))
            {
                DrawDebugTab();
                ImGui.EndTabItem();
            }
        }

        if (openResetPopupThisFrame)
        {
            ImGui.OpenPopup("lapcat.reset.modal");
            openResetPopupThisFrame = false;
        }

        if (openResetAllPopupThisFrame)
        {
            ImGui.OpenPopup("lapcat.resetall.modal");
            openResetAllPopupThisFrame = false;
        }

        DrawResetModal();
        DrawResetAllModal();
    }

    private void DrawHeader()
    {
        using var header = ImRaii.Child("lapcat.header", new Vector2(0, 64 * ImGuiHelpers.GlobalScale), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var titlePos = ImGui.GetCursorPos();

        UiWidgets.AccentBar(new Vector4(0.78f, 0.66f, 0.98f, 1.00f));

        ImGui.SameLine(0, 14 * ImGuiHelpers.GlobalScale);
        ImGui.SetCursorPosY(titlePos.Y + 6 * ImGuiHelpers.GlobalScale);
        ImGui.PushFont(UiBuilder.DefaultFont);
        ImGui.TextUnformatted("Lap Cat Counter");
        ImGui.PopFont();

        ImGui.SameLine();
        UiWidgets.Pill("CHAT MODE", ImGuiColors.HealerGreen);

        ImGui.SetCursorPosY(titlePos.Y + 38 * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
        ImGui.TextUnformatted("Track laps you sit in • /lapcat to open • /lapcatcount to print totals • /lapdebug");
        ImGui.PopStyleColor();
    }

    private void DrawTopCards()
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var gap = 10f * ImGuiHelpers.GlobalScale;
        var cardW = (avail - gap) / 2f;
        var cardH = 84f * ImGuiHelpers.GlobalScale;

        UiWidgets.StatCard(
            id: "total",
            size: new Vector2(cardW, cardH),
            label: "Sat in Laps",
            value: tracker.TotalLaps.ToString(CultureInfo.InvariantCulture),
            accent: new Vector4(0.78f, 0.66f, 0.98f, 1.00f),
            footer: "Times you sat on others");

        ImGui.SameLine(0, gap);

        UiWidgets.StatCard(
            id: "satonyou",
            size: new Vector2(cardW, cardH),
            label: "Sat on You",
            value: satOnYouTracker.TotalSatOnYou.ToString(CultureInfo.InvariantCulture),
            accent: ImGuiColors.HealerGreen,
            footer: "Times others sat on you");

        ImGui.SameLine(0, gap);

        UiWidgets.StatCard(
            id: "unique",
            size: new Vector2(cardW, cardH),
            label: "Unique People",
            value: tracker.UniquePeople.ToString(CultureInfo.InvariantCulture),
            accent: ImGuiColors.TankBlue,
            footer: "People you’ve interacted with");

        ImGui.SameLine(0, gap);

        var current = string.IsNullOrWhiteSpace(tracker.CurrentLapDisplayName) ? "None" : tracker.CurrentLapDisplayName;
        UiWidgets.StatCard(
            id: "current",
            size: new Vector2(cardW, cardH),
            label: "Current Candidate",
            value: current,
            accent: ImGuiColors.HealerGreen,
            footer: "Who you’re sitting on (candidate)");
    }

    private void DrawPeopleTab()
    {
        using (var toolbar = ImRaii.Child("lapcat.people.toolbar", new Vector2(0, 58 * ImGuiHelpers.GlobalScale), false,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Search");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
            ImGui.InputTextWithHint("##lapcat.search", "Name or key…", ref search, 120);

            ImGui.SameLine();
            bool onlyRecent = showOnlyRecent;
            if (ImGui.Checkbox("Recent only", ref onlyRecent))
                showOnlyRecent = onlyRecent;

            ImGui.SameLine();
            ImGui.BeginDisabled(!showOnlyRecent);
            ImGui.SetNextItemWidth(90 * ImGuiHelpers.GlobalScale);
            int mins = recentMinutes;
            if (ImGui.InputInt("min", ref mins, 1, 10))
                recentMinutes = Math.Clamp(mins, 1, 60 * 24 * 7);
            ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();

            if (UiWidgets.SmallPillButton(sortByLapsDesc ? "Sort: Laps ↓" : "Sort: Laps ↑", new Vector4(0.78f, 0.66f, 0.98f, 1.00f)))
            {
                sortByLapsDesc = !sortByLapsDesc;
                sortByNameAsc = false;
            }

            ImGui.SameLine();

            if (UiWidgets.SmallPillButton(sortByNameAsc ? "Sort: Name A→Z" : "Sort: Name Z→A", ImGuiColors.TankBlue))
            {
                sortByNameAsc = !sortByNameAsc;
                sortByLapsDesc = false;
            }

            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();

            if (UiWidgets.SmallPillButton("Copy totals", ImGuiColors.DalamudGrey2))
                ImGui.SetClipboardText(BuildTotalsText());

            ImGui.SameLine();

            if (UiWidgets.SmallPillButton("Reset all…", ImGuiColors.DalamudRed))
                openResetAllPopupThisFrame = true;
        }

        ImGui.Spacing();

        var rows = tracker.TopPeople(200);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            rows = rows
                .Where(p => p.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            (p.Key?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        if (showOnlyRecent)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-Math.Abs(recentMinutes));
            rows = rows.Where(p => p.LastLapUtc >= cutoff).ToList();
        }

        if (sortByLapsDesc)
            rows = rows.OrderByDescending(p => p.LapCount).ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        else if (sortByNameAsc)
            rows = rows.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ThenByDescending(p => p.LapCount).ToList();
        else
            rows = rows.OrderByDescending(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ThenByDescending(p => p.LapCount).ToList();

        var avail = ImGui.GetContentRegionAvail();
        using var table = ImRaii.Table("lapcat.people.table", 6,
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.BordersOuter |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingStretchProp,
            new Vector2(avail.X, avail.Y));

        if (!table) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 40 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Sat In", ImGuiTableColumnFlags.WidthFixed, 80 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Sat on You", ImGuiTableColumnFlags.WidthFixed, 100 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Last Lap", ImGuiTableColumnFlags.WidthFixed, 170 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 220 * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        int idx = 0;
        foreach (var p in rows)
        {
            idx++;

            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(idx.ToString(CultureInfo.InvariantCulture));

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(p.DisplayName);

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(p.LapCount.ToString(CultureInfo.InvariantCulture));

            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted(p.SatOnYouCount.ToString(CultureInfo.InvariantCulture));

            ImGui.TableSetColumnIndex(4);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey2);
            ImGui.TextUnformatted(FormatLastLap(p.LastLapUtc));
            ImGui.PopStyleColor();

            ImGui.TableSetColumnIndex(5);

            if (UiWidgets.SmallPillButton("Copy", ImGuiColors.DalamudGrey2))
                ImGui.SetClipboardText(p.DisplayName);

            ImGui.SameLine();

            if (UiWidgets.SmallPillButton("Key", ImGuiColors.DalamudGrey2))
                ImGui.SetClipboardText(p.Key ?? "");

            ImGui.SameLine();

            if (UiWidgets.SmallPillButton("Reset", ImGuiColors.DalamudRed))
            {
                pendingResetKey = p.Key;
                pendingResetAtUtc = DateTime.UtcNow;
                openResetPopupThisFrame = true;
            }
        }
    }

    private void DrawSettingsTab()
    {
        ImGui.TextUnformatted("Settings");
        ImGui.Separator();

        bool require = cfg.RequireSitEmote;
        if (ImGui.Checkbox("Require /sit or /groundsit before counting", ref require))
        {
            cfg.RequireSitEmote = require;
            save();
        }

        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudGrey2, "Tip: IDs auto-detect on startup. Only set manually if needed.");
    }

    private void DrawAboutTab()
    {
        ImGui.TextUnformatted("About");
        ImGui.Separator();
        ImGui.TextUnformatted("Lap Cat Counter counts how many laps you sit in :3");
    }

    private void DrawDebugTab()
    {
        ImGui.TextUnformatted("Debug");
        ImGui.Separator();

        ImGui.TextUnformatted($"Hook ready: {(emoteHook?.HookReady ?? false)}");
        ImGui.TextUnformatted($"Hook active: {plugin.LastHookActive}");

        if (plugin.LastDebugInfo.HasValue)
        {
            var d = plugin.LastDebugInfo.Value;
            ImGui.Spacing();
            ImGui.TextUnformatted($"Candidate: {d.CandidateName}");
            ImGui.TextUnformatted($"dist3={d.Distance3D:0.00} horizXZ={d.HorizontalXZ:0.00}");
            ImGui.TextUnformatted($"dx={d.Dx:0.00} dz={d.Dz:0.00} dy={d.Dy:0.00}");
            ImGui.TextUnformatted($"passR={d.PassRadius} passXY={d.PassXY} passZ={d.PassZ}");
            ImGui.TextUnformatted($"stable={d.StableSeconds:0.00} counted={d.CountedThisGate}");
            ImGui.TextUnformatted($"reason={d.Reason}");
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("No debug info yet.");
        }
    }

    private void DrawResetModal()
    {
        using var popup = ImRaii.PopupModal("lapcat.reset.modal", ref resetModalOpen,
            ImGuiWindowFlags.AlwaysAutoResize);

        if (!popup) return;

        ImGui.TextUnformatted("Reset this person's lap count?");
        ImGui.Spacing();

        if (UiWidgets.SmallPillButton("Cancel", ImGuiColors.DalamudGrey2))
        {
            ImGui.CloseCurrentPopup();
            pendingResetKey = null;
            pendingResetAtUtc = null;
        }

        ImGui.SameLine();

        if (UiWidgets.SmallPillButton("Reset", ImGuiColors.DalamudRed))
        {
            if (!string.IsNullOrWhiteSpace(pendingResetKey))
            {
                cfg.People.Remove(pendingResetKey);
                save();
            }

            ImGui.CloseCurrentPopup();
            pendingResetKey = null;
            pendingResetAtUtc = null;
        }
    }

    private void DrawResetAllModal()
    {
        using var popup = ImRaii.PopupModal("lapcat.resetall.modal", ref resetAllModalOpen,
            ImGuiWindowFlags.AlwaysAutoResize);

        if (!popup) return;

        ImGui.TextUnformatted("Reset ALL lap counts?");
        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudGrey2, "This cannot be undone.");

        ImGui.Spacing();

        if (UiWidgets.SmallPillButton("Cancel", ImGuiColors.DalamudGrey2))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (UiWidgets.SmallPillButton("Reset ALL", ImGuiColors.DalamudRed))
        {
            cfg.People.Clear();
            save();
            tracker.ResetCurrent();
            ImGui.CloseCurrentPopup();
        }
    }

    private string BuildTotalsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Lap Cat Counter Totals");
        sb.AppendLine($"Sat in laps: {tracker.TotalLaps}");
        sb.AppendLine($"Sat on you: {satOnYouTracker.TotalSatOnYou}");
        sb.AppendLine($"Unique People: {tracker.UniquePeople}");
        sb.AppendLine();
        sb.AppendLine("People (SatIn / SatOnYou):");

        foreach (var p in tracker.TopPeople(200).OrderByDescending(x => x.LapCount))
            sb.AppendLine($"{p.DisplayName}: {p.LapCount} / {p.SatOnYouCount}");

        return sb.ToString();
    }

    private static string FormatLastLap(DateTime utc)
    {
        if (utc == DateTime.MinValue)
            return "—";

        var now = DateTime.UtcNow;
        var delta = now - utc;

        if (delta.TotalSeconds < 60) return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";

        return utc.ToLocalTime().ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture);
    }
}
