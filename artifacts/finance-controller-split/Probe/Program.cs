using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using VirtualCompany.Api.Controllers;

var root = Path.GetFullPath(args[0]);
var output = Path.Combine(root, "artifacts", "finance-controller-split");
var options = new JsonSerializerOptions { WriteIndented = true };

var routes = typeof(InternalFinanceCashPostingBackfillController).Assembly.GetTypes()
    .Where(t => t.Name.StartsWith("InternalFinance") && typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
    .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>().Select(http => new
        {
            Route = type.GetCustomAttribute<RouteAttribute>(true)!.Template + "/" + http.Template,
            Verbs = string.Join(",", http.HttpMethods.Order()),
            Action = method.Name,
            ReturnType = method.ReturnType.ToString(),
            Parameters = string.Join("|", method.GetParameters().Select(p => $"{p.Name}:{p.ParameterType}:{p.HasDefaultValue}:{p.DefaultValue}:" + string.Join(",", p.GetCustomAttributesData().Where(a => a.AttributeType.Namespace != "System.Runtime.CompilerServices").Select(a => a.ToString()).Order()))),
            ControllerAttributes = string.Join("|", type.GetCustomAttributesData().Where(a => a.AttributeType != typeof(RouteAttribute) && a.AttributeType.Namespace != "System.Runtime.CompilerServices").Select(a => a.ToString()).Order()),
            ActionAttributes = string.Join("|", method.GetCustomAttributesData().Where(a => !typeof(HttpMethodAttribute).IsAssignableFrom(a.AttributeType) && a.AttributeType.Namespace != "System.Runtime.CompilerServices").Select(a => a.ToString()).Order())
        })))
    .OrderBy(x => x.Route).ThenBy(x => x.Verbs).ToArray();
File.WriteAllText(Path.Combine(output, args.Length > 1 ? args[1] : "routes-current.json"), JsonSerializer.Serialize(routes, options));
Console.WriteLine($"Captured {routes.Length} internal Finance routes.");
