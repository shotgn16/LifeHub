namespace LifeHub.Domain;

/// <summary>A passage you read (logged by you, the reading plan, the verse of the day or a study).</summary>
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
