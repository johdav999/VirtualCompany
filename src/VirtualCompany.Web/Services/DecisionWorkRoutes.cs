namespace VirtualCompany.Web.Services;
public static class DecisionWorkRoutes
{
    public static string Create(Guid company, string kind, Guid version, string item) =>
        $"/work/create-from-decision?companyId={company:D}&kind={Uri.EscapeDataString(kind)}&versionId={version:D}&itemKey={Uri.EscapeDataString(item)}";
    public static string Source(Guid company, Guid task) => $"/work/source?companyId={company:D}&taskId={task:D}";
}
