namespace LifeHub.Domain;

/// <summary>A passage you read (logged by you, the reading plan, the verse of the day or a study).</summary>
[Owned(OwnScope.Personal)]
public sealed class BibleReading : Entity
{
    public DateOnly Date { get; set; }
    /// <summary>Normalised reference, e.g. "Romans 8:28-39".</summary>
    public string Reference { get; set; } = "";
    /// <summary>manual · verse · plan · catch-up · study · api</summary>
    public string Source { get; set; } = "manual";
    public int? Minutes { get; set; }
    public string Notes { get; set; } = "";
}

/// <summary>Time spent in the Bible app (from Home Assistant, an iPhone Shortcut or the API).</summary>
[Owned(OwnScope.Personal)]
public sealed class BibleAppSession : Entity
{
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    /// <summary>ha · shortcut · api</summary>
    public string Source { get; set; } = "ha";
    /// <summary>Phone / sensor it came from.</summary>
    public string Device { get; set; } = "";
    /// <summary>You said you weren't reading (e.g. the app was just left open).</summary>
    public bool Ignored { get; set; }
    /// <summary>A "What did you read?" prompt was sent for this session.</summary>
    public bool Asked { get; set; }

    public double Minutes(DateTime nowUtc) => ((EndUtc ?? nowUtc) - StartUtc).TotalMinutes;
}

/// <summary>The verse of the day for one date, and whether you read it.</summary>
[Owned(OwnScope.Personal)]
public sealed class DailyVerse : Entity
{
    public DateOnly Date { get; set; }
    public string Reference { get; set; } = "";
    /// <summary>USFM id, e.g. JHN.3.16 (for links to the app / bible.com).</summary>
    public string Usfm { get; set; } = "";
    public string Text { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>youversion · manual · plan</summary>
    public string Source { get; set; } = "manual";
    public bool Read { get; set; }
    public DateTime? ReadUtc { get; set; }
    /// <summary>app · manual · catch-up</summary>
    public string ReadHow { get; set; } = "";
    /// <summary>Set when it was moved onto a later day's reading list.</summary>
    public DateOnly? CarriedTo { get; set; }
}

/// <summary>One thing on a day's reading list: plan chapters and catch-ups from missed days.</summary>
[Owned(OwnScope.Personal)]
public sealed class ReadingPlanItem : Entity
{
    public DateOnly Date { get; set; }
    public string Reference { get; set; } = "";
    /// <summary>plan · catch-up · manual</summary>
    public string Kind { get; set; } = "plan";
    public bool Done { get; set; }
    public DateOnly? DoneOn { get; set; }
    /// <summary>The day it was first due (for carried-over items).</summary>
    public DateOnly? OriginalDate { get; set; }
    public int TimesCarried { get; set; }
    public Guid? VerseId { get; set; }
}

/// <summary>A Bible study session with your notes, uploaded proof and AI help kept separate.</summary>
[Owned(OwnScope.Personal)]
public sealed class BibleStudy : Entity
{
    public DateOnly Date { get; set; }
    public string Title { get; set; } = "";
    /// <summary>Passages you studied (normalised references).</summary>
    public List<string> Passages { get; set; } = new();
    public int? Minutes { get; set; }
    /// <summary>Your own notes — LifeHub never changes these.</summary>
    public string Notes { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public List<StudyAttachment> Attachments { get; set; } = new();

    /// <summary>AI: what was read from the uploads (OCR).</summary>
    public string ExtractedText { get; set; } = "";
    /// <summary>AI: summary, key points, themes… of your notes.</summary>
    public StudyBreakdown? Breakdown { get; set; }
    /// <summary>AI: insights from reference sites and your other studies. Shown separately, never merged into Notes.</summary>
    public List<AiInsight> Insights { get; set; } = new();
    /// <summary>none · working · done · failed (+ message in AiError)</summary>
    /// <summary>AI: "Go deeper" study built from StudyLight commentaries. Never merged into Notes.</summary>
    public DeepStudy? Deeper { get; set; }
    /// <summary>What the study is for (see <see cref="StudyTypes"/>): personal, homegroup, preaching, teaching, youth, word, discipleship.</summary>
    public string StudyType { get; set; } = "";
    /// <summary>AI: starter notes made from a verse, a level and a study type. Never merged into Notes.</summary>
    public StudyStarter? Starter { get; set; }
    public string AiStatus { get; set; } = "none";
    public string AiError { get; set; } = "";
    public float[]? Embedding { get; set; }
}

public sealed class StudyAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    /// <summary>File name inside data/bible/files.</summary>
    public string StoredName { get; set; } = "";
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Text read from this file (PDF text layer or AI OCR).</summary>
    public string Text { get; set; } = "";
    /// <summary>pdf-text · ocr · text · none</summary>
    public string TextSource { get; set; } = "none";

    public bool IsImage => ContentType.StartsWith("image/");
    public bool IsPdf => ContentType == "application/pdf" || FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
}

public sealed class StudyBreakdown
{
    public string Summary { get; set; } = "";
    public List<string> KeyPoints { get; set; } = new();
    /// <summary>References the AI found in your notes (normalised).</summary>
    public List<string> Passages { get; set; } = new();
    public List<string> Themes { get; set; } = new();
    public List<string> People { get; set; } = new();
    public List<string> Questions { get; set; } = new();
    public List<string> Applications { get; set; } = new();
    public DateTime GeneratedUtc { get; set; }
    public string Model { get; set; } = "";
}

public sealed class AiInsight
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    /// <summary>connection · expand · context · cross-reference · question · related-study</summary>
    public string Kind { get; set; } = "connection";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>Where it came from: StudyLight, Biblia Graphia, a study title, or "general knowledge".</summary>
    public string Source { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public Guid? RelatedStudyId { get; set; }
    public bool Pinned { get; set; }
    public bool Dismissed { get; set; }
}

/// <summary>A deeper look at the passage: interpretations, context, what it could look like, how to apply it.</summary>
public sealed class DeepStudy
{
    public int Level { get; set; }
    public DateTime GeneratedUtc { get; set; }
    public string Model { get; set; } = "";
    public List<string> Passages { get; set; } = new();
    public string Overview { get; set; } = "";
    /// <summary>Ways the passage can be read at a deeper level (Note = who holds this view).</summary>
    public List<DeepItem> Interpretations { get; set; } = new();
    public string Context { get; set; } = "";
    /// <summary>Title = word, Note = original (Greek/Hebrew + Strong's), Body = meaning.</summary>
    public List<DeepItem> Words { get; set; } = new();
    /// <summary>Title = reference, Body = why it helps.</summary>
    public List<DeepItem> CrossReferences { get; set; } = new();
    public List<string> Themes { get; set; } = new();
    /// <summary>"What this could look like": Title = situation, Body = description.</summary>
    public List<DeepItem> LooksLike { get; set; } = new();
    /// <summary>Title = area of life, Body = practical step.</summary>
    public List<DeepItem> Applications { get; set; } = new();
    public List<string> Questions { get; set; } = new();
    public string Prayer { get; set; } = "";
    /// <summary>Title = site, Body = commentators drawn on, Note = URL.</summary>
    public List<DeepItem> Sources { get; set; } = new();
}

public sealed class DeepItem
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>Starter notes for a study: the main idea, context and sections shaped by what the study is for.</summary>
public sealed class StudyStarter
{
    public string Type { get; set; } = "personal";
    public int Level { get; set; } = 2;
    public string Focus { get; set; } = "";
    public DateTime GeneratedUtc { get; set; }
    public string Model { get; set; } = "";
    public string Passage { get; set; } = "";
    /// <summary>The passage text that was used (from YouVersion), so the notes can be checked against it.</summary>
    public string PassageText { get; set; } = "";
    /// <summary>The main idea in one sentence ("big idea" for preaching).</summary>
    public string BigIdea { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Context { get; set; } = "";
    public List<StarterSection> Sections { get; set; } = new();
    /// <summary>Title = the question, Note = kind (observation, interpretation, application, discussion, reflection).</summary>
    public List<DeepItem> Questions { get; set; } = new();
    public string Prayer { get; set; } = "";
    /// <summary>Title = site, Body = commentators drawn on, Note = URL.</summary>
    public List<DeepItem> Sources { get; set; } = new();
}

public sealed class StarterSection
{
    public string Heading { get; set; } = "";
    /// <summary>Title = point, Body = detail, Note = extra (verse, timing, illustration idea).</summary>
    public List<DeepItem> Points { get; set; } = new();
}

/// <summary>The kinds of study LifeHub can start for you, and what each one's notes contain.</summary>
public static class StudyTypes
{
    public sealed record Kind(string Key, string Name, string Blurb, string Brief, string Sections, string QuestionKinds);

    public static readonly IReadOnlyList<Kind> All = new Kind[]
    {
        new("personal", "Personal study", "Quiet time and devotion",
            "a personal devotional study for one person's quiet time",
            "\"Read and notice\" (what the passage actually says: observations), \"What it shows about God\", \"Reflect and respond\" (personal application)",
            "reflection"),
        new("homegroup", "Home group", "Leading a small group discussion",
            "a home group / small group Bible study that someone will LEAD for a group of adults, about 45–60 minutes of discussion",
            "\"Icebreaker\" (1–2 light openers linked to the theme), \"Read\" (how to read it together, what to listen for), \"Look closer\" (key observations for the leader), \"What it means\" (interpretation notes, tricky bits explained simply), \"Live it out\" (application ideas for the group), \"Leader's notes\" (timings, where discussion may go, sensitive topics, how to close)",
            "observation, interpretation, application"),
        new("preaching", "Preaching", "Sermon preparation",
            "sermon preparation for a preacher: an exegetical start and a sermon outline",
            "\"Exegetical notes\" (verse by verse: what the text says and means; Note = the verse), \"Outline\" (3–4 main points that flow from the text; Body = explanation, Note = illustration idea), \"Introduction ideas\", \"Application\" (for different people in the congregation), \"Conclusion and response\", \"Watch out for\" (common misreadings, hard questions)",
            "discussion"),
        new("teaching", "Teaching", "A Bible class or course",
            "a lesson plan for teaching a Bible class to adults",
            "\"Learning aims\", \"Background\", \"Lesson plan\" (steps; Note = minutes), \"Key truths\", \"Activities\"",
            "observation, interpretation, application"),
        new("youth", "Youth & children", "A talk or session for young people",
            "a session for young people (youth group or older children): simple language, engaging and age-appropriate",
            "\"Opening activity\" (a game or hook linked to the theme), \"The story / explanation\" (in simple words), \"Big truth\" (one memorable sentence), \"Response\" (an activity or challenge)",
            "discussion"),
        new("word", "Word & theme study", "Key words and themes across Scripture",
            "a word and theme study: the passage's key words in the original languages and how its themes run through the Bible",
            "\"Key words\" (Title = English word, Note = Greek/Hebrew with transliteration, Body = meaning and use), \"The theme across Scripture\" (Title = reference, Body = connection), \"What it teaches\" (doctrine in plain words)",
            "reflection"),
        new("discipleship", "One-to-one", "Reading the Bible with someone",
            "a one-to-one discipleship / mentoring conversation reading the passage together",
            "\"Read together\" (how to approach it), \"Conversation starters\", \"What it means for us\", \"Challenge for the week\" (one concrete step), \"Check in next time\"",
            "discussion, application")
    };

    public static Kind Of(string? key) => All.FirstOrDefault(k => k.Key == key) ?? All[0];

    public static string LevelName(int level) => level switch { <= 1 => "Getting started", 2 => "Going further", _ => "In depth" };
    public static string LevelBlurb(int level) => level switch
    {
        <= 1 => "Simple language, the main points, no background needed",
        2 => "More background, some original-language words, fuller notes",
        _ => "Detailed exegesis, Greek/Hebrew, differing views and more cross-references"
    };
}
