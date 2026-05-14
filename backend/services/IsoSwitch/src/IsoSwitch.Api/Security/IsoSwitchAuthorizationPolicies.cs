namespace IsoSwitch.Api.Security;

public static class IsoSwitchAuthorizationPolicies
{
    public const string OperateSwitch = "CanOperateSwitch";
    public const string ViewSwitchMonitor = "CanViewSwitchMonitor";
    public const string ManageSwitchRoutes = "CanManageSwitchRoutes";
    public const string ViewAudit = "CanViewAudit";

    public const string SwitchOperatePermission = "switch:operate";
    public const string SwitchMonitorPermission = "switch:monitor";
    public const string RoutingManagePermission = "routing:manage";
    public const string AuditViewPermission = "audit:view";
}
