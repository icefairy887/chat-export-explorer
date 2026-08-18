namespace ChatAnalyzer.Core.Observations;

public interface IObservationExtractor
{
    Task<IReadOnlyList<ObservationCandidate>> ExtractAsync(
        EvidenceCluster cluster,
        CancellationToken cancellationToken = default);
}
