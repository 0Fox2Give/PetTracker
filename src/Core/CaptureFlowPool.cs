namespace PetTracker;

// The active game connection must not lose its reassembler because unrelated
// browser/background traffic fills the bounded capture table.
public sealed class CaptureFlowPool<TKey, TValue>
    where TKey : notnull
{
    readonly Dictionary<TKey, TValue> entries = new();
    readonly int capacity;
    readonly Func<TValue, TimeSpan> lastSeen;
    readonly Func<TKey, TValue, bool> protect;
    public CaptureFlowPool(int capacity, Func<TValue, TimeSpan> lastSeen, Func<TKey, TValue, bool> protect)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
        this.lastSeen = lastSeen;
        this.protect = protect;
    }

    public IReadOnlyDictionary<TKey, TValue> Entries => entries;

    public bool TryGetValue(TKey key, out TValue value) => entries.TryGetValue(key, out value!);
    public bool Remove(TKey key) => entries.Remove(key);
    public bool TryAdd(TKey key, TValue value, out KeyValuePair<TKey, TValue>? evicted)
    {
        evicted = null;
        if (entries.ContainsKey(key))
            throw new InvalidOperationException("Capture flow already exists.");
        if (entries.Count >= capacity)
        {
            var candidates = entries.Where(p => !protect(p.Key, p.Value)).ToArray();
            if (candidates.Length == 0)
                return false;
            var oldest = candidates.MinBy(p => lastSeen(p.Value));
            entries.Remove(oldest.Key);
            evicted = oldest;
        }

        entries.Add(key, value);
        return true;
    }
}
