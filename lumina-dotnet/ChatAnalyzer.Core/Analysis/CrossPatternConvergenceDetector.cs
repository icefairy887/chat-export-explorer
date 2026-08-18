namespace ChatAnalyzer.Core.Analysis;

public sealed record ConvergenceMember(
    WithinPatternShift Shift,
    float[] Direction
);

public sealed record ConvergenceGroup(
    IReadOnlyList<ConvergenceMember> Members,
    double AverageSimilarity,
    double Score
);

public sealed class CrossPatternConvergenceDetector
{
    public IReadOnlyList<ConvergenceGroup> Detect(
        IReadOnlyList<WithinPatternShift> shifts,
        IReadOnlyDictionary<string, float[]> embeddings,
        double minimumDirectionSimilarity = 0.25)
    {
        var members = shifts
            .Select(shift => CreateMember(shift, embeddings))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        var groups = new List<ConvergenceGroup>();
        var used = new HashSet<WithinPatternShift>();

        foreach (var seed in members.OrderByDescending(x => x.Shift.Shift))
        {
            if (used.Contains(seed.Shift))
                continue;

            var group = new List<ConvergenceMember> { seed };

            foreach (var candidate in members)
            {
                if (candidate.Shift == seed.Shift ||
                    used.Contains(candidate.Shift))
                    continue;

                var similarity = Cosine(
                    seed.Direction,
                    candidate.Direction);

                if (similarity >= minimumDirectionSimilarity)
                    group.Add(candidate);
            }

            if (group.Count < 2)
                continue;

            var similarities = new List<double>();

            for (var i = 0; i < group.Count; i++)
            {
                for (var j = i + 1; j < group.Count; j++)
                {
                    similarities.Add(
                        Cosine(
                            group[i].Direction,
                            group[j].Direction));
                }
            }

            var average = similarities.Count > 0
                ? similarities.Average()
                : 0;

            var score =
                average *
                group.Count *
                group.Average(x => x.Shift.Shift);

            groups.Add(new ConvergenceGroup(
                group,
                average,
                score));

            foreach (var member in group)
                used.Add(member.Shift);
        }

        return groups
            .OrderByDescending(x => x.Score)
            .Take(20)
            .ToList();
    }

    private static ConvergenceMember? CreateMember(
        WithinPatternShift shift,
        IReadOnlyDictionary<string, float[]> embeddings)
    {
        var members = shift.Pattern.Matches
            .Select(m => m.Exchange)
            .Append(shift.Pattern.Anchor)
            .Where(e =>
                e.StartedAt is not null &&
                embeddings.ContainsKey(e.Id))
            .OrderBy(e => e.StartedAt)
            .ToList();

        if (members.Count < 4)
            return null;

        var split = members.Count / 2;

        var early = Centroid(
            members.Take(split)
                .Select(e => embeddings[e.Id]));

        var late = Centroid(
            members.Skip(split)
                .Select(e => embeddings[e.Id]));

        if (early.Length == 0 || late.Length == 0)
            return null;

        var direction = new float[early.Length];

        for (var i = 0; i < direction.Length; i++)
            direction[i] = late[i] - early[i];

        Normalize(direction);

        return new ConvergenceMember(
            shift,
            direction);
    }

    private static float[] Centroid(
        IEnumerable<float[]> vectors)
    {
        var list = vectors.ToList();

        if (list.Count == 0)
            return [];

        var result = new float[list[0].Length];

        foreach (var vector in list)
            for (var i = 0; i < result.Length; i++)
                result[i] += vector[i];

        for (var i = 0; i < result.Length; i++)
            result[i] /= list.Count;

        return result;
    }

    private static void Normalize(float[] vector)
    {
        var norm = Math.Sqrt(
            vector.Sum(x => x * x));

        if (norm == 0)
            return;

        for (var i = 0; i < vector.Length; i++)
            vector[i] = (float)(vector[i] / norm);
    }

    private static double Cosine(
        float[] a,
        float[] b)
    {
        double dot = 0;
        double aa = 0;
        double bb = 0;

        var length = Math.Min(a.Length, b.Length);

        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            aa += a[i] * a[i];
            bb += b[i] * b[i];
        }

        return aa == 0 || bb == 0
            ? 0
            : dot / (Math.Sqrt(aa) * Math.Sqrt(bb));
    }
}

