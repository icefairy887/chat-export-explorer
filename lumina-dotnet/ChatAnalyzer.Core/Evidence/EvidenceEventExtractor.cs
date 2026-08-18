using System.Text.RegularExpressions;
using ChatAnalyzer.Core.Embeddings;
using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Evidence;

public sealed class EvidenceEventExtractor
{
    private readonly IEmbeddingService _embeddings;

    private static readonly (EvidenceKind Kind, string Prototype)[] Prototypes =
    [
        (EvidenceKind.Claim, "stating that something is true or describing a belief about reality"),
        (EvidenceKind.Action, "describing something I actually did or am currently doing"),
        (EvidenceKind.Decision, "stating a decision, commitment, choice, or intention that has been made"),
        (EvidenceKind.Preference, "expressing what I want, prefer, like, dislike, or would choose"),
        (EvidenceKind.Outcome, "describing a result, consequence, completion, success, failure, or something that happened after an action")
    ];

    public EvidenceEventExtractor(IEmbeddingService embeddings)
    {
        _embeddings = embeddings;
    }

    public async Task<IReadOnlyList<EvidenceEvent>> ExtractAsync(
        IReadOnlyList<Exchange> exchanges,
        CancellationToken cancellationToken = default)
    {
        var prototypeVectors = await _embeddings.EmbedBatchAsync(
            Prototypes.Select(x => x.Prototype).ToList(),
            cancellationToken);

        var candidates = exchanges
            .Where(e => e.StartedAt is not null)
            .SelectMany(e => Split(e.UserText)
                .Select(text => new Candidate(e, text)))
            .Where(x => x.Text.Length >= 25)
            .Where(x => IsEventCandidate(x.Text))
            .ToList();

        var results = new List<EvidenceEvent>();

        const int batchSize = 128;

        for (var offset = 0; offset < candidates.Count; offset += batchSize)
        {
            var batch = candidates
                .Skip(offset)
                .Take(batchSize)
                .ToList();

            var vectors = await _embeddings.EmbedBatchAsync(
                batch.Select(x => x.Text).ToList(),
                cancellationToken);

            for (var i = 0; i < batch.Count; i++)
            {
                var candidate = batch[i];
                var vector = vectors[i];

                var scores = Prototypes
                    .Select((prototype, index) => new
                    {
                        prototype.Kind,
                        Score = Cosine(vector, prototypeVectors[index])
                    })
                    .OrderByDescending(x => x.Score)
                    .ToList();

                var explicitKind = ClassifyExplicitSelfReport(candidate.Text);
                var best = explicitKind is not null
                    ? scores.First(x => x.Kind == explicitKind.Value)
                    : scores[0];
                var second = scores.Count > 1 ? scores[1].Score : 0;

                // Explicit first-person language is stronger evidence than a
                // close semantic tie between two neighboring event classes.
                if (explicitKind is null)
                {
                    if (best.Score < 0.22)
                        continue;

                    if (best.Score - second < 0.015)
                        continue;
                }

                if (!IsPlausible(best.Kind, candidate.Text))
                    continue;

                if (!IsSelfEvidence(best.Kind, candidate.Text))
                    continue;

                results.Add(new EvidenceEvent(
                    Id: $"{candidate.Exchange.Id}:{offset + i}",
                    ConversationId: candidate.Exchange.ConversationId,
                    ExchangeId: candidate.Exchange.Id,
                    Timestamp: candidate.Exchange.StartedAt,
                    Kind: best.Kind,
                    Text: candidate.Text,
                    Confidence: best.Score,
                    Source: candidate.Exchange));
            }
        }

        return results
            .OrderBy(x => x.Timestamp)
            .ToList();
    }

    private static EvidenceKind? ClassifyExplicitSelfReport(string text)
    {
        var t = text.Trim().ToLowerInvariant();

        if (ContainsAny(t,
            "i finished", "i completed", "i passed", "i failed",
            "i received", "i was accepted", "i was rejected",
            "i got hired", "i got fired", "i got the job"))
            return EvidenceKind.Outcome;

        if (ContainsAny(t,
            "i decided", "i've decided", "i have decided", "i chose",
            "i'm going to", "i am going to", "i will ", "i won't "))
            return EvidenceKind.Decision;

        if (ContainsAny(t,
            "i applied", "i sent", "i called", "i emailed", "i started",
            "i stopped", "i made", "i built", "i created", "i enrolled",
            "i installed", "i submitted", "i registered", "i did "))
            return EvidenceKind.Action;

        if (ContainsAny(t,
            "i want", "i don't want", "i do not want", "i prefer",
            "i'd rather", "i would rather", "i like", "i love", "i hate"))
            return EvidenceKind.Preference;

        if (ContainsAny(t,
            "i think", "i believe", "i know ", "i don't think",
            "i do not think", "i feel like"))
            return EvidenceKind.Claim;

        return null;
    }




    private static bool IsSelfEvidence(
        EvidenceKind kind,
        string text)
    {
        var t = text.Trim().ToLowerInvariant();

        // Reject obvious pasted/quoted third-person boilerplate.
        if (!ContainsAny(t,
            " i ", "i'm", "i am", "i've", "i have",
            "i'll", "i will", "i'd", "i would",
            "my ", "me ", "myself"))
        {
            return false;
        }

        return kind switch
        {
            EvidenceKind.Action =>
                ContainsAny(t,
                    "i applied", "i sent", "i called", "i texted",
                    "i emailed", "i went", "i left", "i started",
                    "i stopped", "i made", "i built", "i created",
                    "i bought", "i paid", "i took", "i did ",
                    "i finished", "i completed", "i signed",
                    "i submitted", "i registered", "i enrolled",
                    "i installed", "i deleted", "i blocked",
                    "i told ", "i asked "),

            EvidenceKind.Decision =>
                ContainsAny(t,
                    "i decided", "i've decided", "i have decided",
                    "i chose", "i've chosen", "i have chosen",
                    "i'm going to", "i am going to", "i'm gonna",
                    "i will ", "i won't ", "i am not going to",
                    "i'm not going to", "i'm done with",
                    "i am done with"),

            EvidenceKind.Preference =>
                ContainsAny(t,
                    "i want", "i don't want", "i do not want",
                    "i like", "i don't like", "i do not like",
                    "i prefer", "i'd rather", "i would rather",
                    "i hate", "i love", "i wish"),

            EvidenceKind.Outcome =>
                ContainsAny(t,
                    "i got ", "i received", "i was accepted",
                    "i was rejected", "i passed", "i failed",
                    "i finished", "i completed", "i got hired",
                    "i got fired", "i got the job", "i didn't get",
                    "i did not get"),

            EvidenceKind.Claim =>
                ContainsAny(t,
                    "i think", "i believe", "i know ",
                    "i don't think", "i do not think",
                    "i feel like", "it seems to me"),

            _ => false
        };
    }
    private static bool IsPlausible(
        EvidenceKind kind,
        string text)
    {
        var t = text.Trim().ToLowerInvariant();

        // Questions, commands to ChatGPT, and response-management
        // are not evidence events about the user's life.
        var interrogativeStarts = new[]
        {
            "what ", "why ", "how ", "when ", "where ", "who ",
            "are ", "is ", "was ", "were ", "do ", "does ", "did ",
            "can ", "could ", "would ", "should ", "will "
        };

        if (t.EndsWith("?") ||
            interrogativeStarts.Any(t.StartsWith))
            return false;

        var commandStarts = new[]
        {
            "tell me ", "show me ", "give me ", "help me ",
            "start ", "now start ", "now tell ", "now give ",
            "all right get ", "alright get ", "do one ",
            "keep me ", "convince me "
        };

        if (commandStarts.Any(t.StartsWith))
            return false;

        if (t.Contains("i'm asking you") ||
            t.Contains("i am asking you") ||
            t.Contains("you haven't said") ||
            t.Contains("you have not said") ||
            t.Contains("you're not doing") ||
            t.Contains("you are not doing"))
            return false;

        return kind switch
        {
            EvidenceKind.Action =>
                ContainsAny(t,
                    "i applied", "i sent", "i called", "i texted",
                    "i emailed", "i went", "i came", "i left",
                    "i started", "i stopped", "i made", "i built",
                    "i created", "i bought", "i paid", "i took",
                    "i did ", "i finished", "i completed",
                    "i signed", "i submitted", "i registered",
                    "i enrolled", "i installed", "i deleted",
                    "i blocked", "i told ", "i asked ",
                    "i'm doing", "i am doing", "i've been doing"),

            EvidenceKind.Decision =>
                ContainsAny(t,
                    "i decided", "i've decided", "i have decided",
                    "i chose", "i've chosen", "i have chosen",
                    "i'm going to", "i am going to",
                    "i'm gonna", "i will ", "i won't ",
                    "i am not going to", "i'm not going to",
                    "i'm done with", "i am done with"),

            EvidenceKind.Preference =>
                ContainsAny(t,
                    "i want", "i don't want", "i do not want",
                    "i like", "i don't like", "i do not like",
                    "i prefer", "i'd rather", "i would rather",
                    "i hate", "i love", "i wish"),

            EvidenceKind.Outcome =>
                ContainsAny(t,
                    "i got ", "i received", "they accepted",
                    "they rejected", "i was accepted",
                    "i was rejected", "i passed", "i failed",
                    "it worked", "it didn't work", "it did not work",
                    "it happened", "ended up", "turned out",
                    "i finished", "i completed",
                    "they approved", "they denied",
                    "i got hired", "i got fired",
                    "i got the job", "i didn't get",
                    "i did not get"),

            EvidenceKind.Claim =>
                ContainsAny(t,
                    "i think", "i believe", "i know ",
                    "i don't think", "i do not think",
                    "i feel like", "it seems",
                    "the thing is", "the fact is",
                    "obviously", "apparently",
                    "he is ", "he's ", "she is ", "she's ",
                    "they are ", "they're ",
                    "it is ", "it's "),

            _ => false
        };
    }

    private static bool ContainsAny(
        string text,
        params string[] values)
    {
        return values.Any(text.Contains);
    }
    private static bool IsEventCandidate(string text)
    {
        var t = text.Trim().ToLowerInvariant();

        if (t.EndsWith("?"))
            return false;

        if (t.StartsWith("what ") ||
            t.StartsWith("why ") ||
            t.StartsWith("how ") ||
            t.StartsWith("when ") ||
            t.StartsWith("where ") ||
            t.StartsWith("who ") ||
            t.StartsWith("tell me ") ||
            t.StartsWith("show me ") ||
            t.StartsWith("give me ") ||
            t.StartsWith("help me ") ||
            t.StartsWith("do ") ||
            t.StartsWith("can you ") ||
            t.StartsWith("could you ") ||
            t.StartsWith("would you "))
            return false;

        return true;
    }
    private static IEnumerable<string> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        foreach (var piece in Regex.Split(
            text,
            @"(?<=[.!?])\s+|\r?\n+"))
        {
            var cleaned = piece.Trim();

            if (cleaned.Length >= 25 && cleaned.Length <= 500)
                yield return cleaned;
        }
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0;
        double aa = 0;
        double bb = 0;

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            dot += a[i] * b[i];
            aa += a[i] * a[i];
            bb += b[i] * b[i];
        }

        return aa == 0 || bb == 0
            ? 0
            : dot / (Math.Sqrt(aa) * Math.Sqrt(bb));
    }

    private sealed record Candidate(
        Exchange Exchange,
        string Text);
}



