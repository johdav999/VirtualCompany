using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public abstract class FinanceSummaryPageBase<TViewModel> : FinancePageBase, IDisposable
    where TViewModel : class
{
    [Inject] protected FinanceApiClient FinanceApiClient { get; set; } = default!;

    private CancellationTokenSource? _summaryLoadCts;
    private int _summaryLoadVersion;

    protected TViewModel? ViewModel { get; private set; }
    protected bool IsSummaryLoading { get; private set; }
    protected bool IsSummaryEmpty { get; private set; }
    protected string? SummaryErrorMessage { get; private set; }

    protected abstract Task<TViewModel?> LoadSummaryViewModelAsync(Guid companyId, CancellationToken cancellationToken);

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        CancelSummaryLoad();
        if (IsLoading || !AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            ResetSummary();
            return;
        }

        await LoadSummaryAsync(companyId);
    }

    protected Task RetryAsync() =>
        AccessState.CompanyId is Guid companyId
            ? LoadSummaryAsync(companyId)
            : Task.CompletedTask;

    private async Task LoadSummaryAsync(Guid companyId)
    {
        IsSummaryLoading = true;
        ResetSummaryState();
        await InvokeAsync(StateHasChanged);

        var loadVersion = Interlocked.Increment(ref _summaryLoadVersion);
        var cancellationTokenSource = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _summaryLoadCts, cancellationTokenSource);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();

        try
        {
            var viewModel = await LoadSummaryViewModelAsync(companyId, cancellationTokenSource.Token);
            if (loadVersion != _summaryLoadVersion || cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            IsSummaryEmpty = viewModel is null;
            ViewModel = viewModel;
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (FinanceApiException ex)
        {
            if (loadVersion != _summaryLoadVersion || cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            SummaryErrorMessage = ex.Message;
        }
        finally
        {
            if (loadVersion == _summaryLoadVersion)
            {
                IsSummaryLoading = false;
                await InvokeAsync(StateHasChanged);
            }

            if (ReferenceEquals(_summaryLoadCts, cancellationTokenSource))
            {
                _summaryLoadCts = null;
            }

            cancellationTokenSource.Dispose();
        }
    }

    private void ResetSummary()
    {
        ResetSummaryState();
        IsSummaryLoading = false;
    }

    private void ResetSummaryState() =>
        (ViewModel, IsSummaryEmpty, SummaryErrorMessage) = (null, false, null);

    private void CancelSummaryLoad()
    {
        var cancellationTokenSource = Interlocked.Exchange(ref _summaryLoadCts, null);
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
    }

    public void Dispose() => CancelSummaryLoad();
}