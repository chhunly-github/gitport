namespace GitPort.Services;

internal static class PushOutput
{
    // Git transfers objects (which may include several versions of a file).
    public static bool IsProgress(string line)
    {
        if (line.StartsWith("remote:", StringComparison.OrdinalIgnoreCase))
            line = line[7..].TrimStart();
        return new[] { "Enumerating objects:", "Counting objects:", "Compressing objects:",
            "Writing objects:", "Receiving objects:", "Resolving deltas:" }
            .Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal)) ||
            System.Text.RegularExpressions.Regex.IsMatch(line, @"^Total \d+ \(delta \d+\)");
    }

    public static string Diagnostics(string output) => string.Join(Environment.NewLine,
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()).Where(line => !IsProgress(line)).Distinct());

    public static string Guidance(string error)
    {
        if (new[] { "quota", "upgrade", "billing", "storage limit", "plan limit", "payment" }
            .Any(word => error.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return "Account limit: review the server message below. Free storage or ask the repository owner to update billing or upgrade the hosting plan, then retry.";
        if (GitService.IsAuthFailure(error))
            return "Account access: update your username or token and check that the account has write permission to this repository.";
        if (GitService.IsSslFailure(error))
            return "Connection certificate: check the server certificate and connection settings, then retry.";
        if (GitService.IsPushRejectedFailure(error))
            return "Remote changes: fetch and pull or merge the destination branch, resolve any conflicts, then push again. Check protected branch rules if the server denies the push.";
        return "Review the server message below. Resolve the reported Git or repository issue, then retry the push.";
    }
}
