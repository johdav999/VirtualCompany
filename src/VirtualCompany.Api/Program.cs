using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure;
using VirtualCompany.Infrastructure.Authorization;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddVirtualCompanyInfrastructure(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(CompanyPolicies.CompanyMembership, policy =>
        policy.RequireAuthenticatedUser()
            .AddRequirements(new CompanyMembershipRequirement()));

    options.AddPolicy(CompanyPolicies.CompanyManager, policy =>
        policy.RequireAuthenticatedUser()
            .AddRequirements(new CompanyRoleRequirement(
                CompanyMembershipRole.Owner,
                CompanyMembershipRole.Admin,
                CompanyMembershipRole.Manager)));

    options.AddPolicy(CompanyPolicies.CompanyAdmin, policy =>
        policy.RequireAuthenticatedUser()
            .AddRequirements(new CompanyRoleRequirement(
                CompanyMembershipRole.Owner,
                CompanyMembershipRole.Admin)));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<CompanyContextResolutionMiddleware>();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
    if (dbContext.Database.IsRelational())
    {
        dbContext.Database.Migrate();
    }
}

app.MapControllers();

app.Run();

public partial class Program;
