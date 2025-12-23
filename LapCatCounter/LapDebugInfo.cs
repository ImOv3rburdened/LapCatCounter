using System;

namespace LapCatCounter;

public struct LapDebugInfo
{
    public string CandidateName;
    public ulong CandidateObjectId;
    public float Distance3D;
    public float HorizontalXZ;
    public float Dx;
    public float Dy;
    public float Dz;
    public bool PassRadius;
    public bool PassXY;
    public bool PassZ;
    public float StableSeconds;
    public bool CountedThisGate;
    public string Reason;
}