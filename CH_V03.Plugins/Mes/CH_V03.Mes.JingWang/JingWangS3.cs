using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CH_V03.Core.Mes;

namespace CH_V03.Mes.JingWang;

internal static class JingWangS3
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false });

    internal static string ContentKey(MesPluginOptions options, string id)
    {
        var prefix = options.Get("S3Prefix").Trim().Trim('/');
        return prefix.Length == 0 ? id : $"{prefix}/{id}";
    }

    internal static string? Validate(MesPluginOptions options)
    {
        if (!Uri.TryCreate(options.Get("S3Endpoint"), UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length != 0
            || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            return "请在 CSV 文件及关联页填写有效的 S3 服务地址";
        foreach (var key in new[] { "S3Bucket", "S3AccessKey", "S3SecretKey" })
            if (string.IsNullOrWhiteSpace(options.Get(key))) return $"CSV 上传缺少配置：{key}";
        var bucket = options.Get("S3Bucket");
        if (bucket.Contains('/') || bucket.Contains('\\') || bucket is "." or "..") return "S3 存储桶名称无效";
        var content = ContentKey(options, new string('0', 32));
        if (content.Length > 255 || content.Contains('\\') || content.Split('/').Any(p => p is "" or "." or ".."))
            return "S3 文件夹无效或超过 IMGCONTENT 的长度限制";
        return null;
    }

    internal static async Task UploadAsync(MesPluginOptions options, string key, string file, CancellationToken ct)
    {
        var bucketAndKey = options.Get("S3Bucket") + "/" + key;
        var uri = new Uri(options.Get("S3Endpoint").TrimEnd('/') + "/" +
            string.Join("/", bucketAndKey.Split('/').Select(Uri.EscapeDataString)));
        using var stream = File.OpenRead(file);
        var payloadHash = Hex(await SHA256.HashDataAsync(stream, ct));
        stream.Position = 0;
        var now = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = now[..8];
        var region = options.Get("S3Region", "us-east-1");
        var scope = $"{date}/{region}/s3/aws4_request";
        const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        var headers = $"host:{uri.Authority}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{now}\n";
        var canonical = $"PUT\n{uri.AbsolutePath}\n\n{headers}\n{signedHeaders}\n{payloadHash}";
        var toSign = $"AWS4-HMAC-SHA256\n{now}\n{scope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}";
        var signingKey = Encoding.UTF8.GetBytes("AWS4" + options.Get("S3SecretKey"));
        foreach (var component in new[] { date, region, "s3", "aws4_request" })
            signingKey = HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(component));
        var signature = Hex(HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(toSign)));
        using var request = new HttpRequestMessage(HttpMethod.Put, uri) { Content = new StreamContent(stream) };
        request.Headers.TryAddWithoutValidation("Authorization", $"AWS4-HMAC-SHA256 Credential={options.Get("S3AccessKey")}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
        request.Headers.Add("x-amz-content-sha256", payloadHash);
        request.Headers.Add("x-amz-date", now);
        request.Content.Headers.ContentType = new("text/csv");
        using var response = await Client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
