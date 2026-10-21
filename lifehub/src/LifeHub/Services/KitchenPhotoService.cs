using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LifeHub.Integrations.Ollama;

namespace LifeHub.Services;

/// <summary>One item found in a photo, waiting for you to check it before it goes into the kitchen.</summary>
public sealed class PhotoItem
{
    public Guid Key { get; set; } = Guid.NewGuid();
    public bool Include { get; set; } = true;
    public string Name { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public string Unit { get; set; } = "";
    public string Category { get; set; } = "Other";
    public StorageLocation Location { get; set; } = StorageLocation.Fridge;
    public DateOnly? Date { get; set; }
    public DateKind Kind { get; set; } = DateKind.UseBy;
    /// <summary>photo · close-up · manual · estimate · "" (none yet)</summary>
    public string DateSource { get; set; } = "";
    /// <summary>The date exactly as the AI read it from the pack.</summary>
    public string DateText { get; set; } = "";
    public string Where { get; set; } = "";
    public double Confidence { get; set; } = 0.8;
    /// <summary>Ticked to include in the next close-up photo of dates.</summary>
    public bool Selected { get; set; }
    public bool NeedsDate => Date is null && Location != StorageLocation.Freezer;
}

/// <summary>
/// "Add from photo": the local vision model lists the groceries in a picture and reads any printed dates.
/// Items without a readable date get a close-up photo, a typed date, or a typical-shelf-life estimate.
/// </summary>
public sealed class KitchenPhotoService
{
    private readonly OllamaClient _ollama;
    private readonly KitchenService _kitchen;
    private readonly SettingsService _settings;
    private readonly Clock _clock;

    public KitchenPhotoService(OllamaClient ollama, KitchenService kitchen, SettingsService settings, Clock clock)
    {
        _ollama = ollama; _kitchen = kitchen; _settings = settings; _clock = clock;
    }

    public bool Available => _ollama.IsConfigured;
    public bool HasVision => _ollama.HasVision;

    public async Task<List<PhotoItem>> IdentifyAsync(IReadOnlyList<byte[]> photos, CancellationToken ct = default)
    {
        if (!_ollama.IsConfigured) throw new InvalidOperationException("Set up the local AI (Ollama) in Settings → AI first.");
        var g = _settings.Current.General;
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["name"] = new JsonObject { ["type"] = "string" },
                            ["quantity"] = new JsonObject { ["type"] = "integer" },
                            ["category"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(g.FoodCategories.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()) },
                            ["storage"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("fridge", "freezer", "cupboard") },
                            ["date_printed"] = new JsonObject { ["type"] = "string" },
                            ["date_type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("use_by", "best_before", "none") },
                            ["position"] = new JsonObject { ["type"] = "string" },
                            ["confidence"] = new JsonObject { ["type"] = "number" }
                        },
                        ["required"] = new JsonArray("name", "quantity", "category", "storage", "date_printed", "date_type", "confidence")
                    }
                }
            },
            ["required"] = new JsonArray("items")
        };
        const string system = "You identify groceries in photos for a household food-stock app. Be accurate and never invent items or dates.";
        const string prompt = """
            List every distinct food or grocery item you can see in this photo.
            - name: what it is in plain words, with the brand if it's clearly readable (e.g. "Kellogg's Corn Flakes", "Free range eggs", "Semi-skimmed milk 2L").
            - quantity: how many of that exact item are visible.
            - storage: where it should be kept (fridge, freezer or cupboard).
            - date_printed: ONLY if a use-by / best-before / BB / display-until date is clearly readable on the pack, copy it
              exactly as printed (e.g. "12 NOV", "03/2027", "24.10.26"). Otherwise "". Never guess a date.
            - date_type: use_by if the label says use by; best_before for best before / BB / BBE; none if no date is readable.
            - position: a few words to find it in the photo (e.g. "back left, red box").
            - confidence: 0–1, how sure you are about the item.
            Ignore non-food items, bags, hands and furniture.
            """;
        var all = new List<PhotoItem>();
        foreach (var photo in photos)
        {
            var json = await _ollama.ChatJsonAsync(system, prompt, schema, ct, new[] { photo });
            foreach (var item in ParseItems(json))
            {
                // The same item in two photos of one shop shouldn't count twice.
                var same = all.FirstOrDefault(x => Norm(x.Name) == Norm(item.Name));
                if (same is null) all.Add(item);
                else
                {
                    same.Quantity = Math.Max(same.Quantity, item.Quantity);
                    if (same.Date is null && item.Date is not null) { same.Date = item.Date; same.DateText = item.DateText; same.Kind = item.Kind; same.DateSource = "photo"; }
                }
            }
        }
        return all;
    }

    private List<PhotoItem> ParseItems(JsonElement json)
    {
        var list = new List<PhotoItem>();
        if (!json.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        var today = _clock.Today;
        foreach (var x in arr.EnumerateArray())
        {
            string S(string n) => x.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
            double N(string n, double d) => x.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : d;
            var name = S("name");
            if (name.Length < 2 || name.Length > 80) continue;
            var confidence = Math.Clamp(N("confidence", 0.7), 0, 1);
            if (confidence < 0.3) continue;
            var category = S("category");
            if (!_settings.Current.General.FoodCategories.Contains(category)) category = "Other";
            var location = S("storage") switch { "freezer" => StorageLocation.Freezer, "cupboard" => StorageLocation.Cupboard, _ => StorageLocation.Fridge };
            var staple = _kitchen.Staples.All().FirstOrDefault(s => s.Matches(name));
            var item = new PhotoItem
            {
                Name = name, Quantity = Math.Clamp((decimal)N("quantity", 1), 1, 99), Category = staple?.Category ?? category,
                Location = staple?.DefaultLocation ?? location, Where = S("position"), Confidence = confidence,
                Kind = Freshness.KindFor(name, staple)
            };
            var printed = S("date_printed");
            var type = S("date_type");
            if (printed.Length > 0 && ParseDate(printed, today) is { } d)
            {
                item.Date = d;
                item.DateText = printed;
                item.DateSource = "photo";
                if (type == "use_by") item.Kind = DateKind.UseBy;
                else if (type == "best_before") item.Kind = DateKind.BestBefore;
            }
            list.Add(item);
        }
        return list;
    }

    /// <summary>
    /// Reads dates from close-up photos and matches them to the chosen items. Returns how many were filled in.
    /// </summary>
    public async Task<int> ReadDatesAsync(IReadOnlyList<PhotoItem> targets, IReadOnlyList<byte[]> closeUps, CancellationToken ct = default)
    {
        if (targets.Count == 0 || closeUps.Count == 0) return 0;
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["dates"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["item"] = new JsonObject { ["type"] = "integer" },
                            ["date_printed"] = new JsonObject { ["type"] = "string" },
                            ["date_type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("use_by", "best_before") }
                        },
                        ["required"] = new JsonArray("item", "date_printed", "date_type")
                    }
                }
            },
            ["required"] = new JsonArray("dates")
        };
        var numbered = string.Join("\n", targets.Select((t, i) => $"{i + 1}. {t.Name}{(string.IsNullOrEmpty(t.Where) ? "" : $" ({t.Where})")}"));
        var prompt = $"""
            These are close-up photos of date labels. The items are:
            {numbered}
            For every use-by or best-before date you can CLEARLY read, give the item number it belongs to, the date exactly
            as printed (e.g. "12 NOV", "03/2027") and whether it's use_by or best_before. Skip dates you can't read.
            {(targets.Count == 1 ? "There is only one item, so any readable date belongs to item 1." : "")}
            """;
        var filled = 0;
        var today = _clock.Today;
        foreach (var photo in closeUps)
        {
            var json = await _ollama.ChatJsonAsync("You read printed food date labels accurately and never guess.", prompt, schema, ct, new[] { photo });
            if (!json.TryGetProperty("dates", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var x in arr.EnumerateArray())
            {
                var idx = x.TryGetProperty("item", out var iv) && iv.ValueKind == JsonValueKind.Number ? iv.GetInt32() - 1 : -1;
                if (idx < 0 || idx >= targets.Count) continue;
                var printed = x.TryGetProperty("date_printed", out var dp) ? dp.GetString() ?? "" : "";
                if (ParseDate(printed, today) is not { } d) continue;
                var t = targets[idx];
                t.Date = d;
                t.DateText = printed;
                t.DateSource = "close-up";
                t.Selected = false;
                if (x.TryGetProperty("date_type", out var dt))
                    t.Kind = dt.GetString() == "use_by" ? DateKind.UseBy : DateKind.BestBefore;
                filled++;
            }
        }
        return filled;
    }

    /// <summary>Fills a typical date for something without a readable one (eggs ~4 weeks, cereal ~6 months…).</summary>
    public bool Estimate(PhotoItem item)
    {
        var staple = _kitchen.Staples.All().FirstOrDefault(s => s.Matches(item.Name));
        var (date, kind) = Freshness.Estimate(item.Name, staple, null, _clock.Today, item.Location);
        if (date is null) return false;
        item.Date = date;
        item.Kind = kind;
        item.DateSource = "estimate";
        item.DateText = "";
        return true;
    }

    public async Task<int> AddAllAsync(IEnumerable<PhotoItem> items)
    {
        var n = 0;
        foreach (var i in items.Where(i => i.Include && !string.IsNullOrWhiteSpace(i.Name)))
        {
            await _kitchen.AddToPantryAsync(new PantryItem
            {
                Name = i.Name.Trim(), Quantity = i.Quantity <= 0 ? 1 : i.Quantity, Unit = i.Unit, Category = i.Category, Location = i.Location,
                UseBy = i.Location == StorageLocation.Freezer ? null : i.Date,
                DateEstimated = i.DateSource == "estimate",
                Notes = i.DateText.Length > 0 ? $"Date on pack: {i.DateText}" : ""
            }, kind: i.Date is null ? null : i.Kind);
            n++;
        }
        return n;
    }

    private static string Norm(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]", "");

    private static readonly string[] Months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

    /// <summary>
    /// Understands what's printed on packs: "12 NOV", "12 Nov 2026", "12NOV26", "24/10/26", "24.10.2026",
    /// "2026-10-24", "03/2027" or "NOV 2027" (month only → end of month). Day/month order follows your culture.
    /// A missing year means the next time that date comes round (allowing for things a little past their date).
    /// </summary>
    public DateOnly? ParseDate(string? text, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.ToLowerInvariant();
        t = Regex.Replace(t, @"\b(use|best|before|by|bb|bbe|display|until|end|exp|expiry|expires|date)\b|:", " ");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        var monthFirst = _settings.Current.General.Culture.StartsWith("en-US", StringComparison.OrdinalIgnoreCase);

        int Year(int y) => y < 100 ? 2000 + y : y;
        DateOnly? Make(int y, int m, int d)
        {
            if (m < 1 || m > 12 || d < 1 || d > DateTime.DaysInMonth(y, m)) return null;
            return new DateOnly(y, m, d);
        }
        DateOnly? NoYear(int m, int d)
        {
            var candidate = Make(today.Year, m, d);
            if (candidate is null) return null;
            return candidate.Value < today.AddDays(-60) ? Make(today.Year + 1, m, d) : candidate;
        }

        Match mt;
        if ((mt = Regex.Match(t, @"\b(\d{4})-(\d{1,2})-(\d{1,2})\b")).Success)
            return Make(int.Parse(mt.Groups[1].Value), int.Parse(mt.Groups[2].Value), int.Parse(mt.Groups[3].Value));
        if ((mt = Regex.Match(t, @"\b(\d{1,2})\s*([a-z]{3})[a-z]*\.?\s*(\d{2,4})?\b")).Success && Array.IndexOf(Months, mt.Groups[2].Value) is var mi and >= 0)
        {
            var d = int.Parse(mt.Groups[1].Value);
            return mt.Groups[3].Success ? Make(Year(int.Parse(mt.Groups[3].Value)), mi + 1, d) : NoYear(mi + 1, d);
        }
        if ((mt = Regex.Match(t, @"\b([a-z]{3})[a-z]*\.?\s*(\d{1,2})(?:st|nd|rd|th)?,?\s*(\d{4})\b")).Success && Array.IndexOf(Months, mt.Groups[1].Value) is var mi2 and >= 0)
            return Make(int.Parse(mt.Groups[3].Value), mi2 + 1, int.Parse(mt.Groups[2].Value));
        if ((mt = Regex.Match(t, @"\b(\d{1,2})[/.\- ](\d{1,2})[/.\- ](\d{2,4})\b")).Success)
        {
            int a = int.Parse(mt.Groups[1].Value), b = int.Parse(mt.Groups[2].Value), y = Year(int.Parse(mt.Groups[3].Value));
            return monthFirst ? Make(y, a, b) ?? Make(y, b, a) : Make(y, b, a) ?? Make(y, a, b);
        }
        if ((mt = Regex.Match(t, @"\b([a-z]{3})[a-z]*\.?\s*(\d{2,4})\b")).Success && Array.IndexOf(Months, mt.Groups[1].Value) is var mi3 and >= 0)
        {
            var y = Year(int.Parse(mt.Groups[2].Value));
            if (y >= 2000 && y < 2100) return new DateOnly(y, mi3 + 1, DateTime.DaysInMonth(y, mi3 + 1));
        }
        if ((mt = Regex.Match(t, @"\b(\d{1,2})[/.\-](\d{4})\b")).Success)
        {
            int m = int.Parse(mt.Groups[1].Value), y = int.Parse(mt.Groups[2].Value);
            if (m is >= 1 and <= 12) return new DateOnly(y, m, DateTime.DaysInMonth(y, m));
        }
        if ((mt = Regex.Match(t, @"\b(\d{1,2})[/.\-](\d{1,2})\b")).Success)
        {
            int a = int.Parse(mt.Groups[1].Value), b = int.Parse(mt.Groups[2].Value);
            return monthFirst ? NoYear(a, b) ?? NoYear(b, a) : NoYear(b, a) ?? NoYear(a, b);
        }
        return null;
    }
}
