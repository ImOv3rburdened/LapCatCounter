using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace LapCatCounter;

public static unsafe class ActorStateReader
{
    public static bool TryGetMode(IPlayerCharacter player, out CharacterModes mode, out byte modeParam)
    {
        mode = CharacterModes.None;
        modeParam = 0;

        if (player.Address == nint.Zero)
            return false;

        try
        {
            var nativeCharacter = (Character*)player.Address;
            mode = nativeCharacter->Mode;
            modeParam = nativeCharacter->ModeParam;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsLapCompatibleState(IPlayerCharacter player)
        => TryGetMode(player, out var mode, out _) && IsLapCompatibleState(mode);

    public static bool IsLapCompatibleState(CharacterModes mode)
        => mode is CharacterModes.InPositionLoop;

    public static string Describe(IPlayerCharacter player)
        => TryGetMode(player, out var mode, out var modeParam)
            ? $"{mode} ({modeParam})"
            : "Unknown";
}


