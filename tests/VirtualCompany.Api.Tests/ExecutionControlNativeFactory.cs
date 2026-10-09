using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualCompany.Application.Agents;
using VirtualCompany.Infrastructure.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
namespace VirtualCompany.Api.Tests;

// Research is a company-owned database operation. This fixture uses its real typed adapter;
// it does not supply a model provider or invoke any customer delivery.
public class ExecutionControlNativeFactory : TestWebApplicationFactory
{
    public Exception? LastError { get; private set; }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging=>logging.AddProvider(new Errors(this)));
        builder.ConfigureTestServices(services=>{
            services.RemoveAll<IInternalCompanyToolContract>();
            services.AddScoped<IInternalCompanyToolContract,TaskDraftToolAdapter>();
            services.AddDbContext<VirtualCompanyDbContext>((_,options)=>options.ReplaceService<IModelCustomizer,ResearchSqliteModelCustomizer>());
        });
    }
    private sealed class Errors(ExecutionControlNativeFactory owner) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)=>new ErrorLogger(owner);public void Dispose(){}
        private sealed class ErrorLogger(ExecutionControlNativeFactory owner):ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;
            public bool IsEnabled(LogLevel level)=>level>=LogLevel.Error;
            public void Log<TState>(LogLevel level,EventId id,TState state,Exception? exception,Func<TState,Exception?,string> formatter){if(exception is not null&&level>=LogLevel.Error)owner.LastError=exception;}
        }
    }
}
// SQLite cannot generate SQL Server rowversion values. Only these isolated fixture types
// use supplied tokens; admission/concurrency is separately exercised on SQL Server.
public sealed class ResearchSqliteModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder,DbContext context)
    {
        base.Customize(modelBuilder,context);
        if(!context.Database.IsSqlite())return;
        modelBuilder.Entity<IdealCustomerProfile>().Property(x=>x.RowVersion).HasColumnType("BLOB").ValueGeneratedNever().IsConcurrencyToken();
        modelBuilder.Entity<ProspectingRun>().Property(x=>x.RowVersion).HasColumnType("BLOB").ValueGeneratedNever().IsConcurrencyToken();
        modelBuilder.Entity<ProspectAccount>().Property(x=>x.RowVersion).HasColumnType("BLOB").ValueGeneratedNever().IsConcurrencyToken();
    }
}
