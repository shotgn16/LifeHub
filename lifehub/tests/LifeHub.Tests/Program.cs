// Quick unit checks for LifeHub (run: dotnet run --project lifehub/tests/LifeHub.Tests). Exits 1 if any fail.
using LifeHub.Data; using LifeHub.Infrastructure; using LifeHub.Domain; using LifeHub.Services;
int pass = 0, fail = 0;
void Eq(string name, object? got, object? want) { if (Equals(got?.ToString(), want?.ToString())) pass++; else { fail++; Console.WriteLine($"FAIL {name}: got '{got}' want '{want}'"); } }
var i1 = MealService.ParseIngredient("200g basmati rice"); Eq("ing g", $"{i1.Quantity}|{i1.Unit}|{i1.Name}", "200|g|basmati rice");
var i2 = MealService.ParseIngredient("2 onions, finely chopped"); Eq("ing count", $"{i2.Quantity}|{i2.Unit}|{i2.Name}|{i2.Notes}", "2||onions|finely chopped");
var i3 = MealService.ParseIngredient("1½ tbsp olive oil"); Eq("ing frac", $"{i3.Quantity}|{i3.Unit}|{i3.Name}", "1.5|tbsp|olive oil");
var i4 = MealService.ParseIngredient("salt (optional)"); Eq("ing optional", $"{i4.Quantity}|{i4.Name}|{i4.Optional}", "|salt|True");
var i5 = MealService.ParseIngredient("1 tin chopped tomatoes"); Eq("ing tin", $"{i5.Quantity}|{i5.Unit}|{i5.Name}", "1|tin|chopped tomatoes");
Eq("eggs kind", Freshness.ProfileFor("Free range eggs")?.Kind, DateKind.BestBefore);
Eq("egg noodles", Freshness.ProfileFor("egg noodles")?.Name, "Dry goods");
Eq("milk", Freshness.ProfileFor("Semi-skimmed milk")?.Kind, DateKind.UseBy);
Eq("cereal", Freshness.ProfileFor("Kellogg's Corn Flakes")?.Name, "Cereal");
Eq("chocolate milk is snack? (chocolate first)", Freshness.ProfileFor("milk chocolate")?.Name, "Snacks");
var today = new DateOnly(2026, 10, 21);
PantryItem Egg(int d) => new() { Name = "Eggs", UseBy = today.AddDays(d), DateKind = DateKind.BestBefore, AddedOn = today.AddDays(-20), Location = StorageLocation.Fridge };
Eq("egg fresh", Freshness.Of(Egg(10), null, today, 3).State, FreshState.Fresh);
Eq("egg soon", Freshness.Of(Egg(2), null, today, 3).State, FreshState.UseSoon);
Eq("egg past best", Freshness.Of(Egg(-1), null, today, 3).State, FreshState.PastBest);
Eq("egg check", Freshness.Of(Egg(-5), null, today, 3).State, FreshState.CheckFirst);
var milk = new PantryItem { Name = "Milk", UseBy = today.AddDays(-1), DateKind = DateKind.UseBy, AddedOn = today.AddDays(-7) };
Eq("milk expired", Freshness.Of(milk, null, today, 3).State, FreshState.Expired);
var staple = new StapleItem { Name = "Eggs", GraceDays = 10 };
Eq("staple grace override", Freshness.Of(Egg(-5), staple, today, 3).State, FreshState.PastBest);
// no screen sensor → capped at 30
// quick glance dropped
// brief switch merged
// unavailable ignored
// screen on/off twice while app open
// ongoing
Eq("salvage cut item", LifeHub.Integrations.Ollama.AiClient.Salvage("{\"insights\": [{\"a\": 1}, {\"a\": 2}, {\"a\": \"tru"), "{\"insights\": [{\"a\": 1}, {\"a\": 2}]}");
Eq("salvage nested", LifeHub.Integrations.Ollama.AiClient.Salvage("{\"x\": {\"items\": [\"a, b\", \"c\", \"d"), "{\"x\": {\"items\": [\"a, b\", \"c\"]}}");
Eq("parse salvaged", LifeHub.Integrations.Ollama.AiClient.ParseLenient("```json\n{\"insights\": [{\"t\": \"ok\"}, {\"t\": \"cut").GetProperty("insights").GetArrayLength(), 1);

// ---- 2.0: visibility between people
{
    var alex = Guid.NewGuid(); var sam = Guid.NewGuid();
    LifeHub.Data.UserScope.Household = new(alex, true, false, true, new[] { alex, sam });
    var task = new TaskItem { OwnerId = alex }; var legacy = new TaskItem(); var pantry = new PantryItem(); var samsMeal = new MealPlanEntry { OwnerId = sam };
    bool See(Guid? who, LifeHub.Data.Entity e, LifeHub.Data.OwnScope sc) { using (who is { } w ? LifeHub.Data.UserScope.As(w) : LifeHub.Data.UserScope.None()) return LifeHub.Data.UserScope.CanSee(e, sc); }
    Eq("own task", See(alex, task, LifeHub.Data.OwnScope.Personal), true);
    Eq("other's task hidden", See(sam, task, LifeHub.Data.OwnScope.Personal), false);
    Eq("pre-2.0 task = main person", See(alex, legacy, LifeHub.Data.OwnScope.Personal), true);
    Eq("pre-2.0 task hidden from others", See(sam, legacy, LifeHub.Data.OwnScope.Personal), false);
    Eq("shared kitchen visible", See(sam, pantry, LifeHub.Data.OwnScope.Kitchen), true);
    Eq("unshared meals: Sam's own", See(sam, samsMeal, LifeHub.Data.OwnScope.Meals), true);
    Eq("unshared meals: hidden from Alex", See(alex, samsMeal, LifeHub.Data.OwnScope.Meals), false);
    Eq("no scope hides personal", See(null, task, LifeHub.Data.OwnScope.Personal), false);
    using (LifeHub.Data.UserScope.AsSystem()) Eq("system sees all", LifeHub.Data.UserScope.CanSee(task, LifeHub.Data.OwnScope.Personal), true);
    var shared = new Note { OwnerId = alex, Shared = true };
    Eq("shared note visible", See(sam, shared, LifeHub.Data.OwnScope.Personal), true);
    var n1 = new TaskItem(); using (LifeHub.Data.UserScope.As(sam)) LifeHub.Data.UserScope.Stamp(n1, LifeHub.Data.OwnScope.Personal); Eq("stamped owner", n1.OwnerId, sam);
    var p1 = new PantryItem(); using (LifeHub.Data.UserScope.As(sam)) LifeHub.Data.UserScope.Stamp(p1, LifeHub.Data.OwnScope.Kitchen); Eq("shared area stays household", p1.OwnerId, Guid.Empty);
    var m1 = new MealPlanEntry(); using (LifeHub.Data.UserScope.As(sam)) LifeHub.Data.UserScope.Stamp(m1, LifeHub.Data.OwnScope.Meals); Eq("unshared area stamped", m1.OwnerId, sam);
    var cp = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim("name", "x") }, "Cookies"));
    Eq("old cookie = main person", LifeHub.Data.UserScope.FromPrincipal(cp), alex);
    var gone = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim(LifeHub.Data.UserScope.ClaimUserId, Guid.NewGuid().ToString()) }, "Cookies"));
    Eq("removed person signed out", LifeHub.Data.UserScope.FromPrincipal(gone) is null, true);
}
// ---- 2.0: deals matching and notes
Eq("deal match", DealsService.Matches("Chicken breast fillets 650g", "chicken breast"), true);
Eq("deal plural", DealsService.Matches("Free range eggs x12", "egg"), true);
Eq("deal no match", DealsService.Matches("Chicken stock cubes", "chicken breast"), false);
Eq("deal staple milk", DealsService.Matches("Semi-skimmed milk 4 pints", "Milk"), true);
Eq("price pence", DealsService.PriceText(new DealOffer { Price = 0.49m }), "49p");
Eq("price was", DealsService.PriceText(new DealOffer { Price = 3.29m, WasPrice = 4.49m, Offer = "Super 6" }), "£3.29 · Super 6 · was £4.49");
Eq("note guess link", NotesService.Guess("https://example.com/recipe", Array.Empty<string>()), "Links");
Eq("note guess existing", NotesService.Guess("another gift for Sam", new[] { "Gift Ideas" }), "Gift Ideas");
Eq("note guess unsorted", NotesService.Guess("door code 1234", Array.Empty<string>()), "Unsorted");
// ---- 3.0: desktop app
{
    var dir = Path.Combine(Path.GetTempPath(), "lh-ut-" + Guid.NewGuid().ToString("N"));
    var c = LifeHub.Desktop.HostConfig.Load(dir);
    Eq("default mode", c.Mode, LifeHub.Desktop.HostMode.Local);
    Eq("default port", c.Port, 8099);
    Eq("local not on network", c.OnNetwork, false);
    c.Mode = LifeHub.Desktop.HostMode.Server; c.Port = 8123; c.ModeChosen = true; c.Save(dir);
    var c2 = LifeHub.Desktop.HostConfig.Load(dir);
    Eq("config round trip", $"{c2.Mode}|{c2.Port}|{c2.ModeChosen}|{c2.OnNetwork}", "Server|8123|True|True");
    Eq("mode saved as text", File.ReadAllText(LifeHub.Desktop.HostConfig.FileIn(dir)).Contains("\"Server\""), true);
    LifeHub.Desktop.DesktopRuntime.DataDir = dir;
    LifeHub.Desktop.DesktopRuntime.Active = false;
    var tok = LifeHub.Desktop.DesktopRuntime.EnsureToken();
    Eq("token length", tok.Length, 64);
    Eq("token stable", LifeHub.Desktop.DesktopRuntime.EnsureToken(), tok);
    Eq("token refused when not desktop", LifeHub.Desktop.DesktopRuntime.CheckToken(tok), false);
    LifeHub.Desktop.DesktopRuntime.Active = true;
    Eq("token accepted", LifeHub.Desktop.DesktopRuntime.CheckToken(tok), true);
    Eq("wrong token", LifeHub.Desktop.DesktopRuntime.CheckToken(new string('0', 64)), false);
    Eq("empty token", LifeHub.Desktop.DesktopRuntime.CheckToken(""), false);
    var who = Guid.NewGuid(); var once = LifeHub.Desktop.DesktopRuntime.IssueOnce(who);
    Eq("once code works", LifeHub.Desktop.DesktopRuntime.TakeOnce(once), who);
    Eq("once code only once", LifeHub.Desktop.DesktopRuntime.TakeOnce(once) is null, true);
    Eq("bad once code", LifeHub.Desktop.DesktopRuntime.TakeOnce("nope") is null, true);
    LifeHub.Desktop.DesktopRuntime.Active = false;
    Eq("data dir linux", LifeHub.Desktop.DesktopPaths.DefaultData().EndsWith(Path.Combine(".local", "share", "lifehub")) || Environment.GetEnvironmentVariable("XDG_DATA_HOME") is not null, true);
    Eq("model fits memory", LifeHub.Desktop.OllamaSetup.Recommended().Model.StartsWith("qwen2.5:"), true);
    Eq("free port", LifeHub.Desktop.DesktopRuntime.FreePort() > 1024, true);
    Directory.Delete(dir, true);
}
{
    // 3.1: encryption at rest
    var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
    var c = new DataCipher(key);
    var enc = c.Encrypt(System.Text.Encoding.UTF8.GetBytes("hello"));
    Eq("cipher header", DataCipher.IsEncrypted(enc), true);
    Eq("cipher roundtrip", System.Text.Encoding.UTF8.GetString(c.Decrypt(enc)), "hello");
    Eq("cipher nonce random", Convert.ToBase64String(c.Encrypt(System.Text.Encoding.UTF8.GetBytes("hello"))) != Convert.ToBase64String(enc), true);
    var bad = (byte[])enc.Clone(); bad[^1] ^= 1;
    var threw = false; try { c.Decrypt(bad); } catch { threw = true; }
    Eq("cipher tamper detected", threw, true);
    threw = false; try { new DataCipher(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).Decrypt(enc); } catch { threw = true; }
    Eq("cipher wrong key", threw, true);
    Eq("plain passes through", System.Text.Encoding.UTF8.GetString(c.Decrypt(System.Text.Encoding.UTF8.GetBytes("{}"))), "{}");
    Eq("recovery key parse", Convert.ToBase64String(DataCipher.ParseRecoveryKey(c.RecoveryKey)!), Convert.ToBase64String(key));
    Eq("recovery key junk", DataCipher.ParseRecoveryKey("nope") is null, true);
    var d = Path.Combine(Path.GetTempPath(), "lhut" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d);
    var f = Path.Combine(d, "a.json"); File.WriteAllText(f, "[1]");
    Eq("encrypt existing count", c.EncryptExisting(new[] { f, Path.Combine(d, "missing.json") }), 1);
    Eq("encrypt existing file", DataCipher.IsEncryptedFile(f), true);
    Eq("encrypt existing again", c.EncryptExisting(new[] { f }), 0);
    Eq("read text", c.ReadText(f), "[1]");
    var store = new MemStore();
    var opened = DataCipher.Open(store, () => false);
    Eq("open makes key", store.Key?.Length, 32);
    Eq("open reuses key", DataCipher.Open(store, () => true).RecoveryKey, opened.RecoveryKey);
    threw = false; try { DataCipher.Open(new MemStore(), () => true); } catch (InvalidOperationException) { threw = true; }
    Eq("open refuses without key", threw, true);
    var rs = new MemStore { Recovery = key };
    Eq("open adopts recovery", DataCipher.Open(rs, () => true).RecoveryKey, c.RecoveryKey);
    Directory.Delete(d, true);
    // 3.1: notification routes
    var ns = new NotificationSettings();

    ns.Routes[NoticeCategories.Deals] = NoticeRoute.Off;
    Eq("route override", ns.RouteOf(NoticeCategories.Deals), NoticeRoute.Off);
    Eq("route categories", NoticeCategories.All.Length >= 8, true);
}
{
    // 3.2: usual week → detailed periods
    var week = new UsualWeek();
    var blocks = week.ToBlocks();
    string Key(IEnumerable<PeriodBlock> x) => string.Join(";", x.SelectMany(b => b.Days.Select(d => $"{d}|{b.Start}|{b.End}|{b.Kind}")).OrderBy(k => k));
    Eq("usual week default = default blocks", Key(blocks), Key(ScheduleSettings.DefaultBlocks()));
    UsualWeek.FromBlocks(ScheduleSettings.DefaultBlocks(), out var exact);
    Eq("default blocks read back exactly", exact, true);
    week.Regular.Add(new RegularTime { Label = "Gym", Days = new() { DayOfWeek.Monday }, Start = new(18, 0), End = new(19, 30) });
    week.Days.First(d => d.Day == DayOfWeek.Saturday).Working = true;
    var mon = week.ToBlocks().Where(b => b.Days.Contains(DayOfWeek.Monday)).OrderBy(b => b.Start).Select(b => $"{b.Start:HH:mm}-{b.End:HH:mm} {b.Kind}").ToList();
    Eq("monday with gym", string.Join(", ", mon), "07:00-09:00 Free, 09:00-17:30 Work, 17:30-18:00 Free, 18:00-19:30 Reserved, 19:30-23:00 Free");
    Eq("saturday working", week.ToBlocks().Any(b => b.Days.Contains(DayOfWeek.Saturday) && b.Kind == PeriodKind.Work), true);
    var back = UsualWeek.FromBlocks(week.ToBlocks(), out var exact2);
    Eq("gym survives round trip", back.Regular.Count == 1 && exact2, true);
    var plan = new SchedulePlan { Kind = PlanKind.DayOff, From = new(2026, 3, 3), To = new(2026, 3, 7) };
    Eq("plan covers", plan.Covers(new(2026, 3, 5)) && !plan.Covers(new(2026, 3, 8)), true);
    Eq("plan name", plan.Name, "Day off");
}
Eq("html text", HtmlText.Extract("<html><body><p>Hello <b>world</b></p><script>x()</script></body></html>").Trim(), "Hello world");
Console.WriteLine($"{pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;

sealed class MemStore : LifeHub.Data.IDataKeyStore
{
    public byte[]? Key; public byte[]? Recovery;
    public byte[]? Load() => Key;
    public void Save(byte[] key) => Key = key;
    public byte[]? TakeRecoveryKey() { var r = Recovery; Recovery = null; return r; }
}
