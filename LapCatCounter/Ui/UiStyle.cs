using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace LapCatCounter;

internal static class UiStyle
{
    private static int depth;
    private static int pushedVars;
    private static int pushedColors;

    public static void PushWindowDefaults()
    {
        depth++;
        if (depth != 1) return;

        pushedVars = 0;
        pushedColors = 0;

        var s = ImGuiHelpers.GlobalScale;

        PushVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 12) * s);
        PushVar(ImGuiStyleVar.FramePadding, new Vector2(10, 6) * s);
        PushVar(ImGuiStyleVar.ItemSpacing, new Vector2(10, 8) * s);
        PushVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(8, 6) * s);

        PushVar(ImGuiStyleVar.WindowRounding, 12 * s);
        PushVar(ImGuiStyleVar.ChildRounding, 12 * s);
        PushVar(ImGuiStyleVar.FrameRounding, 10 * s);
        PushVar(ImGuiStyleVar.PopupRounding, 12 * s);
        PushVar(ImGuiStyleVar.ScrollbarRounding, 12 * s);
        PushVar(ImGuiStyleVar.GrabRounding, 10 * s);

        var windowBg = new Vector4(0.12f, 0.11f, 0.15f, 0.92f);
        var childBg = new Vector4(0.12f, 0.11f, 0.15f, 0.55f);
        var popupBg = new Vector4(0.12f, 0.11f, 0.15f, 0.98f);

        var titleBg = new Vector4(0.25f, 0.21f, 0.33f, 0.95f);
        var titleBgActive = new Vector4(0.48f, 0.40f, 0.62f, 1.00f);
        var titleBgColl = new Vector4(0.22f, 0.19f, 0.30f, 0.90f);
        var menuBarBg = new Vector4(0.18f, 0.16f, 0.24f, 0.92f);

        var frame = new Vector4(0.22f, 0.20f, 0.28f, 0.88f);
        var frameHover = new Vector4(0.30f, 0.26f, 0.38f, 0.92f);
        var frameAct = new Vector4(0.36f, 0.30f, 0.46f, 0.95f);

        var btn = new Vector4(0.26f, 0.23f, 0.34f, 0.88f);
        var btnHover = new Vector4(0.34f, 0.28f, 0.44f, 0.92f);
        var btnAct = new Vector4(0.40f, 0.32f, 0.52f, 0.98f);

        var header = new Vector4(0.26f, 0.23f, 0.34f, 0.70f);
        var headerHover = new Vector4(0.34f, 0.28f, 0.44f, 0.82f);
        var headerAct = new Vector4(0.40f, 0.32f, 0.52f, 0.92f);

        var tab = new Vector4(0.20f, 0.18f, 0.26f, 0.88f);
        var tabHover = new Vector4(0.32f, 0.28f, 0.40f, 0.92f);
        var tabActive = new Vector4(0.38f, 0.32f, 0.48f, 0.98f);
        var tabUnfocus = new Vector4(0.18f, 0.16f, 0.24f, 0.78f);

        var border = new Vector4(1f, 1f, 1f, 0.08f);
        var sep = new Vector4(1f, 1f, 1f, 0.08f);
        var sepHover = new Vector4(1f, 1f, 1f, 0.14f);
        var sepAct = new Vector4(1f, 1f, 1f, 0.18f);

        var text = new Vector4(0.95f, 0.95f, 0.97f, 1.00f);
        var textDis = new Vector4(0.72f, 0.72f, 0.78f, 1.00f);

        var accent = new Vector4(0.78f, 0.66f, 0.98f, 1.00f);

        var scrollBg = new Vector4(0.00f, 0.00f, 0.00f, 0.25f);
        var scrollGrab = new Vector4(0.32f, 0.28f, 0.40f, 0.88f);
        var scrollHov = new Vector4(0.38f, 0.32f, 0.48f, 0.92f);
        var scrollAct = new Vector4(0.44f, 0.36f, 0.58f, 0.98f);

        PushColor(ImGuiCol.WindowBg, windowBg);
        PushColor(ImGuiCol.ChildBg, childBg);
        PushColor(ImGuiCol.PopupBg, popupBg);

        PushColor(ImGuiCol.TitleBg, titleBg);
        PushColor(ImGuiCol.TitleBgActive, titleBgActive);
        PushColor(ImGuiCol.TitleBgCollapsed, titleBgColl);
        PushColor(ImGuiCol.MenuBarBg, menuBarBg);

        PushColor(ImGuiCol.Text, text);
        PushColor(ImGuiCol.TextDisabled, textDis);

        PushColor(ImGuiCol.FrameBg, frame);
        PushColor(ImGuiCol.FrameBgHovered, frameHover);
        PushColor(ImGuiCol.FrameBgActive, frameAct);

        PushColor(ImGuiCol.Button, btn);
        PushColor(ImGuiCol.ButtonHovered, btnHover);
        PushColor(ImGuiCol.ButtonActive, btnAct);

        PushColor(ImGuiCol.Header, header);
        PushColor(ImGuiCol.HeaderHovered, headerHover);
        PushColor(ImGuiCol.HeaderActive, headerAct);

        PushColor(ImGuiCol.Tab, tab);
        PushColor(ImGuiCol.TabHovered, tabHover);
        PushColor(ImGuiCol.TabActive, tabActive);
        PushColor(ImGuiCol.TabUnfocused, tabUnfocus);
        PushColor(ImGuiCol.TabUnfocusedActive, tabActive);

        PushColor(ImGuiCol.Border, border);
        PushColor(ImGuiCol.Separator, sep);
        PushColor(ImGuiCol.SeparatorHovered, sepHover);
        PushColor(ImGuiCol.SeparatorActive, sepAct);

        PushColor(ImGuiCol.ScrollbarBg, scrollBg);
        PushColor(ImGuiCol.ScrollbarGrab, scrollGrab);
        PushColor(ImGuiCol.ScrollbarGrabHovered, scrollHov);
        PushColor(ImGuiCol.ScrollbarGrabActive, scrollAct);

        PushColor(ImGuiCol.CheckMark, accent);
        PushColor(ImGuiCol.SliderGrab, accent);
        PushColor(ImGuiCol.SliderGrabActive, accent);

        PushColor(ImGuiCol.TableHeaderBg, new Vector4(0.18f, 0.16f, 0.24f, 1.00f));
        PushColor(ImGuiCol.TableBorderStrong, new Vector4(1f, 1f, 1f, 0.10f));
        PushColor(ImGuiCol.TableBorderLight, new Vector4(1f, 1f, 1f, 0.06f));
        PushColor(ImGuiCol.TableRowBg, new Vector4(0f, 0f, 0f, 0.00f));
        PushColor(ImGuiCol.TableRowBgAlt, new Vector4(1f, 1f, 1f, 0.03f));

        PushColor(ImGuiCol.NavHighlight, new Vector4(accent.X, accent.Y, accent.Z, 0.35f));
        PushColor(ImGuiCol.ResizeGrip, new Vector4(accent.X, accent.Y, accent.Z, 0.18f));
        PushColor(ImGuiCol.ResizeGripHovered, new Vector4(accent.X, accent.Y, accent.Z, 0.35f));
        PushColor(ImGuiCol.ResizeGripActive, new Vector4(accent.X, accent.Y, accent.Z, 0.50f));
    }

    public static void PopWindowDefaults()
    {
        if (depth == 0) return;
        depth--;
        if (depth != 0) return;

        if (pushedColors > 0) ImGui.PopStyleColor(pushedColors);
        if (pushedVars > 0) ImGui.PopStyleVar(pushedVars);

        pushedColors = 0;
        pushedVars = 0;
    }

    private static void PushVar(ImGuiStyleVar var, Vector2 v)
    {
        ImGui.PushStyleVar(var, v);
        pushedVars++;
    }

    private static void PushVar(ImGuiStyleVar var, float v)
    {
        ImGui.PushStyleVar(var, v);
        pushedVars++;
    }

    private static void PushColor(ImGuiCol col, Vector4 v)
    {
        ImGui.PushStyleColor(col, v);
        pushedColors++;
    }
}
