// IFeatureCollection 的默认实现
// Kestrel 里对应 DefaultHttpContext.Features（生产版还带 FeatureReferences 缓存）

public class FeatureCollection : IFeatureCollection
{
    private readonly Dictionary<Type, object> _features = new();

    public T? Get<T>() where T : class
        => _features.TryGetValue(typeof(T), out var v) ? (T)v : null;

    public void Set<T>(T feature) where T : class
        => _features[typeof(T)] = feature;

    public bool Has<T>() where T : class
        => _features.ContainsKey(typeof(T));
}
