using Dalamud.Plugin.Services;
using System;
using System.Collections;
using System.Reflection;
using System.Text;
using Lumina.Excel.Sheets;

namespace LapCatCounter;

public static class EmoteIdResolver
{
    public static bool TryResolveSitIds(IDataManager data, IPluginLog log, out ushort sitId, out ushort groundSitId)
    {
        sitId = 0;
        groundSitId = 0;

        try
        {
            var sheet = data.GetExcelSheet<Emote>();
            if (sheet is null)
                return false;

            foreach (var emote in sheet)
            {
                string? cmd = null;

                try
                {
                    var tc = emote.TextCommand.Value;
                    var cmdObj = tc.Command;

                    cmd = ExtractCommandString(cmdObj);
                }
                catch
                {
                    cmd = null;
                }

                if (string.IsNullOrWhiteSpace(cmd))
                    continue;

                cmd = NormalizeToSlashCommand(cmd);
                if (cmd is null)
                    continue;

                if (sitId == 0 && string.Equals(cmd, "/sit", StringComparison.OrdinalIgnoreCase))
                    sitId = (ushort)emote.RowId;

                if (groundSitId == 0 && string.Equals(cmd, "/groundsit", StringComparison.OrdinalIgnoreCase))
                    groundSitId = (ushort)emote.RowId;

                if (sitId != 0 && groundSitId != 0)
                    return true;
            }

            return sitId != 0 && groundSitId != 0;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[LapCatCounter] Failed to resolve /sit and /groundsit emote IDs from Lumina.");
            return false;
        }
    }

    private static string? NormalizeToSlashCommand(string raw)
    {
        raw = raw.Trim();
        if (raw.Length == 0) return null;

        var parts = raw.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (p.StartsWith("/"))
            {
                return p.Trim().TrimEnd('.', ',', ';', ':');
            }
        }

        if (raw.StartsWith("/"))
            return raw.Trim().TrimEnd('.', ',', ';', ':');

        return null;
    }

    private static string? ExtractCommandString(object? cmdObj)
    {
        if (cmdObj is null) return null;

        if (cmdObj is string s0)
            return s0;

        var s1 =
            TryGetStringProp(cmdObj, "RawString") ??
            TryGetStringProp(cmdObj, "TextValue") ??
            TryGetStringProp(cmdObj, "Value") ??
            TryGetStringProp(cmdObj, "String");
        if (!string.IsNullOrWhiteSpace(s1))
            return s1;

        var payloadText = TryExtractFromPayloads(cmdObj);
        if (!string.IsNullOrWhiteSpace(payloadText))
            return payloadText;

        var s2 = cmdObj.ToString();
        return string.IsNullOrWhiteSpace(s2) ? null : s2;
    }

    private static string? TryGetStringProp(object obj, string propName)
    {
        try
        {
            var p = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return null;
            if (p.PropertyType != typeof(string)) return null;
            return p.GetValue(obj) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractFromPayloads(object obj)
    {
        try
        {
            var payloadsProp = obj.GetType().GetProperty("Payloads", BindingFlags.Public | BindingFlags.Instance);
            if (payloadsProp == null) return null;

            var payloadsObj = payloadsProp.GetValue(obj);
            if (payloadsObj is not IEnumerable payloads) return null;

            var sb = new StringBuilder();

            foreach (var payload in payloads)
            {
                if (payload == null) continue;

                var txt =
                    TryGetStringProp(payload, "Text") ??
                    TryGetStringProp(payload, "RawText") ??
                    TryGetStringProp(payload, "Value") ??
                    TryGetStringProp(payload, "String") ??
                    TryGetStringProp(payload, "TextValue");

                if (!string.IsNullOrWhiteSpace(txt))
                {
                    sb.Append(txt);
                    continue;
                }

                var inner =
                    TryGetObjProp(payload, "Text") ??
                    TryGetObjProp(payload, "Payload") ??
                    TryGetObjProp(payload, "Data");

                if (inner != null)
                {
                    var innerTxt =
                        TryGetStringProp(inner, "Text") ??
                        TryGetStringProp(inner, "RawString") ??
                        TryGetStringProp(inner, "TextValue") ??
                        inner.ToString();

                    if (!string.IsNullOrWhiteSpace(innerTxt))
                        sb.Append(innerTxt);
                }
            }

            var combined = sb.ToString();
            return string.IsNullOrWhiteSpace(combined) ? null : combined;
        }
        catch
        {
            return null;
        }
    }

    private static object? TryGetObjProp(object obj, string propName)
    {
        try
        {
            var p = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return null;
            return p.GetValue(obj);
        }
        catch
        {
            return null;
        }
    }
}
