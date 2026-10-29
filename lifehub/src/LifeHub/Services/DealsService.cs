using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LifeHub.Integrations.Ollama;

namespace LifeHub.Services;

/// <summary>
/// Deals & offers. When switched on, LifeHub periodically looks at the offers of stores near the area you choose
/// (their offers pages, supermarket emails you already get, leaflets you photograph or text you paste), works out
/// which ones matter to you (your shopping list, staples, recipes and meal plan, minus anything that clashes with
/// your dietary needs) and asks the local AI for meals and ingredients that use them on days that suit — plus
/// when to fit in the shop.
/// </summary>
public sealed class DealsService
{
    private readonly IRepository<DealOffer> _offers;
    private readonly IRepository<DealIdea> _ideas;
    private readonly IRepository<Promotion> _promos;
    private readonly SettingsService _settings;
    private readonly OllamaClient _ollama;
    private readonly KitchenService _kitchen;
    private readonly MealService _meals;
    private readonly IHttpClientFactory _http;
    private readonly Clock _clock;
    private readonly NotificationService _notify;
    private readonly ILogger<DealsService> _log;
    private readonly SemaphoreSlim _running = new(1, 1);

    public DealsService(IRepository<DealOffer> offers, IRepository<DealIdea> ideas, IRepository<Promotion> promos, SettingsService settings,
        OllamaClient ollama, KitchenService kitchen, MealService meals, IHttpClientFactory http, Clock clock, NotificationService notify,
        ILogger<DealsService> log)
    {
        _offers = offers; _ideas = ideas; _promos = promos; _settings = settings; _ollama = ollama; _kitchen = kitchen; _meals = meals;
        _http = http; _clock = clock; _notify = notify; _log = log;
    }

    public IRepository<DealOffer> Offers => _offers;
    public IRepository<DealIdea> Ideas => _ideas;
    public DealSettings Config => _settings.Current.Deals;
    public bool Busy => _running.CurrentCount == 0;
    public string Progress { get; private set; } = "";

    /// <summary>Current offers (not expired or dismissed), most relevant first.</summary>
    public IReadOnlyList<DealOffer> Current()
    {
        var today = _clock.Today;
        return _offers.Where(o => !o.Dismissed && (o.ValidUntil is null || o.ValidUntil >= today))
            .OrderByDescending(o => o.Score).ThenBy(o => o.Store).ThenBy(o => o.Item).ToList();
    }

    public IReadOnlyList<DealIdea> OpenIdeas() => _ideas.Where(i => i.Status == "open")
        .OrderBy(i => i.Kind == "trip" ? 0 : i.Kind == "meal" ? 1 : 2).ThenBy(i => i.Day).ToList();

    // ------------------------------------------------------------------ background

    /// <summary>Called hourly by the automation heartbeat: checks again once the interval has passed.</summary>
    public async Task RunIfDueAsync(CancellationToken ct = default)
    {
        var c = _settings.Household.Deals;
        if (!c.Enabled || string.IsNullOrWhiteSpace(c.Area)) return;
        if (c.LastCheckUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(Math.Max(6, c.CheckEveryHours))) return;
        // As the main person: their kitchen and meal plan when those aren't shared.
        using var asMain = UserScope.As(_settings.PrimaryUserId);
        var before = Current().Where(o => o.Score >= 4).Select(o => o.Key).ToHashSet();
        await CheckAsync(ct);
        var fresh = Current().Where(o => o.Score >= 4 && !before.Contains(o.Key)).ToList();
        if (c.Notify && fresh.Count > 0)
        {
            var lines = fresh.Take(5).Select(o => $"{o.Item} — {PriceText(o)} at {o.Store}");
            var ideas = OpenIdeas().Count(i => i.Kind == "meal");
            using (UserScope.Household.Meals ? UserScope.AsSystem() : UserScope.As(_settings.PrimaryUserId))
                await _notify.NotifyAsync($"🏷 {fresh.Count} deal{(fresh.Count == 1 ? "" : "s")} that suit you",
                    NotificationService.Bullets(lines, 5) + (ideas > 0 ? $"\n\n{ideas} meal idea{(ideas == 1 ? "" : "s")} for this week" : ""),
                    NoticeLevel.Info, "meals?tab=deals", $"deals:{_clock.Today}:{string.Join(",", fresh.Select(f => f.Id.ToString()[..6]))}", tag: "deals");
        }
    }

    /// <summary>Checks every store now (also the "Check now" button). Returns a one-line summary.</summary>
    public async Task<string> CheckAsync(CancellationToken ct = default)
    {
        if (!await _running.WaitAsync(0, ct)) return "Already checking…";
        try
        {
            var c = _settings.Current.Deals;
            var status = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(c.Area) && (c.LocatedArea != c.Area.Trim() || c.Latitude is null || c.Nearby.Count == 0))
            {
                Progress = $"Finding stores near {c.Area}…";
                try { await LocateAsync(ct); c = _settings.Current.Deals; }
                catch (Exception ex) when (!ct.IsCancellationRequested) { _log.LogInformation("Store lookup failed: {Message}", ex.Message); status["_area"] = ex.Message; }
            }

            var nearbyNames = c.Nearby.Select(n => n.Store).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var useNearby = c.NearbyOnly && c.Latitude is not null && nearbyNames.Count > 0;
            var found = 0;
            foreach (var store in c.Stores.Where(s => s.Enabled))
            {
                if (useNearby && !nearbyNames.Contains(store.Name)) { status[store.Name] = $"no branch within {c.RadiusMiles} miles"; continue; }
                if (string.IsNullOrWhiteSpace(store.OffersUrl)) { status[store.Name] = "emails and leaflets only"; continue; }
                Progress = $"Checking {store.Name}…";
                try
                {
                    var n = await CheckStoreAsync(store, ct);
                    found += n;
                    status[store.Name] = n > 0 ? $"{n} offer{(n == 1 ? "" : "s")}" : "no offers found on the page";
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    status[store.Name] = ex.Message;
                    _log.LogInformation("Deals: {Store} failed: {Message}", store.Name, ex.Message);
                }
            }
            if (c.UseEmails)
            {
                Progress = "Looking through supermarket emails…";
                var n = await FromEmailsAsync(c, useNearby ? nearbyNames : null);
                if (n > 0) status["_emails"] = $"{n} from emails";
                found += n;
            }
            Progress = "Matching deals to your kitchen and meals…";
            await PruneAsync();
            await ScoreAllAsync();
            var relevant = Current().Count(o => o.Score >= 4);
            var summary = $"{Current().Count} offers · {relevant} that suit you";
            if (c.SuggestMeals && _ollama.IsConfigured && relevant > 0)
            {
                Progress = "Thinking of meals for your week…";
                try { var ideas = await IdeasAsync(ct); summary += $" · {ideas.Count(i => i.Kind == "meal")} meal ideas"; }
                catch (Exception ex) when (!ct.IsCancellationRequested) { status["_ideas"] = ex.Message; _log.LogInformation("Deal ideas failed: {Message}", ex.Message); }
            }
            await _settings.UpdateAsync(s =>
            {
                s.Deals.LastCheckUtc = DateTime.UtcNow;
                s.Deals.LastResult = summary;
                s.Deals.StoreStatus = status;
            });
            return summary;
        }
        finally
        {
            Progress = "";
            _running.Release();
        }
    }

    // ------------------------------------------------------------------ area & nearby stores

    private static readonly Regex UkPostcode = new(@"^[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}$", RegexOptions.IgnoreCase);
    private static readonly Regex UkOutcode = new(@"^[A-Z]{1,2}\d[A-Z\d]?$", RegexOptions.IgnoreCase);

    private HttpClient Client()
    {
        var c = _http.CreateClient("web");
        c.Timeout = TimeSpan.FromSeconds(30);
        // OpenStreetMap's services ask for an identifying user agent.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("LifeHub/2.0 (self-hosted household app; https://github.com/Shotgn16/LifeHub)");
        return c;
    }

    /// <summary>Works out where the area is (postcodes.io for UK postcodes, OpenStreetMap otherwise) and which stores have a branch nearby.</summary>
    public async Task<string> LocateAsync(CancellationToken ct = default)
    {
        var c = _settings.Current.Deals;
        var area = c.Area.Trim();
        if (area.Length == 0) throw new InvalidOperationException("Enter a town, city or postcode first.");
        using var client = Client();
        double lat, lon;
        string label;
        if (UkPostcode.IsMatch(area) || UkOutcode.IsMatch(area))
        {
            var path = UkPostcode.IsMatch(area) ? "postcodes" : "outcodes";
            using var r = await client.GetAsync($"{c.PostcodeApi.TrimEnd('/')}/{path}/{Uri.EscapeDataString(area.Replace(" ", ""))}", ct);
            if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"Couldn't find the postcode {area}.");
            var res = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("result");
            lat = res.GetProperty("latitude").GetDouble();
            lon = res.GetProperty("longitude").GetDouble();
            label = path == "postcodes" && res.TryGetProperty("admin_district", out var d) && d.ValueKind == JsonValueKind.String
                ? $"{area.ToUpperInvariant()}, {d.GetString()}"
                : area.ToUpperInvariant();
        }
        else
        {
            using var r = await client.GetAsync($"{c.GeocodeApi.TrimEnd('/')}/search?format=json&limit=1&q={Uri.EscapeDataString(area)}", ct);
            if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"Couldn't look up {area} ({(int)r.StatusCode}).");
            var arr = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct)).RootElement;
            if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) throw new InvalidOperationException($"Couldn't find {area}. Try a postcode.");
            var first = arr[0];
            lat = double.Parse(first.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture);
            lon = double.Parse(first.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture);
            label = (first.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? area : area).Split(',').Take(2).Aggregate((a, b) => a + "," + b).Trim();
        }

        var metres = Math.Clamp(c.RadiusMiles, 1, 30) * 1609;
        var query = $"[out:json][timeout:25];nwr[\"shop\"~\"supermarket|convenience|frozen_food\"](around:{metres},{lat.ToString(CultureInfo.InvariantCulture)},{lon.ToString(CultureInfo.InvariantCulture)});out center tags;";
        var nearby = new List<NearbyStore>();
        try
        {
            using var body = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", query) });
            using var r = await client.PostAsync(c.OverpassApi, body, ct);
            if (r.IsSuccessStatusCode)
            {
                var root = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct)).RootElement;
                foreach (var el in root.GetProperty("elements").EnumerateArray())
                {
                    if (!el.TryGetProperty("tags", out var tags)) continue;
                    string T(string k) => tags.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
                    var name = T("name"); var brand = T("brand");
                    var text = (brand + " " + name + " " + T("operator")).ToLowerInvariant();
                    var store = c.Stores.FirstOrDefault(s => s.Match.Any(m => text.Contains(m.ToLowerInvariant())));
                    if (store is null) continue;
                    double elat, elon;
                    if (el.TryGetProperty("lat", out var la)) { elat = la.GetDouble(); elon = el.GetProperty("lon").GetDouble(); }
                    else if (el.TryGetProperty("center", out var ce)) { elat = ce.GetProperty("lat").GetDouble(); elon = ce.GetProperty("lon").GetDouble(); }
                    else continue;
                    nearby.Add(new NearbyStore { Store = store.Name, Name = name.Length > 0 ? name : store.Name, Miles = Math.Round(Miles(lat, lon, elat, elon), 1) });
                }
            }
            else _log.LogInformation("Overpass returned {Status}", (int)r.StatusCode);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { _log.LogInformation("Nearby store lookup failed: {Message}", ex.Message); }

        // Closest branch of each store.
        var closest = nearby.GroupBy(n => n.Store).Select(g => g.OrderBy(x => x.Miles).First()).OrderBy(n => n.Miles).ToList();
        await _settings.UpdateAsync(s =>
        {
            s.Deals.Latitude = lat; s.Deals.Longitude = lon; s.Deals.AreaLabel = label; s.Deals.LocatedArea = area;
            s.Deals.Nearby = closest;
        });
        return closest.Count == 0 ? $"{label}: no stores from your list found nearby" : $"{label}: {string.Join(", ", closest.Select(n => $"{n.Store} {n.Miles} mi"))}";
    }

    private static double Miles(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 3958.8;
        double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1); var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    // ------------------------------------------------------------------ finding offers

    private async Task<int> CheckStoreAsync(DealStore store, CancellationToken ct)
    {
        using var client = Client();
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; LifeHub/2.0; household meal planner)");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-GB,en;q=0.8");
        using var r = await client.GetAsync(store.OffersUrl, ct);
        if ((int)r.StatusCode is 401 or 403 or 429) throw new InvalidOperationException("the store blocks automated visits — its emails and leaflets still work");
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"offers page returned {(int)r.StatusCode}");
        var html = await r.Content.ReadAsStringAsync(ct);

        var offers = JsonLdOffers(html, store.Name, store.OffersUrl);
        var text = HtmlText.Extract(html, maxChars: 9000);
        if (offers.Count == 0)
        {
            if (text.Length < 500 || !Regex.IsMatch(text, @"£\s?\d|\d+p\b"))
                throw new InvalidOperationException("couldn't read the page (it's built in the browser) — its emails and leaflets still work");
            if (!_ollama.IsConfigured) throw new InvalidOperationException("needs the local AI to read the page");
            offers = await ExtractAsync(store.Name, text, "web", store.OffersUrl, ct);
        }
        return await SaveAsync(offers, store.Name, "web");
    }

    /// <summary>Products with prices published in the page's structured data (schema.org), when a store provides it.</summary>
    private static List<DealOffer> JsonLdOffers(string html, string store, string url)
    {
        var list = new List<DealOffer>();
        foreach (Match m in Regex.Matches(html, @"<script[^>]+application/ld\+json[^>]*>([\s\S]*?)</script>", RegexOptions.IgnoreCase))
        {
            try
            {
                using var doc = JsonDocument.Parse(m.Groups[1].Value);
                void Walk(JsonElement e)
                {
                    if (e.ValueKind == JsonValueKind.Array) { foreach (var x in e.EnumerateArray()) Walk(x); return; }
                    if (e.ValueKind != JsonValueKind.Object) return;
                    if (e.TryGetProperty("@type", out var t) && t.ToString().Contains("Product") && e.TryGetProperty("name", out var name))
                    {
                        decimal? price = null; DateOnly? until = null;
                        if (e.TryGetProperty("offers", out var of))
                        {
                            var o = of.ValueKind == JsonValueKind.Array && of.GetArrayLength() > 0 ? of[0] : of;
                            if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty("price", out var p) &&
                                decimal.TryParse(p.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var pv)) price = pv;
                            if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty("priceValidUntil", out var pu) && DateOnly.TryParse(pu.ToString()[..Math.Min(10, pu.ToString().Length)], CultureInfo.InvariantCulture, out var d)) until = d;
                        }
                        list.Add(new DealOffer { Store = store, Item = name.ToString().Trim(), Price = price, ValidUntil = until, Url = url, Source = "web" });
                    }
                    foreach (var key in new[] { "@graph", "itemListElement", "item" })
                        if (e.TryGetProperty(key, out var child)) Walk(child);
                }
                Walk(doc.RootElement);
            }
            catch (JsonException) { }
        }
        return list.Where(o => o.Item.Length > 1).DistinctBy(o => o.Item.ToLowerInvariant()).Take(150).ToList();
    }

    private static JsonObject ExtractSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["deals"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["item"] = new JsonObject { ["type"] = "string" },
                        ["price"] = new JsonObject { ["type"] = "string" },
                        ["was_price"] = new JsonObject { ["type"] = "string" },
                        ["offer"] = new JsonObject { ["type"] = "string" },
                        ["category"] = new JsonObject { ["type"] = "string" },
                        ["valid_until"] = new JsonObject { ["type"] = "string" },
                        ["food"] = new JsonObject { ["type"] = "boolean" }
                    },
                    ["required"] = new JsonArray("item", "price", "offer", "food")
                }
            }
        },
        ["required"] = new JsonArray("deals")
    };

    private const string ExtractSystem = """
        You read supermarket offers. List each product that is on offer: item (product name with size, e.g. "Chicken thighs 1kg"),
        price (the offer price as a number, e.g. "2.49", or "" if not given), was_price (number or ""), offer (e.g. "Half price",
        "3 for £5", "Clubcard price", "Super 6", "" if it's just a low price), category (Meat & Fish, Fruit & Veg, Dairy, Bakery,
        Frozen, Tins & Jars, Dry Goods, Drinks, Snacks, Household, Other), valid_until (yyyy-MM-dd if the text says when it ends,
        else ""), food (false for non-food such as toiletries, clothes, garden). Only include products actually in the text.
        Never invent prices. If there are no offers, return an empty list.
        """;

    /// <summary>The AI reads offers out of page text, an email, a pasted list or a leaflet photo.</summary>
    private async Task<List<DealOffer>> ExtractAsync(string store, string text, string source, string url, CancellationToken ct, byte[]? image = null)
    {
        var list = new List<DealOffer>();
        var today = _clock.Today;
        var pieces = image is not null ? new List<string> { "" } :
            Enumerable.Range(0, (int)Math.Ceiling(text.Length / 3000.0)).Select(i => text.Substring(i * 3000, Math.Min(3000, text.Length - i * 3000))).Take(3).ToList();
        foreach (var piece in pieces)
        {
            var prompt = image is not null
                ? $"STORE: {store}\nTODAY: {today:yyyy-MM-dd}\nList the offers in this leaflet / shelf photo."
                : $"STORE: {store}\nTODAY: {today:yyyy-MM-dd}\nTEXT:\n{piece}";
            var json = await _ollama.ChatJsonAsync(ExtractSystem, prompt, ExtractSchema(), ct,
                image is null ? null : new[] { image }, image is null ? null : _ollama.VisionModel);
            if (!json.TryGetProperty("deals", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var d in arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
            {
                string S(string n) => d.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
                var item = S("item");
                if (item.Length < 2 || item.Length > 120) continue;
                list.Add(new DealOffer
                {
                    Store = store, Item = item, Price = Money(S("price")), WasPrice = Money(S("was_price")), Offer = Clip(S("offer"), 60),
                    Category = S("category"), ValidUntil = DateOnly.TryParseExact(S("valid_until"), "yyyy-MM-dd", out var vu) && vu >= today.AddDays(-1) ? vu : null,
                    Food = !d.TryGetProperty("food", out var f) || f.ValueKind != JsonValueKind.False, Url = url, Source = source
                });
            }
        }
        return list;
    }

    private static decimal? Money(string s)
    {
        var m = Regex.Match(s.Replace(",", ""), @"(\d+(?:\.\d{1,2})?)\s*(p\b)?", RegexOptions.IgnoreCase);
        if (!m.Success || !decimal.TryParse(m.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return null;
        if (m.Groups[2].Success && v >= 1) v /= 100;
        return v is > 0 and < 1000 ? v : null;
    }

    private async Task<int> SaveAsync(List<DealOffer> found, string store, string source)
    {
        var existing = _offers.Where(o => o.Store.Equals(store, StringComparison.OrdinalIgnoreCase)).ToDictionary(o => o.Key, o => o);
        var save = new List<DealOffer>();
        foreach (var o in found.DistinctBy(o => o.Key))
        {
            if (existing.TryGetValue(o.Key, out var old))
            {
                old.Price = o.Price ?? old.Price; old.WasPrice = o.WasPrice ?? old.WasPrice; old.Offer = o.Offer.Length > 0 ? o.Offer : old.Offer;
                old.ValidUntil = o.ValidUntil ?? old.ValidUntil; old.FoundUtc = DateTime.UtcNow; old.Category = o.Category.Length > 0 ? o.Category : old.Category;
                save.Add(old);
            }
            else save.Add(o);
        }
        if (save.Count > 0) await _offers.UpsertManyAsync(save);
        return save.Count;
    }

    /// <summary>Supermarket newsletters: the AI already files their deals as promotions — use those from your stores.</summary>
    private async Task<int> FromEmailsAsync(DealSettings c, HashSet<string>? onlyStores)
    {
        IReadOnlyList<Promotion> promos;
        using (UserScope.AsSystem()) promos = _promos.Where(p => !p.Dismissed && (p.EndsOn is null || p.EndsOn >= _clock.Today));
        var n = 0;
        foreach (var store in c.Stores.Where(s => s.Enabled && (onlyStores is null || onlyStores.Contains(s.Name))))
        {
            var mine = promos.Where(p => store.Match.Any(m => p.Merchant.Contains(m, StringComparison.OrdinalIgnoreCase) || p.Title.Contains(m, StringComparison.OrdinalIgnoreCase)))
                .Select(p => new DealOffer
                {
                    Store = store.Name, Item = Clip(p.Title, 100), Price = p.Price, Offer = Clip(p.Description, 60), ValidUntil = p.EndsOn,
                    Url = p.WebLink, Source = "email"
                }).ToList();
            n += await SaveAsync(mine, store.Name, "email");
        }
        return n;
    }

    /// <summary>Offers you paste in (from an app, a website or a message).</summary>
    public async Task<int> AddFromTextAsync(string store, string text, CancellationToken ct = default)
    {
        if (!_ollama.IsConfigured) throw new InvalidOperationException("Turn on the local AI in Settings → AI to read offers.");
        if (string.IsNullOrWhiteSpace(store)) throw new InvalidOperationException("Choose the store");
        var found = await ExtractAsync(store.Trim(), text, "paste", "", ct);
        var n = await SaveAsync(found, store.Trim(), "paste");
        await ScoreAllAsync();
        return n;
    }

    /// <summary>A photo of the leaflet or the shelf labels, read by the vision model.</summary>
    public async Task<int> AddFromPhotoAsync(string store, byte[] image, CancellationToken ct = default)
    {
        if (!_ollama.HasVision) throw new InvalidOperationException("Set a vision model in Settings → AI to read leaflet photos.");
        if (string.IsNullOrWhiteSpace(store)) throw new InvalidOperationException("Choose the store");
        var found = await ExtractAsync(store.Trim(), "", "leaflet", "", ct, image);
        var n = await SaveAsync(found, store.Trim(), "leaflet");
        await ScoreAllAsync();
        return n;
    }

    private async Task PruneAsync()
    {
        var today = _clock.Today;
        // Expired offers, and page offers not seen for a week (the store has moved on).
        await _offers.DeleteManyAsync(_offers.Where(o => (o.ValidUntil is { } v && v < today) ||
            (o.ValidUntil is null && o.FoundUtc < DateTime.UtcNow.AddDays(o.Source == "web" ? -7 : -14))).Select(o => o.Id));
    }

    // ------------------------------------------------------------------ what matters to you

    private static HashSet<string> Words(string s) => Regex.Matches(s.ToLowerInvariant(), @"[a-z]{3,}")
        .Select(m => m.Value.EndsWith("ies") ? m.Value[..^3] + "y" : m.Value.EndsWith("oes") ? m.Value[..^2] : m.Value.EndsWith('s') && !m.Value.EndsWith("ss") ? m.Value[..^1] : m.Value)
        .Where(w => w is not ("and" or "the" or "with" or "for" or "pack" or "large" or "small" or "fresh" or "british" or "free" or "range"))
        .ToHashSet();

    /// <summary>"Chicken thighs 1kg" matches "chicken thighs" and "chicken".</summary>
    public static bool Matches(string item, string name)
    {
        var need = Words(name);
        return need.Count > 0 && need.IsSubsetOf(Words(item));
    }

    /// <summary>Scores every offer against your shopping list, staples, recipes and plan; flags dietary clashes.</summary>
    public async Task ScoreAllAsync()
    {
        var today = _clock.Today;
        // (not items LifeHub put on the list from a deal — they'd just match themselves)
        var shopping = _kitchen.Shopping.Where(s => !s.Checked && !s.Reason.StartsWith("Deal:")).Select(s => s.Name).ToList();
        var staples = _kitchen.Staples.All().SelectMany(s => s.Aliases.Prepend(s.Name)).ToList();
        var recipes = _meals.Recipes.All();
        var planned = _meals.EntriesFor(today, today.AddDays(13)).Where(e => e.RecipeId is not null).Select(e => e.RecipeId!.Value).ToHashSet();
        var all = _offers.All();
        foreach (var o in all)
        {
            var why = new List<string>();
            var score = o.Food ? 1 : -3;
            if (shopping.FirstOrDefault(s => Matches(o.Item, s)) is { } sh) { why.Add($"on your shopping list ({sh})"); score += 5; }
            if (staples.FirstOrDefault(s => Matches(o.Item, s)) is { } st) { why.Add($"a staple ({st})"); score += 4; }
            foreach (var r in recipes)
            {
                var ing = r.Ingredients.FirstOrDefault(i => i.Name.Length > 2 && Matches(o.Item, i.Name));
                if (ing is null) continue;
                var isPlanned = planned.Contains(r.Id);
                why.Add(isPlanned ? $"for {r.Name} (planned)" : $"used in {r.Name}");
                score += isPlanned ? 4 : 2;
                if (why.Count >= 4) break;
            }
            if (o.WasPrice is { } was && o.Price is { } now && was > 0 && now < was) score += now <= was / 2 ? 2 : 1;
            var clash = o.Food ? _meals.DietClash(o.Item) : "";
            if (clash.Length > 0) score -= 4;
            o.Matches = why.Distinct().Take(4).ToList();
            o.DietWarning = clash;
            o.Score = score;
        }
        if (all.Count > 0) await _offers.UpsertManyAsync(all);
    }

    // ------------------------------------------------------------------ ideas for the week

    private static JsonObject IdeasSchema()
    {
        JsonObject Str() => new() { ["type"] = "string" };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["trips"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["store"] = Str(), ["date"] = Str(), ["time"] = Str(), ["why"] = Str() },
                        ["required"] = new JsonArray("store", "date", "why")
                    }
                },
                ["meals"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["date"] = Str(), ["meal"] = Str(), ["why"] = Str(),
                            ["deals"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" } }
                        },
                        ["required"] = new JsonArray("date", "meal", "why", "deals")
                    }
                },
                ["ingredients"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["deal"] = new JsonObject { ["type"] = "integer" }, ["idea"] = Str() },
                        ["required"] = new JsonArray("deal", "idea")
                    }
                }
            },
            ["required"] = new JsonArray("trips", "meals", "ingredients")
        };
    }

    /// <summary>
    /// Asks the local AI for this week: meals that use the deals on days that suit them (busy days get quick meals),
    /// ingredients worth picking up, and when to fit in the shop (a free slot before the offers end).
    /// </summary>
    public async Task<List<DealIdea>> IdeasAsync(CancellationToken ct = default)
    {
        if (!_ollama.IsConfigured) throw new InvalidOperationException("Turn on the local AI in Settings → AI for meal ideas.");
        var today = _clock.Today;
        var deals = Current().Where(o => o.Food && o.Score >= 2 && o.DietWarning.Length == 0).Take(25).ToList();
        if (deals.Count == 0) deals = Current().Where(o => o.Food && o.DietWarning.Length == 0).Take(20).ToList();
        if (deals.Count == 0) return new();
        var days = await _meals.DayContextsAsync(today, today.AddDays(6));
        var plan = _meals.EntriesFor(today, today.AddDays(6)).Where(e => !e.Cooked).ToList();
        var m = _settings.Current.Meals;
        var nearby = _settings.Current.Deals.Nearby;

        var sb = new StringBuilder();
        sb.AppendLine($"TODAY: {today:yyyy-MM-dd} {today.DayOfWeek}");
        if (m.Requirements.Count > 0) sb.AppendLine($"DIETARY REQUIREMENTS (must be met): {string.Join(", ", m.Requirements)}");
        if (!string.IsNullOrWhiteSpace(m.Preferences)) sb.AppendLine($"PREFERENCES: {m.Preferences}");
        sb.AppendLine("\nDAYS (energy = time/energy for cooking; free = free time in the evening):");
        foreach (var d in days)
            sb.AppendLine($"- {d.Date:yyyy-MM-dd} {d.Date.DayOfWeek}: energy {d.Energy}; {d.Summary}{(d.Events.Count > 0 ? "; " + string.Join(", ", d.Events.Take(3)) : "")}");
        sb.AppendLine("\nALREADY PLANNED (don't replace these):");
        foreach (var e in plan) sb.AppendLine($"- {e.Date:yyyy-MM-dd} {e.Slot}: {_meals.TitleOf(e)}");
        if (plan.Count == 0) sb.AppendLine("- nothing yet");
        sb.AppendLine("\nDEALS (number. item — price at store, until; why it matters):");
        for (var i = 0; i < deals.Count; i++)
        {
            var o = deals[i];
            sb.AppendLine($"D{i + 1}. {o.Item} — {PriceText(o)} at {o.Store}{(o.ValidUntil is { } v ? $", until {v:ddd d MMM}" : "")}{(o.Matches.Count > 0 ? "; " + string.Join(", ", o.Matches) : "")}");
        }
        if (nearby.Count > 0) sb.AppendLine($"\nSTORES NEARBY: {string.Join(", ", nearby.Select(n => $"{n.Store} ({n.Miles} miles)"))}");
        sb.AppendLine($"\nKITCHEN: {_meals.StockSummary(40)}");

        const string system = """
            You help a household plan the week around supermarket deals near them.
            meals: 2-5 dinner ideas for days that have NO meal planned yet. Each uses one or more deals (deal numbers) and fits that day's
                   energy: quick/easy meals on low-energy or busy days, more involved cooking on free days. Meet every dietary requirement.
                   meal = the dish name; why = one sentence (the deal, and why that day).
            ingredients: 1-4 deals worth picking up anyway (stock up on a staple, freeze for later, a cheap swap) with a short idea for each.
            trips: 1-2 shopping trips: which store, a date (and a time window if obvious) when they have free time, before the deals end and
                   before the meals that need them. why = one sentence.
            Use only the deals listed and real days from the list. Never invent prices.
            """;
        var json = await _ollama.ChatJsonAsync(system, sb.ToString(), IdeasSchema(), ct);

        var model = _settings.Current.Ollama.Model;
        string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
        DateOnly? Day(string s) => DateOnly.TryParseExact(s.Length >= 10 ? s[..10] : s, "yyyy-MM-dd", out var d) && d >= today && d <= today.AddDays(13) ? d : null;
        var ideas = new List<DealIdea>();
        if (json.TryGetProperty("trips", out var trips) && trips.ValueKind == JsonValueKind.Array)
            foreach (var t in trips.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Take(2))
            {
                var store = S(t, "store");
                if (store.Length == 0) continue;
                ideas.Add(new DealIdea { Kind = "trip", Store = store, Day = Day(S(t, "date")), Time = Clip(S(t, "time"), 30),
                    Title = $"Shop at {store}", Body = S(t, "why"), Model = model,
                    DealIds = deals.Where(d => d.Store.Equals(store, StringComparison.OrdinalIgnoreCase)).Select(d => d.Id).ToList() });
            }
        var plannedDays = plan.Select(p => p.Date).ToHashSet();
        if (json.TryGetProperty("meals", out var meals) && meals.ValueKind == JsonValueKind.Array)
            foreach (var x in meals.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Take(5))
            {
                var name = S(x, "meal");
                if (name.Length == 0) continue;
                var used = x.TryGetProperty("deals", out var ds) && ds.ValueKind == JsonValueKind.Array
                    ? ds.EnumerateArray().Where(n => n.TryGetInt32(out var k) && k >= 1 && k <= deals.Count).Select(n => deals[n.GetInt32() - 1]).ToList()
                    : new();
                var day = Day(S(x, "date"));
                ideas.Add(new DealIdea { Kind = "meal", Title = Clip(name, 80), Body = S(x, "why"), Day = day is { } dd && plannedDays.Contains(dd) ? null : day,
                    Store = string.Join(", ", used.Select(u => u.Store).Distinct()), DealIds = used.Select(u => u.Id).ToList(), Model = model });
            }
        if (json.TryGetProperty("ingredients", out var ings) && ings.ValueKind == JsonValueKind.Array)
            foreach (var x in ings.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Take(4))
            {
                if (!x.TryGetProperty("deal", out var dn) || !dn.TryGetInt32(out var k) || k < 1 || k > deals.Count) continue;
                var d = deals[k - 1];
                ideas.Add(new DealIdea { Kind = "ingredient", Title = d.Item, Body = S(x, "idea"), Store = d.Store, DealIds = new() { d.Id }, Model = model });
            }
        if (ideas.Count == 0) throw new AiFormatException("The AI didn't come up with ideas this time. Try again.");
        await _ideas.DeleteManyAsync(_ideas.Where(i => i.Status == "open").Select(i => i.Id));
        await _ideas.UpsertManyAsync(ideas);
        return ideas;
    }

    // ------------------------------------------------------------------ acting on them

    public async Task<ShoppingItem> AddToShoppingAsync(Guid offerId)
    {
        var o = _offers.Get(offerId) ?? throw new InvalidOperationException("That offer has gone");
        var item = await _kitchen.AddToShoppingAsync(o.Item, 1, "", $"Deal: {o.Store} {PriceText(o)}".Trim(), string.IsNullOrEmpty(o.Category) ? null : o.Category);
        o.AddedToList = true;
        await _offers.UpsertAsync(o);
        return item;
    }

    /// <summary>Puts a meal idea on its day in the plan, and its deal items on the shopping list.</summary>
    public async Task<string> PlanAsync(Guid ideaId, DateOnly? date = null)
    {
        var idea = _ideas.Get(ideaId) ?? throw new InvalidOperationException("That idea has gone");
        var day = date ?? idea.Day ?? NextFreeDay();
        var slot = _settings.Current.Meals.Slots.FirstOrDefault() ?? "Dinner";
        var recipe = _meals.Recipes.Where(r => r.Name.Equals(idea.Title, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        await _meals.Entries.UpsertAsync(new MealPlanEntry
        {
            Date = day, Slot = slot, RecipeId = recipe?.Id, Title = recipe is null ? idea.Title : "",
            Servings = _settings.Current.Meals.DefaultServings, Notes = $"From this week's deals{(idea.Store.Length > 0 ? " (" + idea.Store + ")" : "")}"
        });
        var added = 0;
        foreach (var id in idea.DealIds)
            if (_offers.Get(id) is { AddedToList: false }) { await AddToShoppingAsync(id); added++; }
        idea.Status = "planned";
        await _ideas.UpsertAsync(idea);
        return $"{idea.Title} planned for {day.ToString("dddd", CultureInfo.GetCultureInfo(_settings.Current.General.Culture))}" + (added > 0 ? $" · {added} added to the shopping list" : "");
    }

    private DateOnly NextFreeDay()
    {
        var today = _clock.Today;
        var taken = _meals.EntriesFor(today, today.AddDays(13)).Select(e => e.Date).ToHashSet();
        for (var d = today; d <= today.AddDays(13); d = d.AddDays(1)) if (!taken.Contains(d)) return d;
        return today.AddDays(1);
    }

    public async Task DismissIdeaAsync(Guid id)
    {
        if (_ideas.Get(id) is not { } i) return;
        i.Status = "dismissed";
        await _ideas.UpsertAsync(i);
    }

    public async Task DismissAsync(Guid offerId)
    {
        if (_offers.Get(offerId) is not { } o) return;
        o.Dismissed = true;
        await _offers.UpsertAsync(o);
    }

    public static string PriceText(DealOffer o)
    {
        var parts = new List<string>();
        if (o.Price is { } p) parts.Add(p < 1 ? $"{(int)Math.Round(p * 100)}p" : $"£{p:0.00}");
        if (o.Offer.Length > 0 && !parts.Any(x => o.Offer.Contains(x))) parts.Add(o.Offer);
        if (o.WasPrice is { } w && o.Price is not null) parts.Add($"was £{w:0.00}");
        return string.Join(" · ", parts);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";
}
