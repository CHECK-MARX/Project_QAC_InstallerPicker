using System.Collections.Generic;

namespace QACInstallerPicker.App.Models;

public sealed class LocalLlmDecisionResult
{
    public string ModelName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string DefaultOs { get; set; } = string.Empty;
    public List<LocalLlmVersionedRequest> VersionedRequests { get; set; } = new();
    public List<string> MatchedCodes { get; set; } = new();
    public string RawResponse { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public bool IsCached { get; set; }
    public bool IsSuccess => string.IsNullOrWhiteSpace(ErrorMessage);
}

public sealed class LocalLlmVersionedRequest
{
    public string Code { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
}
