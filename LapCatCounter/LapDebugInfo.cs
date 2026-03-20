using System;

namespace LapCatCounter;

public struct LapDebugInfo
{
    public string CandidateName;
    public ulong CandidateObjectId;
    public float Distance3D;
    public float HorizontalXZ;
    public float VerticalDelta;
    public bool PassRadius;
    public bool PassXY;
    public bool PassVertical;
    public bool LocalStateOk;
    public bool PartnerStateOk;
    public string LocalMode;
    public string PartnerMode;
    public float StableSeconds;
    public float MissingSeconds;
    public string CurrentRole;
    public string CurrentStatus;
    public string Reason;
}
