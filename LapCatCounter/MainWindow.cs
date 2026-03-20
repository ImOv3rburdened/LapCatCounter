using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using LapCatCounter;
using LapCatCounter;
using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace LapCatCounter;

public sealed class MainWindow : Window
{
    private enum TopStatsView
    {
        Combined,
        SatIn,
        SatOn,
    }

    private readonly Configuration cfg;
    private readonly LapTracker tracker;
    private readonly Action save;
    private readonly EmoteHook? emoteHook;
    private readonly LapCatCounterPlugin plugin;

    private string search = "";
    private bool sortByLapsDesc = true;
    private bool sortByNameAsc = true;
    private bool showOnlyRecent = false;
    private int recentMinutes = 60;
    private bool openResetPopupThisFrame;
    private TopStatsView topStatsView = TopStatsView.Combined;

    private bool openResetAllPopupThisFrame;
    private static bool resetAllModalOpen = true;

    private string? pendingResetKey;
    private DateTime? pendingResetAtUtc;
    private static bool resetModalOpen = true;

    public MainWindow(Configuration cfg, LapTracker tracker, Action save, EmoteHook emoteHook, LapCatCounterPlugin plugin)
        : base("Lap Cat Counter")
    {
        this.cfg = cfg;
        this.tracker = tracker;
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

            if (ImGui.BeginTabItem("Directions"))
            {
                DrawDirectionsTab();
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

        var indentX = 14 * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosX(titlePos.X + indentX);
        ImGui.SetCursorPosY(titlePos.Y + 38 * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
        ImGui.TextUnformatted("Track laps you sit in • /lapcat to open • /lapcatcount to print totals • /lapdebug");
        ImGui.PopStyleColor();
    }

    private void DrawTopCards()
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var gap = 10f * ImGuiHelpers.GlobalScale;
        var cardW = (avail - gap * 2) / 3f;
        var cardH = 84f * ImGuiHelpers.GlobalScale;

        var currentPartner = string.IsNullOrWhiteSpace(tracker.CurrentBestCandidateKey) ? "None" : tracker.CurrentLapDisplayName;
        var sessionStarted = tracker.CurrentLapStartedUtc.HasValue
            ? tracker.CurrentLapStartedUtc.Value.ToLocalTime().ToString("h:mm tt", CultureInfo.InvariantCulture)
            : "Not active";

        string label1;
        string value1;
        string footer1;
        string label2;
        string value2;
        string footer2;
        string label4;
        string value4;
        string footer4;
        string label6;
        string value6;
        string footer6;

        switch (topStatsView)
        {
            case TopStatsView.SatIn:
                label1 = "Times I Sat In Laps";
                value1 = tracker.TotalTimesISatInTheirLaps.ToString(CultureInfo.InvariantCulture);
                footer1 = "All-time sit-in sessions";
                label2 = "Partners Sat In";
                value2 = cfg.People.Values.Count(p => p.TimesISatInTheirLap > 0).ToString(CultureInfo.InvariantCulture);
                footer2 = "People whose lap you used";
                label4 = "Time I Sat In Laps";
                value4 = UiWidgets.FormatDuration(tracker.TotalTimeISatInTheirLaps);
                footer4 = "All-time sit-in time";
                label6 = "Session Start";
                value6 = sessionStarted;
                footer6 = "Current sit-in tracking window";
                break;

            case TopStatsView.SatOn:
                label1 = "Times Sat In Mine";
                value1 = tracker.TotalTimesTheySatInMyLap.ToString(CultureInfo.InvariantCulture);
                footer1 = "All-time sat-on sessions";
                label2 = "Partners Sat On";
                value2 = cfg.People.Values.Count(p => p.TimesTheySatInMyLap > 0).ToString(CultureInfo.InvariantCulture);
                footer2 = "People who sat in your lap";
                label4 = "Time Sat In Mine";
                value4 = UiWidgets.FormatDuration(tracker.TotalTimeTheySatInMyLap);
                footer4 = "All-time sat-on time";
                label6 = "Session Start";
                value6 = sessionStarted;
                footer6 = "Current sat-on tracking window";
                break;

            default:
                label1 = "Total Sessions";
                value1 = tracker.TotalLaps.ToString(CultureInfo.InvariantCulture);
                footer1 = "All-time lap sessions";
                label2 = "Unique Partners";
                value2 = tracker.UniquePeople.ToString(CultureInfo.InvariantCulture);
                footer2 = "People in lap history";
                label4 = "Total Session Time";
                value4 = UiWidgets.FormatDuration(tracker.TotalLapTime);
                footer4 = "All-time lap time";
                label6 = "Longest Session";
                value6 = UiWidgets.FormatDuration(tracker.LongestLapTime);
                footer6 = "Personal record";
                break;
        }

        UiWidgets.StatCard(
            id: "top1",
            size: new Vector2(cardW, cardH),
            label: label1,
            value: value1,
            accent: new Vector4(0.78f, 0.66f, 0.98f, 1.00f),
            footer: footer1);

        ImGui.SameLine(0, gap);

        UiWidgets.StatCard(
            id: "top2",
            size: new Vector2(cardW, cardH),
            label: label2,
            value: value2,
            accent: ImGuiColors.TankBlue,
            footer: footer2);

        ImGui.SameLine(0, gap);

        UiWidgets.StatCard(
            id: "top3",
            size: new Vector2(cardW, cardH),
            label: "Current Partner",
            value: currentPartner,
            accent: ImGuiColors.HealerGreen,
            footer: $"{tracker.CurrentStatus}: {tracker.CurrentRole}");

        ImGui.Spacing();

        UiWidgets.StatCard(
            id: "top4",
            size: new Vector2(cardW, cardH),
            label: label4,
            value: value4,
            accent: new Vector4(0.98f, 0.75f, 0.86f, 1.00f),
            footer: footer4);

        ImGui.SameLine(0, gap);

        UiWidgets.StatCard(
            id: "top5",
            size: new Vector2(cardW, cardH),
            label: "Active Session",
            value: UiWidgets.FormatDuration(tracker.CurrentLapTime),
            accent: new Vector4(0.75f, 0.92f, 0.98f, 1.00f),
            footer: "Current uninterrupted session");

        ImGui.SameLine(0, gap);

        UiWidgets.StatCard(
            id: "top6",
            size: new Vector2(cardW, cardH),
            label: label6,
            value: value6,
            accent: new Vector4(0.85f, 0.98f, 0.75f, 1.00f),
            footer: footer6);

        ImGui.Spacing();
        using var summary = ImRaii.Child("lapcat.direction.summary", new Vector2(0, 56 * ImGuiHelpers.GlobalScale), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        ImGui.AlignTextToFramePadding();
        if (UiWidgets.SmallPillButton($"Toggle Stats: {GetTopStatsViewLabel()}", new Vector4(0.78f, 0.66f, 0.98f, 1.00f)))
            CycleTopStatsView();

        ImGui.SameLine(0, 12 * ImGuiHelpers.GlobalScale);
        ImGui.TextUnformatted("Toggle between combined, sat-in, and sat-on totals.");
    }

    private string GetTopStatsViewLabel()
        => topStatsView switch
        {
            TopStatsView.SatIn => "I Sat In Laps",
            TopStatsView.SatOn => "They Sat In Mine",
            _ => "Combined",
        };

    private void CycleTopStatsView()
    {
        topStatsView = topStatsView switch
        {
            TopStatsView.Combined => TopStatsView.SatIn,
            TopStatsView.SatIn => TopStatsView.SatOn,
            _ => TopStatsView.Combined,
        };
    }

    private void DrawPeopleTab()
    {
        DrawSharedToolbar();
        ImGui.Spacing();

        var rows = ApplyPeopleFilters(tracker.TopPeople(200));

        var avail = ImGui.GetContentRegionAvail();
        using var table = ImRaii.Table("lapcat.people.table", 7,
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
        ImGui.TableSetupColumn("Sessions", ImGuiTableColumnFlags.WidthFixed, 90 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Last Lap", ImGuiTableColumnFlags.WidthFixed, 170 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 220 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 120 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Longest", ImGuiTableColumnFlags.WidthFixed, 100 * ImGuiHelpers.GlobalScale);
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
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey2);
            ImGui.TextUnformatted(FormatLastLap(p.LastLapUtc));
            ImGui.PopStyleColor();

            ImGui.TableSetColumnIndex(4);
            DrawPersonActions(p, idx);

            ImGui.TableSetColumnIndex(5);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey2);
            ImGui.TextUnformatted(UiWidgets.FormatDuration(TimeSpan.FromSeconds(p.TotalLapSeconds)));
            ImGui.PopStyleColor();

            ImGui.TableSetColumnIndex(6);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey2);
            ImGui.TextUnformatted(UiWidgets.FormatDuration(TimeSpan.FromSeconds(p.LongestLapSeconds)));
            ImGui.PopStyleColor();
        }
    }

    private void DrawDirectionsTab()
    {
        DrawSharedToolbar();
        ImGui.Spacing();

        var rows = ApplyPeopleFilters(tracker.TopPeople(200));

        var avail = ImGui.GetContentRegionAvail();
        using var table = ImRaii.Table("lapcat.directions.table", 8,
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
        ImGui.TableSetupColumn("In Their Lap", ImGuiTableColumnFlags.WidthFixed, 100 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 120 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("In My Lap", ImGuiTableColumnFlags.WidthFixed, 100 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 120 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Last Lap", ImGuiTableColumnFlags.WidthFixed, 160 * ImGuiHelpers.GlobalScale);
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
            ImGui.TextUnformatted(p.TimesISatInTheirLap.ToString(CultureInfo.InvariantCulture));

            ImGui.TableSetColumnIndex(3);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey2);
            ImGui.TextUnformatted(UiWidgets.FormatDuration(TimeSpan.FromSeconds(p.TimeISatInTheirLapSeconds)));
            ImGui.PopStyleColor();

            ImGui.TableSetColumnIndex(4);
            ImGui.TextUnformatted(p.TimesTheySatInMyLap.ToString(CultureInfo.InvariantCulture));

            ImGui.TableSetColumnIndex(5);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey2);
            ImGui.TextUnformatted(UiWidgets.FormatDuration(TimeSpan.FromSeconds(p.TimeTheySatInMyLapSeconds)));
            ImGui.PopStyleColor();

            ImGui.TableSetColumnIndex(6);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey2);
            ImGui.TextUnformatted(FormatLastLap(p.LastLapUtc));
            ImGui.PopStyleColor();

            ImGui.TableSetColumnIndex(7);
            DrawPersonActions(p, idx);
        }
    }

    private void DrawSharedToolbar()
    {
        using var toolbar = ImRaii.Child("lapcat.people.toolbar", new Vector2(0, 58 * ImGuiHelpers.GlobalScale), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

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

        if (UiWidgets.SmallPillButton(sortByLapsDesc ? "Sort: Sessions ?" : "Sort: Sessions ?", new Vector4(0.78f, 0.66f, 0.98f, 1.00f)))
        {
            sortByLapsDesc = !sortByLapsDesc;
            sortByNameAsc = false;
        }

        ImGui.SameLine();

        if (UiWidgets.SmallPillButton(sortByNameAsc ? "Sort: Name A?Z" : "Sort: Name Z?A", ImGuiColors.TankBlue))
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

    private System.Collections.Generic.List<Configuration.PersonStats> ApplyPeopleFilters(System.Collections.Generic.IReadOnlyList<Configuration.PersonStats> source)
    {
        var rows = source.ToList();

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

        return rows;
    }

    private void DrawPersonActions(Configuration.PersonStats p, int idx)
    {
        ImGui.PushID(p.Key ?? p.DisplayName ?? idx.ToString(CultureInfo.InvariantCulture));

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

        ImGui.PopID();
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
    }

    private void DrawAboutTab()
    {
        ImGui.TextUnformatted("About");
        ImGui.Separator();
        ImGui.TextUnformatted("Lap Cat Counter counts how many laps you sit in and for how long :3");
    }

    private void DrawDebugTab()
    {
        ImGui.TextUnformatted("Debug");
        ImGui.Separator();

        ImGui.TextUnformatted($"Hook ready: {(emoteHook?.HookReady ?? false)}");
        ImGui.TextUnformatted($"Detection active: {plugin.LastHookActive}");

        if (emoteHook?.LastRelevantEvent is { } evt)
        {
            var age = (DateTime.UtcNow - evt.TimestampUtc).TotalSeconds;
            ImGui.Spacing();
            ImGui.TextUnformatted("Recent Sit Event");
            ImGui.TextUnformatted($"emote={evt.EmoteId} age={age:0.00}s");
            ImGui.TextUnformatted($"instigator={evt.InstigatorName} target={evt.TargetName}");
            ImGui.TextUnformatted($"direction={(evt.InstigatorIsLocal ? "Local -> Target" : evt.TargetIsLocal ? "Other -> Local" : "Not local")}");
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Recent Sit Event");
            ImGui.TextUnformatted("No local sit event captured yet.");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Detection Result");

        if (plugin.LastDebugInfo.HasValue)
        {
            var d = plugin.LastDebugInfo.Value;
            ImGui.TextUnformatted($"Partner: {(string.IsNullOrWhiteSpace(d.CandidateName) ? "None" : d.CandidateName)}");
            ImGui.TextUnformatted($"role={d.CurrentRole} status={d.CurrentStatus}");
            ImGui.TextUnformatted($"localMode={d.LocalMode}");
            ImGui.TextUnformatted($"partnerMode={d.PartnerMode}");
            ImGui.TextUnformatted($"state(local/partner)={d.LocalStateOk}/{d.PartnerStateOk}");
            ImGui.TextUnformatted($"dist3={d.Distance3D:0.00} horizXZ={d.HorizontalXZ:0.00} vertical={d.VerticalDelta:0.00}");
            ImGui.TextUnformatted($"checks(radius/xy/vertical)={d.PassRadius}/{d.PassXY}/{d.PassVertical}");
            ImGui.TextUnformatted($"stable={d.StableSeconds:0.00}/{cfg.StableSecondsToCount:0.00}");
            ImGui.TextUnformatted($"missing={d.MissingSeconds:0.00}/{cfg.SessionBreakGraceSeconds:0.00}");
            ImGui.TextUnformatted($"detectorReason={d.Reason}");
        }
        else
        {
            ImGui.TextUnformatted("No detector state available yet.");
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
                tracker.ResetCurrent();
                tracker.RecalculateTotalsFromPeople();
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
            tracker.ResetAllTotals();
            save();
            ImGui.CloseCurrentPopup();
        }
    }

    private string BuildTotalsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Lap Cat Counter Totals");
        sb.AppendLine($"Total Sessions: {tracker.TotalLaps}");
        sb.AppendLine($"Unique Partners: {tracker.UniquePeople}");
        sb.AppendLine($"I Sat In Laps: {tracker.TotalTimesISatInTheirLaps} ({UiWidgets.FormatDuration(tracker.TotalTimeISatInTheirLaps)})");
        sb.AppendLine($"They Sat In Mine: {tracker.TotalTimesTheySatInMyLap} ({UiWidgets.FormatDuration(tracker.TotalTimeTheySatInMyLap)})");
        sb.AppendLine();

        foreach (var p in tracker.TopPeople(200).OrderByDescending(x => x.LapCount))
            sb.AppendLine($"{p.DisplayName}: {p.LapCount}");

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

