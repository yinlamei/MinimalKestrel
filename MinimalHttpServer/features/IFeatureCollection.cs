// 图一 第 3 层 → 第 4 层的唯一契约：IFeatureCollection（接口字典）
// Kestrel 里对应 Microsoft.AspNetCore.Http.Features.IFeatureCollection
// 本质就是 Dictionary<Type, object> 的类型安全包装

public interface IFeatureCollection
{
    /// <summary>获取某个 Feature，不存在返回 null</summary>
    T? Get<T>() where T : class;

    /// <summary>设置某个 Feature（或覆盖已有）</summary>
    void Set<T>(T feature) where T : class;

    /// <summary>是否有某个 Feature</summary>
    bool Has<T>() where T : class;
}
