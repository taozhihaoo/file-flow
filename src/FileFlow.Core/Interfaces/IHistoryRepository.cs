using FileFlow.Core.Models;

namespace FileFlow.Core.Interfaces;

/// <summary>Persistence for the operation history.</summary>
public interface IHistoryRepository
{
    Task AddAsync(OperationRecord record, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<OperationRecord> Items, int TotalCount)> GetRecentAsync(int limit, int offset, CancellationToken cancellationToken = default);

    Task<int> CountFailedOperationsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationRecord>> GetByTypeAsync(string operationType, int limit, CancellationToken cancellationToken = default);
}
