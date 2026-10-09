using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Orchestration;
namespace VirtualCompany.Application.Finance;
public interface IStrategicScenarioService
{
    Task<StrategicScenarioOptions> OptionsAsync(Guid company,int fiscalYear,CancellationToken ct);
    Task<StrategicScenarioHistoryPage> HistoryAsync(Guid company,int skip,CancellationToken ct);
    Task<StrategicScenarioPreview> PreviewAsync(Guid company,StrategicScenarioInput input,CancellationToken ct);
    Task<StrategicScenarioDocument> SaveAsync(Guid company,SaveStrategicScenario command,CancellationToken ct);
    Task<StrategicScenarioDocument> OpenAsync(Guid company,Guid id,CancellationToken ct);
    Task<StrategicScenarioDocument> DuplicateAsync(Guid company,Guid id,DuplicateStrategicScenario command,CancellationToken ct);
    Task<StrategicScenarioComparison> CompareAsync(Guid company,Guid baseline,Guid alternative,CancellationToken ct);
}
