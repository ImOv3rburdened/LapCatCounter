using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace LapCatCounter;

internal static class UiWidgets
{
    public static void AccentBar(Vector4 color)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var p = ImGui.GetCursorScreenPos();
        var h = 48f * scale;
        var w = 6f * scale;

        ImGui.GetWindowDrawList().AddRectFilled(
            p,
            new Vector2(p.X + w, p.Y + h),
            ImGui.ColorConvertFloat4ToU32(color),
            4f * scale
        );
    }

    public static void StatCard(string id, Vector2 size, string label, string value, Vector4 accent, string footer)
    {
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGuiColors.DalamudGrey3);

        using var child = ImRaii.Child($"lapcat.card.{id}", size, true,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        if (child)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
            ImGui.TextUnformatted(label);
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudWhite);
            ImGui.SetWindowFontScale(1.25f);
            ImGui.TextUnformatted(value);
            ImGui.SetWindowFontScale(1.0f);
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey2);
            ImGui.TextUnformatted(footer);
            ImGui.PopStyleColor();
        }

        ImGui.PopStyleColor();
    }

    public static void Pill(string text, Vector4 color)
    {
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.PushStyleColor(ImGuiCol.Button, color * new Vector4(1, 1, 1, 0.25f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color * new Vector4(1, 1, 1, 0.35f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, color * new Vector4(1, 1, 1, 0.45f));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudWhite);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 999f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 4) * scale);

        ImGui.Button(text);

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(4);
    }

    public static bool SmallPillButton(string label, Vector4 tint)
    {
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.PushStyleColor(ImGuiCol.Button, tint * new Vector4(1, 1, 1, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, tint * new Vector4(1, 1, 1, 0.28f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, tint * new Vector4(1, 1, 1, 0.38f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 999f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12, 6) * scale);

        var clicked = ImGui.Button(label);

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(3);
        return clicked;
    }

    public static void SectionHeader(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudWhite);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Separator, ImGuiColors.DalamudGrey3);
        ImGui.Separator();
        ImGui.PopStyleColor();
    }

    public static void Help(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey2);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    public static void MutedText(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey2);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    public static void BoldNumber(int n)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudWhite);
        ImGui.TextUnformatted(n.ToString());
        ImGui.PopStyleColor();
    }

    public static bool IconButton(string id, string iconText, Vector4 tint, string tooltip)
    {
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.PushStyleColor(ImGuiCol.Button, tint * new Vector4(1, 1, 1, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, tint * new Vector4(1, 1, 1, 0.28f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, tint * new Vector4(1, 1, 1, 0.38f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 10f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 6) * scale);

        var clicked = ImGui.Button($"{iconText}##{id}");

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(3);
        return clicked;
    }

    public static bool SecondaryButton(string label, Vector2? size = null)
    {
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DalamudGrey3);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiColors.DalamudGrey2);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiColors.DalamudGrey2);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 10f * scale);

        var clicked = size.HasValue ? ImGui.Button(label, size.Value) : ImGui.Button(label);

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        return clicked;
    }

    public static bool DangerButton(string label, string tooltip)
    {
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DPSRed * new Vector4(1, 1, 1, 0.25f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiColors.DPSRed * new Vector4(1, 1, 1, 0.35f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiColors.DPSRed * new Vector4(1, 1, 1, 0.45f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 10f * scale);

        var clicked = ImGui.Button(label);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        return clicked;
    }

    public static bool DangerConfirmButton(string label, Vector2? size = null)
    {
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DPSRed * new Vector4(1, 1, 1, 0.30f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiColors.DPSRed * new Vector4(1, 1, 1, 0.40f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiColors.DPSRed * new Vector4(1, 1, 1, 0.55f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 10f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(14, 8) * scale);

        var clicked = size.HasValue ? ImGui.Button(label, size.Value) : ImGui.Button(label);

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(3);
        return clicked;
    }

    public static void InlineKeyValue(string key, string value)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
        ImGui.TextUnformatted(key);
        ImGui.PopStyleColor();

        ImGui.SameLine(140 * ImGuiHelpers.GlobalScale);
        ImGui.TextUnformatted(value);
    }
}
