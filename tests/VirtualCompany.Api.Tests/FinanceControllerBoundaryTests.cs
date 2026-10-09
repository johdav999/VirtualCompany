using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceControllerBoundaryTests
{
    [Fact]
    public void Split_preserves_every_route_verb_parameter_contract_and_authorization_attribute()
    {
        var expected = JsonSerializer.Deserialize<RouteContract[]>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "FinanceRoutes", "internal-finance-routes.json")))!;
        Assert.Equal(expected, CaptureContracts());
    }

    [Fact]
    public void Capability_controllers_activate_independently_and_register_each_route_once()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var controllers = InternalControllers().Where(t => t.IsSubclassOf(typeof(InternalFinanceControllerBase))).ToArray();
        Assert.Equal(35, controllers.Length);
        Assert.All(controllers, type =>
        {
            Assert.NotNull(ActivatorUtilities.CreateInstance(scope.ServiceProvider, type));
            Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == CompanyPolicies.FinanceView);
            Assert.NotNull(type.GetCustomAttribute<RequireCompanyContextAttribute>());
        });
        var actions = scope.ServiceProvider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>().Where(a => controllers.Contains(a.ControllerTypeInfo.AsType())).ToArray();
        var expected = CaptureContracts().Where(c => controllers.Any(t => t.GetMethod(c.Action) is not null)).ToArray();
        Assert.Equal(expected.Length, actions.Length);
        Assert.All(actions.GroupBy(a => (a.AttributeRouteInfo!.Template, a.MethodInfo.GetCustomAttribute<HttpMethodAttribute>()!.HttpMethods.Single())),
            group => Assert.Single(group));
    }

    [Fact]
    public void Shared_transport_base_does_not_inject_capability_services_or_persistence()
    {
        var constructor = typeof(InternalFinanceControllerBase).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        Assert.Equal(new[] { "FinanceInitializationProblemHandler", "ILogger" }, constructor.GetParameters().Select(p => p.ParameterType.Name));
        Assert.All(InternalControllers().Where(t => t.IsSubclassOf(typeof(InternalFinanceControllerBase))), type =>
            Assert.InRange(type.GetConstructors().Single().GetParameters().Length, 2, 11));
        Assert.Empty(typeof(InternalFinanceControllerBase).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
    }

    private static IEnumerable<Type> InternalControllers() => typeof(InternalFinanceCashPostingBackfillController).Assembly.GetTypes()
        .Where(t => t.Name.StartsWith("InternalFinance", StringComparison.Ordinal) && typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    private static RouteContract[] CaptureContracts() => InternalControllers()
        .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>().Select(http => new RouteContract(
                type.GetCustomAttribute<RouteAttribute>(true)!.Template + "/" + http.Template,
                string.Join(",", http.HttpMethods.Order()), method.Name, method.ReturnType.ToString(),
                string.Join("|", method.GetParameters().Select(p => $"{p.Name}:{p.ParameterType}:{p.HasDefaultValue}:{p.DefaultValue}:" +
                    string.Join(",", p.GetCustomAttributesData().Where(a => a.AttributeType.Namespace != "System.Runtime.CompilerServices").Select(a => a.ToString()).Order()))),
                string.Join("|", type.GetCustomAttributesData().Where(a => a.AttributeType != typeof(RouteAttribute) && a.AttributeType.Namespace != "System.Runtime.CompilerServices").Select(a => a.ToString()).Order()),
                string.Join("|", method.GetCustomAttributesData().Where(a => !typeof(HttpMethodAttribute).IsAssignableFrom(a.AttributeType) && a.AttributeType.Namespace != "System.Runtime.CompilerServices").Select(a => a.ToString()).Order())))))
        .OrderBy(c => c.Route).ThenBy(c => c.Verbs).ToArray();

    private sealed record RouteContract(string Route, string Verbs, string Action, string ReturnType, string Parameters, string ControllerAttributes, string ActionAttributes);
}
