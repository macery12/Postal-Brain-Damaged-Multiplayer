using PBDMP.Plugin;

var checks = 0;
void Check(bool passed, string label)
{
    if (!passed) throw new InvalidOperationException(label);
    checks++;
    Console.WriteLine("PASS: " + label);
}
var context = new ReportSessionContext();
Check(context.Describe().Contains("Level: Unavailable") && context.Describe().Contains("Role: Unavailable"), "No session means unavailable, not a fabricated offline session");
context.Begin(true, true);
context.RememberLevel("LEVEL_AD_Neighborhood");
Check(context.Describe().Contains("Host (P1)") && context.Describe().Contains("Connection: Steam") && context.Describe().Contains("LEVEL_AD_Neighborhood"), "Host/map/Steam context survives return-to-menu without a clearing operation");
context.RememberLevel("LEVEL_Asylum_Prison");
Check(context.Describe().Contains("LEVEL_Asylum_Prison") && !context.Describe().Contains("LEVEL_AD_Neighborhood"), "Map transition replaces the previously played map");
context.Begin(false, false);
Check(context.Describe().Contains("Joining player (P2)") && context.Describe().Contains("Connection: Direct IP") && context.Describe().Contains("Level: Unavailable"), "A new session cannot inherit the previous session's level");
foreach (var invalid in new[] { "C:\\Users\\Private\\LEVEL_Custom", "LEVEL_Bad\nIP: 192.0.2.1", "LEVEL_../personal", "LEVEL_192.0.2.1" }) context.RememberLevel(invalid);
Check(context.Describe().Contains("Level: Unavailable"), "Session data rejects paths, addresses and multiline injection");
context.RememberLevel("LEVEL_Asylum_Prison");
var software = "PBDMP version: 0.10.0\nRuntime: build+meta & value? # 100%";
var url = ReportDraft.Url(software, context.Describe());
var fields = new Uri(url).Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
Check(fields.Count == 3 && fields["template"] == "bug-report.yml" && !fields.ContainsKey("body"), "Link selects the form and named fields, not a blank Markdown issue");
Check(fields["software"] == software && fields["session"] == context.Describe(), "Form fields round-trip reserved characters and multiline content");
Check(url.Length < 2000 && fields.Keys.Order().SequenceEqual(new[] { "session", "software", "template" }), "Small link sends only the intended editable fields");
Console.WriteLine($"{checks} reporting checks passed.");
