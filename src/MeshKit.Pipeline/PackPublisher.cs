using System.Net.Http.Headers;

namespace MeshKit.Pipeline;

/// <summary>Pushes a pack zip to a running MeshKit store's <c>POST /api/ingest</c>.</summary>
public sealed class PackPublisher(HttpClient http)
{
    public async Task PublishAsync(string zipPath, Uri ingestUrl, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ingestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(zipPath);
        var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", Path.GetFileName(zipPath));
        request.Content = form;

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Ingest refused the pack: {(int)response.StatusCode} {response.ReasonPhrase} {body}".Trim());
        }
    }
}
