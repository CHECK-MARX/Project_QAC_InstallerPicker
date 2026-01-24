using System.Collections.Generic;

namespace QACInstallerPicker.App.Models;

public class MemoParseResult
{
    public List<string> MatchedCodes { get; } = new();
    public List<AmbiguousMatch> AmbiguousMatches { get; } = new();
    public List<string> UnresolvedTerms { get; } = new();
}

public class AmbiguousMatch
{
    public string Term { get; set; } = string.Empty;
    public List<string> Candidates { get; set; } = new();
}
