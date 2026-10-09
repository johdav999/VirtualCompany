using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

[Trait("Category", "SqlServer")]
public sealed class BusinessWorkAssociationSqlServerTests
{
    [ApiSqlServerFact]
    public async Task Upgrade_backfills_only_same_company_records_and_down_up_preserves_P11_P12_work()
    {
        using var factory=TestWebApplicationFactory.CreateSqlServer(TimeProvider.System);var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await BusinessWorkEvidenceIntegrationTests.SeedOwner(factory,company,owner);
        var fixture=await BusinessEvidenceFixture.SeedAsync(factory,company,owner);
        var prior=await CollaborationEvidenceFixture.SeedAsync(factory,company,owner);
        await factory.SeedAsync(async db=>
        {
            var migrations=(await db.Database.GetAppliedMigrationsAsync()).ToArray();
            var associationMigration=Array.FindIndex(migrations,x=>x.EndsWith("AddBusinessWorkAssociations",StringComparison.Ordinal));
            Assert.True(associationMigration>0);
            var oldCompany=Guid.NewGuid();var foreignId=Guid.NewGuid();var stageId=Guid.NewGuid();var invalidTask=Guid.NewGuid();
            var intakeId=Guid.NewGuid();var intakeTask=Guid.NewGuid();
            db.Add(new DetectedBill(intakeId,company,"Intake supplier",null,"INTAKE-13",DateTime.UtcNow,DateTime.UtcNow.AddDays(7),
                "SEK",400,80,null,null,null,null,null,.8m,"high","valid","required",true,true,true,"[]",null,null,
                validationStatusPersistedAtUtc:DateTime.UtcNow));
            db.Add(new WorkTask(intakeTask,company,"finance_review","Legacy intake association",null,WorkTaskPriority.Normal,null,null,"user",owner,
                new Dictionary<string,JsonNode?>{["billId"]=JsonValue.Create(intakeId)}));
            db.AddRange(new Company(oldCompany,"Foreign migration fixture"),new SalesPipelineStage(stageId,oldCompany,"Foreign stage",1),
                new Deal(foreignId,oldCompany,"Foreign private deal",stageId,1,"SEK"),
                new WorkTask(invalidTask,company,"sales_review","Invalid legacy association",null,WorkTaskPriority.Normal,null,null,"user",owner,
                    new Dictionary<string,JsonNode?>{["dealId"]=JsonValue.Create(foreignId)}));await db.SaveChangesAsync();
            var tasksBefore=await db.WorkTasks.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==company);
            var approvalsBefore=await db.ApprovalRequests.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==company);
            await db.GetService<IMigrator>().MigrateAsync(migrations[associationMigration-1]);
            Assert.Equal(tasksBefore,await db.WorkTasks.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==company));
            Assert.Equal(6,await db.CollaborationContributions.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==company));
            await db.GetService<IMigrator>().MigrateAsync();db.ChangeTracker.Clear();
            foreach(var record in fixture.Records)
            {
                var task=await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==fixture.Tasks[record.Key]);
                var link=record.Key switch{"deal"=>task.BusinessDealId,"case"=>task.BusinessCaseId,"invoice"=>task.BusinessInvoiceId,"bill"=>task.BusinessBillId,"campaign"=>task.BusinessCampaignId,_=>task.BusinessBriefId};
                Assert.Equal(record.Value,link);
            }
            Assert.Null(await db.WorkTasks.IgnoreQueryFilters().Where(x=>x.Id==invalidTask).Select(x=>x.BusinessDealId).SingleAsync());
            Assert.Equal(intakeId,await db.WorkTasks.IgnoreQueryFilters().Where(x=>x.Id==intakeTask).Select(x=>x.BusinessBillId).SingleAsync());
            Assert.Equal(tasksBefore,await db.WorkTasks.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==company));
            Assert.Equal(approvalsBefore,await db.ApprovalRequests.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==company));
            Assert.Equal(fixture.Records["deal"],await db.WorkTasks.IgnoreQueryFilters().Where(x=>x.Id==prior.RootId).Select(x=>x.BusinessDealId).SingleAsync());
            Assert.Equal(6,await db.CollaborationContributions.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==company));
        });
    }
}
