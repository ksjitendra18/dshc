namespace CKYC.Files.Documents;

/// <summary>
/// Best-effort mapping of the stored 2-letter Indian state/UT code to the full name shown on
/// the Aadhaar EKYC report. Unknown values (or values already stored as full names) pass through
/// unchanged; the map is deliberately permissive about historical code variants.
/// </summary>
internal static class IndianStates
{
    private static readonly Dictionary<string, string> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AN"] = "Andaman and Nicobar Islands",
        ["AP"] = "Andhra Pradesh",
        ["AR"] = "Arunachal Pradesh",
        ["AS"] = "Assam",
        ["BR"] = "Bihar",
        ["CH"] = "Chandigarh",
        ["CG"] = "Chhattisgarh",
        ["CT"] = "Chhattisgarh",
        ["DN"] = "Dadra and Nagar Haveli and Daman and Diu",
        ["DD"] = "Dadra and Nagar Haveli and Daman and Diu",
        ["DL"] = "Delhi",
        ["GA"] = "Goa",
        ["GJ"] = "Gujarat",
        ["HR"] = "Haryana",
        ["HP"] = "Himachal Pradesh",
        ["JK"] = "Jammu and Kashmir",
        ["JH"] = "Jharkhand",
        ["KA"] = "Karnataka",
        ["KL"] = "Kerala",
        ["LA"] = "Ladakh",
        ["LD"] = "Lakshadweep",
        ["MP"] = "Madhya Pradesh",
        ["MH"] = "Maharashtra",
        ["MN"] = "Manipur",
        ["ML"] = "Meghalaya",
        ["MZ"] = "Mizoram",
        ["NL"] = "Nagaland",
        ["OD"] = "Odisha",
        ["OR"] = "Odisha",
        ["PY"] = "Puducherry",
        ["PB"] = "Punjab",
        ["RJ"] = "Rajasthan",
        ["SK"] = "Sikkim",
        ["TN"] = "Tamil Nadu",
        ["TS"] = "Telangana",
        ["TG"] = "Telangana",
        ["TR"] = "Tripura",
        ["UP"] = "Uttar Pradesh",
        ["UK"] = "Uttarakhand",
        ["UT"] = "Uttarakhand",
        ["UA"] = "Uttarakhand",
        ["WB"] = "West Bengal",
    };

    public static string Resolve(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return string.Empty;
        return ByCode.TryGetValue(state.Trim(), out var name) ? name : state.Trim();
    }
}
