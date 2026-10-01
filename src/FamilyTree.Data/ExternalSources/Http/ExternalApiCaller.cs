using System.Net;
using System.Text.Json;

namespace FamilyTree.Data.ExternalSources.Http;

/// <summary>The outcome of an API call: either parsed JSON (which the caller disposes) or a user-facing error.</summary>
internal sealed record ApiCallResult(JsonDocument? Json, string? Error);

internal static class ExternalApiCaller
{
    /// <summary>
    /// GETs JSON from an external API. Expected failures (network, timeout, rate limiting, bad responses) come back as
    /// <see cref="ApiCallResult.Error"/> so a source never throws for them; only caller cancellation propagates.
    /// </summary>
    public static async Task<ApiCallResult> GetJsonAsync(HttpClient client, Uri uri, string sourceName, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(uri, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = response.Headers.RetryAfter?.Delta is { } delta ? $" Try again in about {Math.Ceiling(delta.TotalSeconds)} seconds." : " Try again shortly.";
                return new ApiCallResult(null, $"{sourceName} is limiting requests.{wait}");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ApiCallResult(null, $"{sourceName} returned an error (HTTP {(int)response.StatusCode}).");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return new ApiCallResult(await JsonDocument.ParseAsync(stream, cancellationToken: ct), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ApiCallResult(null, $"{sourceName} did not respond in time.");
        }
        catch (HttpRequestException)
        {
            return new ApiCallResult(null, $"{sourceName} could not be reached.");
        }
        catch (JsonException)
        {
            return new ApiCallResult(null, $"{sourceName} returned a response that couldn't be read.");
        }
    }
}
