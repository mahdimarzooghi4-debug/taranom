namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-167: shared, bounded pre-database HTTP ingress on LOCAL,
// CI_EPHEMERAL_ONLY synthetic case commands. Deadline is a TEST guard,
// not clinical SLA, production retry policy or business workflow timeout.
internal static class SyntheticCommandIngress
{
    internal static async Task<byte[]?> ReadAsync(HttpRequest request,int maxBytes)
    {
        if (maxBytes is <2 or >8192 || request.ContentLength is >8192)
            return null;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(
            request.HttpContext.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        var data=new byte[maxBytes+1];
        var count=0;
        while(count<data.Length)
        {
            var n=await request.Body.ReadAsync(data.AsMemory(count),deadline.Token);
            if(n==0)break;
            count+=n;
        }
        // Strictly reject a truncated declared body. A fully received byte
        // sequence is not interpreted until this check has passed.
        if(count is <2 || count>maxBytes ||
           (request.ContentLength is long declared && declared!=count))
            return null;
        return data.AsSpan(0,count).ToArray();
    }
}
