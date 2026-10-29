// Application Pipeline 接口
// Kestrel 里对应的是 IHttpApplication<TContext>.ProcessRequestAsync(TContext context)
// 以及 Microsoft.AspNetCore.Http.RequestDelegate (Func<HttpContext, Task>)

public interface IHttpApplication
{
    /// <summary>
    /// 处理一个 HTTP 请求。Features 里已经填好 RequestFeature 和 ConnectionFeature，
    /// 实现方需要填 ResponseFeature。
    /// </summary>
    Task ProcessRequestAsync(IFeatureCollection features, CancellationToken ct = default);
}
