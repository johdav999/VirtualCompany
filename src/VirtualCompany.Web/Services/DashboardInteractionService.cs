using Microsoft.Extensions.Logging;

namespace VirtualCompany.Web.Services;

public interface IDashboardInteractionService
{
    DashboardTelemetrySession? CurrentSession { get; }
    DashboardTelemetrySession StartSession(string pageName, Guid? companyId);
    void RecordAction(DashboardActionTelemetryContext context);
    void RecordFocusItemClick(DashboardActionTelemetryContext context);
    void RecordScrollDepth(double depthPercentage);
    void EndSession();
}

public sealed class DashboardInteractionService : IDashboardInteractionService
{
    private readonly ILogger<DashboardInteractionService> _logger;

    public DashboardInteractionService(ILogger<DashboardInteractionService> logger)
    {
        _logger = logger;
    }

    public DashboardTelemetrySession? CurrentSession { get; private set; }

    public DashboardTelemetrySession StartSession(string pageName, Guid? companyId)
    {
        if (CurrentSession is not null && CurrentSession.Matches(pageName, companyId))
        {
            return CurrentSession;
        }

        CurrentSession = new DashboardTelemetrySession(pageName, companyId);
        CurrentSession.RecordSessionStarted();
        return CurrentSession;
    }

    public void RecordAction(DashboardActionTelemetryContext context) =>
        Execute(session => session.RecordAction(context), "dashboard action");

    public void RecordFocusItemClick(DashboardActionTelemetryContext context) =>
        Execute(session => session.RecordFocusItemClick(context), "dashboard focus item");

    public void RecordScrollDepth(double depthPercentage) =>
        Execute(session => session.RecordScrollDepth(depthPercentage), "dashboard scroll depth");

    public void EndSession()
    {
        CurrentSession = null;
    }

    private void Execute(Action<DashboardTelemetrySession> callback, string operationName)
    {
        if (CurrentSession is null)
        {
            return;
        }

        try
        {
            callback(CurrentSession);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ignoring telemetry failure while recording {OperationName}.", operationName);
        }
    }
}