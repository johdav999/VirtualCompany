using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Documents;
namespace VirtualCompany.Shared.Contracts.CompanyDocuments;
public sealed record ImportDefaultSupportKnowledgeResponse(

        IReadOnlyList<CompanyKnowledgeDocumentDto> Imported,

        IReadOnlyList<string> Skipped);
