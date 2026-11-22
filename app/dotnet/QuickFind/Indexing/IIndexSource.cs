using QuickFind.Core;

namespace QuickFind.Indexing;

/// <summary>
/// Abstraction over how the index is populated. Production implementation: <see cref="HybridIndexSource"/>.
/// </summary>
public interface IIndexSource
{
    void Start(FileIndexStore store, ProgressState progress, CancellationToken ct);
    void Rebuild(FileIndexStore store, ProgressState progress, CancellationToken ct);
}
