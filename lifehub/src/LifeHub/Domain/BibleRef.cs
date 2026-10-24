using System.Text;
using System.Text.RegularExpressions;

namespace LifeHub.Domain;

public sealed record BibleBook(int Index, string Name, string Usfm, int Chapters, bool NewTestament)
{
    /// <summary>"song-of-solomon", "1-corinthians" (StudyLight style).</summary>
    public string Slug => Name.ToLowerInvariant().Replace(' ', '-');
}

/// <summary>A passage: a book with a chapter (and optionally verse) range.</summary>
public sealed record BibleRef(int Book, int Chapter, int? Verse = null, int? EndChapter = null, int? EndVerse = null)
{
    public BibleBook BookInfo => BibleBooks.All[Book];
    public int LastChapter => EndChapter ?? Chapter;

    /// <summary>Position of the first and last verse covered, for overlap checks.</summary>
    public (long From, long To) Span
    {
        get
        {
            long Pos(int ch, int v) => (long)Book * 1_000_000 + ch * 1000L + v;
            var from = Pos(Chapter, Verse ?? 0);
            var to = EndVerse is { } ev ? Pos(LastChapter, ev) : Verse is { } v && EndChapter is null ? Pos(Chapter, v) : Pos(LastChapter, 999);
            return (from, to);
        }
    }

    public bool Overlaps(BibleRef other)
    {
        var (a1, a2) = Span; var (b1, b2) = other.Span;
        return a1 <= b2 && b1 <= a2;
    }

    public bool SameChapterAs(BibleRef other) =>
        Book == other.Book && Chapter <= other.LastChapter && other.Chapter <= LastChapter;

    public override string ToString()
    {
        var b = BookInfo;
        var name = b.Name == "Psalms" && EndChapter is null ? "Psalm" : b.Name;
        var sb = new StringBuilder(name);
        if (b.Chapters == 1 && Verse is not null) sb.Append(' ').Append(Verse);   // Jude 3
        else
        {
            sb.Append(' ').Append(Chapter);
            if (Verse is { } v) sb.Append(':').Append(v);
        }
        if (EndChapter is { } ec && ec != Chapter)
        {
            sb.Append('-').Append(ec);
            if (EndVerse is { } ev) sb.Append(':').Append(ev);
        }
        else if (EndVerse is { } ev2 && ev2 != Verse) sb.Append('-').Append(ev2);
        return sb.ToString();
    }

    /// <summary>JHN.3.16, JHN.3.16-18 or JHN.3 (YouVersion / bible.com).</summary>
    public string Usfm
    {
        get
        {
            var u = $"{BookInfo.Usfm}.{Chapter}";
            if (Verse is { } v)
            {
                u += $".{v}";
                if ((EndChapter is null || EndChapter == Chapter) && EndVerse is { } ev && ev != v) u += $"-{ev}";
            }
            return u;
        }
    }

    public static BibleRef? FromUsfm(string? usfm)
    {
        if (string.IsNullOrWhiteSpace(usfm)) return null;
        var parts = usfm.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var first = ParseOneUsfm(parts[0]);
        if (first is null || parts.Length == 1) return first;
        var last = ParseOneUsfm(parts[^1]);
        if (last is null || last.Book != first.Book) return first;
        return first with { EndChapter = last.Chapter != first.Chapter ? last.Chapter : null, EndVerse = last.EndVerse ?? last.Verse };
    }

    private static BibleRef? ParseOneUsfm(string s)
    {
        var m = Regex.Match(s.Trim().ToUpperInvariant(), @"^([1-3]?[A-Z]{2,3})\.(\d+)(?:\.(\d+)(?:-(\d+))?)?$");
        if (!m.Success) return null;
        var book = BibleBooks.All.FirstOrDefault(b => b.Usfm == m.Groups[1].Value);
        if (book is null) return null;
        int? v = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : null;
        int? ev = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : null;
        return new BibleRef(book.Index, int.Parse(m.Groups[2].Value), v, null, ev);
    }
}

public static class BibleBooks
{
    public static readonly IReadOnlyList<BibleBook> All = Build();

    private static List<BibleBook> Build()
    {
        var data = new (string Name, string Usfm, int Ch)[]
        {
            ("Genesis","GEN",50),("Exodus","EXO",40),("Leviticus","LEV",27),("Numbers","NUM",36),("Deuteronomy","DEU",34),
            ("Joshua","JOS",24),("Judges","JDG",21),("Ruth","RUT",4),("1 Samuel","1SA",31),("2 Samuel","2SA",24),
            ("1 Kings","1KI",22),("2 Kings","2KI",25),("1 Chronicles","1CH",29),("2 Chronicles","2CH",36),("Ezra","EZR",10),
            ("Nehemiah","NEH",13),("Esther","EST",10),("Job","JOB",42),("Psalms","PSA",150),("Proverbs","PRO",31),
            ("Ecclesiastes","ECC",12),("Song of Solomon","SNG",8),("Isaiah","ISA",66),("Jeremiah","JER",52),("Lamentations","LAM",5),
            ("Ezekiel","EZK",48),("Daniel","DAN",12),("Hosea","HOS",14),("Joel","JOL",3),("Amos","AMO",9),
            ("Obadiah","OBA",1),("Jonah","JON",4),("Micah","MIC",7),("Nahum","NAM",3),("Habakkuk","HAB",3),
            ("Zephaniah","ZEP",3),("Haggai","HAG",2),("Zechariah","ZEC",14),("Malachi","MAL",4),
            ("Matthew","MAT",28),("Mark","MRK",16),("Luke","LUK",24),("John","JHN",21),("Acts","ACT",28),
            ("Romans","ROM",16),("1 Corinthians","1CO",16),("2 Corinthians","2CO",13),("Galatians","GAL",6),("Ephesians","EPH",6),
            ("Philippians","PHP",4),("Colossians","COL",4),("1 Thessalonians","1TH",5),("2 Thessalonians","2TH",3),("1 Timothy","1TI",6),
            ("2 Timothy","2TI",4),("Titus","TIT",3),("Philemon","PHM",1),("Hebrews","HEB",13),("James","JAS",5),
            ("1 Peter","1PE",5),("2 Peter","2PE",3),("1 John","1JN",5),("2 John","2JN",1),("3 John","3JN",1),
            ("Jude","JUD",1),("Revelation","REV",22)
        };
        return data.Select((d, i) => new BibleBook(i, d.Name, d.Usfm, d.Ch, i >= 39)).ToList();
    }

    public static int TotalChapters => All.Sum(b => b.Chapters);

    private static readonly Dictionary<string, int> Lookup = BuildLookup();

    private static string Key(string s) => Regex.Replace(s.ToLowerInvariant(), @"[\s.]", "");

    private static Dictionary<string, int> BuildLookup()
    {
        var d = new Dictionary<string, int>();
        void Add(string alias, string book)
        {
            var idx = All.First(b => b.Name == book).Index;
            d.TryAdd(Key(alias), idx);
        }
        foreach (var b in All) { d.TryAdd(Key(b.Name), b.Index); d.TryAdd(Key(b.Usfm), b.Index); }
        var extra = new Dictionary<string, string[]>
        {
            ["Genesis"] = new[] { "Gen", "Gn" }, ["Exodus"] = new[] { "Ex", "Exod" }, ["Leviticus"] = new[] { "Lev", "Lv" },
            ["Numbers"] = new[] { "Num", "Nm" }, ["Deuteronomy"] = new[] { "Deut", "Dt" }, ["Joshua"] = new[] { "Josh" },
            ["Judges"] = new[] { "Judg", "Jdg", "Jdgs" }, ["Ruth"] = new[] { "Ru", "Rth" }, ["Nehemiah"] = new[] { "Neh" },
            ["Esther"] = new[] { "Esth", "Est" }, ["Psalms"] = new[] { "Psalm", "Ps", "Psa", "Pss", "Psm" },
            ["Proverbs"] = new[] { "Prov", "Pr", "Prv" }, ["Ecclesiastes"] = new[] { "Eccl", "Ecc", "Qoh", "Eccles" },
            ["Song of Solomon"] = new[] { "Song", "Song of Songs", "SoS", "Canticles", "Sg" }, ["Isaiah"] = new[] { "Isa", "Is" },
            ["Jeremiah"] = new[] { "Jer", "Jr" }, ["Lamentations"] = new[] { "Lam" }, ["Ezekiel"] = new[] { "Ezek", "Eze", "Ezk" },
            ["Daniel"] = new[] { "Dan", "Dn" }, ["Hosea"] = new[] { "Hos" }, ["Obadiah"] = new[] { "Obad", "Ob" },
            ["Jonah"] = new[] { "Jon", "Jnh" }, ["Micah"] = new[] { "Mic" }, ["Nahum"] = new[] { "Nah" }, ["Habakkuk"] = new[] { "Hab" },
            ["Zephaniah"] = new[] { "Zeph", "Zep" }, ["Haggai"] = new[] { "Hag" }, ["Zechariah"] = new[] { "Zech", "Zec" }, ["Malachi"] = new[] { "Mal" },
            ["Matthew"] = new[] { "Matt", "Mt" }, ["Mark"] = new[] { "Mk", "Mrk", "Mar" }, ["Luke"] = new[] { "Lk", "Luk" },
            ["John"] = new[] { "Jn", "Jhn", "Joh" }, ["Acts"] = new[] { "Ac", "Act" }, ["Romans"] = new[] { "Rom", "Rm", "Ro" },
            ["Galatians"] = new[] { "Gal" }, ["Ephesians"] = new[] { "Eph" }, ["Philippians"] = new[] { "Phil", "Php", "Pp" },
            ["Colossians"] = new[] { "Col" }, ["Titus"] = new[] { "Tit" }, ["Philemon"] = new[] { "Phlm", "Philem", "Phm" },
            ["Hebrews"] = new[] { "Heb" }, ["James"] = new[] { "Jas", "Jm" }, ["Jude"] = new[] { "Jud", "Jd" },
            ["Revelation"] = new[] { "Rev", "Revelations", "Rv", "Apocalypse", "Apoc" },
        };
        foreach (var (book, aliases) in extra) foreach (var a in aliases) Add(a, book);
        // Numbered books: 1 Sam / I Samuel / First Samuel / 1st Sam / 1Sa …
        var numbered = new Dictionary<string, string[]>
        {
            ["Samuel"] = new[] { "Sam", "Sa", "Sm" }, ["Kings"] = new[] { "Kgs", "Ki", "Kin" }, ["Chronicles"] = new[] { "Chr", "Chron", "Ch" },
            ["Corinthians"] = new[] { "Cor", "Co" }, ["Thessalonians"] = new[] { "Thess", "Thes", "Th" }, ["Timothy"] = new[] { "Tim", "Ti", "Tm" },
            ["Peter"] = new[] { "Pet", "Pe", "Pt" }, ["John"] = new[] { "Jn", "Jhn", "Joh" }
        };
        var prefixes = new Dictionary<int, string[]>
        {
            [1] = new[] { "1", "I", "First", "1st" }, [2] = new[] { "2", "II", "Second", "2nd" }, [3] = new[] { "3", "III", "Third", "3rd" }
        };
        foreach (var (stem, abbr) in numbered)
            foreach (var (n, pre) in prefixes)
            {
                var book = $"{n} {stem}";
                if (All.All(b => b.Name != book)) continue;
                foreach (var p in pre)
                    foreach (var a in abbr.Append(stem)) Add($"{p} {a}", book);
            }
        return d;
    }

    /// <summary>Finds a book from a name or abbreviation ("Rom", "1 Cor", "Psalm", "song of songs").</summary>
    public static BibleBook? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var k = Key(text);
        if (Lookup.TryGetValue(k, out var i)) return All[i];
        if (k.Length < 3) return null;
        var hits = All.Where(b => Key(b.Name).StartsWith(k)).ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    private static readonly Regex BookAtStart = new(
        @"^\s*((?:[123]|i{1,3}|first|second|third|1st|2nd|3rd)\s*)?([a-z][a-z]*(?:\s+of\s+[a-z]+)?)\.?\s*",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses "Rom 8:28-39; 12:1-2", "John 3:16, 18", "Genesis 1:1-2:3", "Ps 23", "1 Cor 13".
    /// Unknown text is ignored.
    /// </summary>
    public static List<BibleRef> Parse(string? text)
    {
        var result = new List<BibleRef>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        text = text.Replace('–', '-').Replace('—', '-');
        text = Regex.Replace(text, @"(\d)\.(\d)", "$1:$2").Replace('.', ' ');   // "Rom. 8.28" → "Rom  8:28"
        BibleBook? book = null;
        foreach (var segment in text.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var rest = segment.Trim();
            var m = BookAtStart.Match(rest);
            if (m.Success && m.Groups[2].Success)
            {
                var candidate = Find((m.Groups[1].Value + " " + m.Groups[2].Value).Trim());
                // "Song of Songs" style multi-word names
                if (candidate is null && rest.Length > 0)
                {
                    var words = Regex.Match(rest, @"^[123]?\s*[A-Za-z ]+?(?=\s*\d|$)");
                    if (words.Success) candidate = Find(words.Value);
                    if (candidate is not null) { book = candidate; rest = rest[words.Length..]; }
                }
                else if (candidate is not null) { book = candidate; rest = rest[m.Length..]; }
            }
            if (book is null) continue;
            int? lastChapter = null;
            bool lastHadVerse = false;
            foreach (var part in rest.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var r = ParsePart(book, part, lastChapter, lastHadVerse);
                if (r is null) continue;
                result.Add(r);
                lastChapter = r.LastChapter;
                lastHadVerse = r.Verse is not null;
            }
            if (rest.Trim().Length == 0 && book.Chapters == 1) result.Add(new BibleRef(book.Index, 1));
        }
        return result.Distinct().ToList();
    }

    private static BibleRef? ParsePart(BibleBook book, string part, int? contextChapter, bool contextHasVerse)
    {
        part = part.Trim().TrimEnd(':');
        var m = Regex.Match(part, @"^(\d+)(?::(\d+))?(?:\s*-\s*(\d+)(?::(\d+))?)?");
        if (!m.Success) return null;
        int a = int.Parse(m.Groups[1].Value);
        int? b = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : null;
        int? c = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : null;
        int? d = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : null;

        BibleRef r;
        if (book.Chapters == 1 && b is null)
            r = new BibleRef(book.Index, 1, a, null, c);                      // Jude 3-5
        else if (b is null && contextHasVerse && contextChapter is { } cc)
            r = new BibleRef(book.Index, cc, a, null, c);                      // "John 3:16, 18" → 3:18
        else if (b is null)
            r = c is { } endCh ? new BibleRef(book.Index, a, null, endCh) : new BibleRef(book.Index, a); // Rom 8 / Rom 8-9
        else if (d is not null)
            r = new BibleRef(book.Index, a, b, c, d);                          // Gen 1:1-2:3
        else
            r = new BibleRef(book.Index, a, b, null, c);                       // Rom 8:28-39
        if (r.Chapter < 1 || r.Chapter > book.Chapters || (r.EndChapter is { } e && (e < r.Chapter || e > book.Chapters))) return null;
        if (r.EndChapter == r.Chapter) r = r with { EndChapter = null };
        return r;
    }

    private static readonly Regex InText = new(
        @"\b((?:[123]|I{1,3})\s?[A-Z][a-z]+|Song of (?:Solomon|Songs)|[A-Z][a-z]{1,13})\.?\s+(\d{1,3})(?::(\d{1,3})(?:\s*[-–]\s*(\d{1,3})(?::(\d{1,3}))?)?)?\b");

    /// <summary>Finds references mentioned in free text ("see Rom 5:8 and John 3:16").</summary>
    public static List<BibleRef> FindInText(string? text)
    {
        var found = new List<BibleRef>();
        if (string.IsNullOrWhiteSpace(text)) return found;
        foreach (Match m in InText.Matches(text))
        {
            var book = Find(m.Groups[1].Value);
            if (book is null) continue;
            // Words that are also book abbreviations but usually aren't references ("Job 2" is fine; "Mark 3" is ambiguous but kept).
            var parsed = Parse(book.Name + " " + m.Value[m.Groups[1].Length..].Trim().TrimStart('.').Trim());
            found.AddRange(parsed);
        }
        return found.Distinct().ToList();
    }

    private static readonly Regex BibleComLink = new(
        @"bible\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?bible/\d+/([1-3a-z]{3}\.\d+(?:\.[\d,\-+.]+)?)", RegexOptions.IgnoreCase);

    /// <summary>
    /// The passage(s) in something shared from YouVersion or another Bible app: a bible.com link
    /// (https://bible.com/bible/111/jhn.3.16-18.NIV), share text ("John 3:16-18 NIV …") or a typed reference.
    /// </summary>
    public static List<BibleRef> FromShared(string? shared)
    {
        if (string.IsNullOrWhiteSpace(shared)) return new();
        var found = new List<BibleRef>();
        foreach (Match m in BibleComLink.Matches(shared))
        {
            var parts = m.Groups[1].Value.ToUpperInvariant().Split('.', StringSplitOptions.RemoveEmptyEntries);
            var book = All.FirstOrDefault(b => b.Usfm == parts[0]);
            if (book is null || parts.Length < 2 || !int.TryParse(parts[1], out var ch) || ch < 1 || ch > book.Chapters) continue;
            if (parts.Length >= 3 && Regex.IsMatch(parts[2], @"^[\d,\-+]+$"))
            {
                // "16-18", "16,18", "16+17+18" → first to last verse
                var nums = Regex.Matches(parts[2], @"\d+").Select(x => int.Parse(x.Value)).ToList();
                var first = nums.Min(); var last = nums.Max();
                found.Add(new BibleRef(book.Index, ch, first, null, last != first ? last : null));
            }
            else found.Add(new BibleRef(book.Index, ch));
        }
        if (found.Count > 0) return found.Distinct().ToList();
        // No link: the first line of share text is usually the reference ("John 3:16-18 NIV").
        var firstLine = shared.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        firstLine = Regex.Replace(firstLine, @"\b[A-Z]{2,6}\d{0,4}\s*$", "").Trim();   // drop a version suffix (NIV, ESV, NKJV…)
        var parsed = Parse(firstLine);
        return parsed.Count > 0 ? parsed : FindInText(shared);
    }

    /// <summary>Normalises any list of reference strings, dropping what can't be parsed.</summary>
    public static List<string> Normalise(IEnumerable<string> refs) =>
        refs.SelectMany(Parse).Select(r => r.ToString()).Distinct().ToList();
}
