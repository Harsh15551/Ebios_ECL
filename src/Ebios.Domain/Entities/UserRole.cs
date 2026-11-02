namespace Ebios.Domain.Entities;

public enum UserRole
{
    SuperAdmin = 1,              // ECL HQ Admin - full access across all 13 Areas
    AreaPersonnelManager = 2,    // APM - access to all Sub-Areas under their assigned Area
    UnitPersonnelManager = 3     // UPM - access strictly restricted to their single Sub-Area
}
