using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Shared;

namespace DemoApi;
public sealed class Telemetry(IHttpClientFactory factory,IConfiguration config)
{
    public async Task<object> Read(CancellationToken ct)
    {
        var mode = config["RUNTIME_MODE"] ?? "Compose";
        var sources = new Dictionary<string,object>();
        var client = factory.CreateClient("services");
        foreach (var name in new[] { "WEBHOOK", "LOAN", "NOTIFICATION", "SIMULATOR" })
        {
            try { using var response = await client.GetAsync(config[name+"_URL"]+"/health/ready",ct); sources[name] = new { status = response.IsSuccessStatusCode ? "Healthy" : "Unavailable",source = "Service readiness endpoint" }; }
            catch (HttpRequestException) { sources[name] = new { status = "Unavailable",source = "Service readiness endpoint" }; }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { sources[name] = new { status = "Unavailable",source = "Service readiness endpoint" }; }
        }
        object? queue = null; JsonNode? cluster = null; JsonNode? requestRate = null; JsonNode? latency = null; JsonNode? acceptanceLatency = null; JsonNode? applicationLatency = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,(config["RABBITMQ_MANAGEMENT_URL"] ?? "http://rabbitmq:15672")+"/api/queues/%2F/lms.payments");
            var uri = new Uri(config["RABBITMQ_URL"]!); request.Headers.Authorization = new("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(uri.UserInfo))));
            using var response = await factory.CreateClient("telemetry").SendAsync(request,ct);
            if (response.IsSuccessStatusCode) { var data = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!; queue = new { ready = data["messages_ready"]?.GetValue<int>(),unacknowledged = data["messages_unacknowledged"]?.GetValue<int>(),consumers = data["consumers"]?.GetValue<int>(),publishRate = data["message_stats"]?["publish_details"]?["rate"]?.GetValue<double>(),deliveryRate = data["message_stats"]?["deliver_get_details"]?["rate"]?.GetValue<double>(),oldestMessageAge = (double?)null,oldestAgeStatus = "Unavailable: RabbitMQ management does not expose message age",source = "RabbitMQ management API" }; }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { }
        if (!string.IsNullOrEmpty(config["PROMETHEUS_URL"]))
        {
            var data = await Task.WhenAll(Query("sum(rate(lms_webhook_accepted_total[1m]))",ct),Query("sum(rate(lms_http_duration_seconds_sum[1m])) / sum(rate(lms_http_requests_total[1m]))",ct),Query("histogram_quantile(0.95,sum by (le)(rate(lms_webhook_acceptance_seconds_bucket[1m])))",ct),Query("histogram_quantile(0.95,sum by (le)(rate(lms_payment_application_seconds_bucket[1m])))",ct));
            requestRate=data[0];latency=data[1];acceptanceLatency=data[2];applicationLatency=data[3];
        }
        if (mode == "Kubernetes") cluster = await Kubernetes(ct);
        return new { runtimeMode = mode,refreshedAt = DateTimeOffset.UtcNow,dependencies = sources,queue,requestRate,httpMeanLatency = latency,acceptanceLatency,applicationLatency,cluster,autoscaling = mode == "Compose" ? "Unavailable in Compose" : cluster is null ? "Kubernetes telemetry unavailable" : "Kubernetes API",grafanaUrl = config["PUBLIC_GRAFANA_URL"],traceUrl = config["PUBLIC_TRACE_URL"],capacityNote = "Docker Desktop pods share one computer's resources." };
    }
    private async Task<JsonNode?> Query(string query,CancellationToken ct)
    {
        try { var node = JsonNode.Parse(await factory.CreateClient("telemetry").GetStringAsync(config["PROMETHEUS_URL"]+"/api/v1/query?query="+Uri.EscapeDataString(query),ct)); return node?["data"]?["result"]?.DeepClone(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }
    private async Task<JsonNode?> Kubernetes(CancellationToken ct)
    {
        var tokenPath = "/var/run/secrets/kubernetes.io/serviceaccount/token"; var caPath = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";
        if (!File.Exists(tokenPath)) return null;
        using var handler = new HttpClientHandler();
        using var ca = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(await File.ReadAllTextAsync(caPath,ct));
        handler.ServerCertificateCustomValidationCallback = (_,cert,_,errors) => {
            if (cert is null || (errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) != 0) return false;
            using var chain = new System.Security.Cryptography.X509Certificates.X509Chain(); chain.ChainPolicy.TrustMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust; chain.ChainPolicy.CustomTrustStore.Add(ca); chain.ChainPolicy.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck; return chain.Build(cert);
        };
        using var client = new HttpClient(handler); client.DefaultRequestHeaders.Authorization = new("Bearer",(await File.ReadAllTextAsync(tokenPath,ct)).Trim());
        try
        {
            var result = new JsonObject();
            foreach (var (name,path) in new[] { ("deployments","/apis/apps/v1/namespaces/lms-demo/deployments"),("hpa","/apis/autoscaling/v2/namespaces/lms-demo/horizontalpodautoscalers"),("keda","/apis/keda.sh/v1alpha1/namespaces/lms-demo/scaledobjects"),("pods","/api/v1/namespaces/lms-demo/pods") })
            {
                using var response = await client.GetAsync("https://kubernetes.default.svc"+path,ct);
                var raw = response.IsSuccessStatusCode ? JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) : null;
                // Only metadata/spec replica targets/status are returned. Pod specs can contain credentials.
                result[name] = raw?["items"] is JsonArray items ? new JsonArray(items.Select(x => (JsonNode)new JsonObject { ["name"] = x?["metadata"]?["name"]?.DeepClone(), ["replicas"] = x?["spec"]?["replicas"]?.DeepClone(), ["status"] = x?["status"]?.DeepClone(), ["metrics"] = name == "hpa" ? x?["spec"]?["metrics"]?.DeepClone() : null }).ToArray()) : null;
            }
            return result;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }
}
