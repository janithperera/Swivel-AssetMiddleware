using System.Net.Http.Json;
using AssetMiddleware.Application.Configuration;
using AssetMiddleware.Application.Interfaces;
using AssetMiddleware.Domain.Exceptions;
using AssetMiddleware.Domain.Models.AssetHub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetMiddleware.Infrastructure.Http;

public sealed class AssetHubHttpClient : IAssetHubClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<AssetHubOptions> _options;
    private readonly ILogger<AssetHubHttpClient> _logger;

    public AssetHubHttpClient(
        HttpClient httpClient,
        IOptions<AssetHubOptions> options,
        ILogger<AssetHubHttpClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<int> GetActiveStatusIdAsync(CancellationToken ct)
    {
        var companyId = Uri.EscapeDataString(_options.Value.CompanyId);
        var url = $"v1/companies/{companyId}/asset-statuses";

        var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<AssetStatusResponse>(ct)
            .ConfigureAwait(false);

        var active = result?.Data.FirstOrDefault(s =>
            s.Name.Equals("Active", StringComparison.OrdinalIgnoreCase))
            ?? throw new AssetHubApiException("Active status not found in AssetHub response.");

        _logger.LogInformation("Retrieved Active status ID: {StatusId}", active.Id);
        return active.Id;
    }

    public async Task<AssetSearchResult?> SearchAssetByIdAsync(
        string projectId, string assetId, CancellationToken ct)
    {
        var url = $"v1/projects/{Uri.EscapeDataString(projectId)}/assets" +
                  $"?search={Uri.EscapeDataString(assetId)}";

        var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<AssetSearchResponse>(ct)
            .ConfigureAwait(false);

        // Empty data array → no match → safe to create
        return result?.Data.FirstOrDefault();
    }

    public async Task<CreateAssetResponse> CreateAssetAsync(
        string projectId, CreateAssetRequest request, CancellationToken ct)
    {
        var url = $"v1/projects/{Uri.EscapeDataString(projectId)}/assets";

        var response = await _httpClient.PostAsJsonAsync(url, request, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<CreateAssetResponse>(ct)
            .ConfigureAwait(false)
            ?? throw new AssetHubApiException("Create asset response was null.");
    }

    public async Task<UpdateAssetResponse> UpdateAssetAsync(
        string projectId, string id, UpdateAssetRequest request, CancellationToken ct)
    {
        var url = $"v1/projects/{Uri.EscapeDataString(projectId)}/assets/{Uri.EscapeDataString(id)}";

        var httpRequest = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(request)
        };

        var response = await _httpClient.SendAsync(httpRequest, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<UpdateAssetResponse>(ct)
            .ConfigureAwait(false)
            ?? throw new AssetHubApiException("Update asset response was null.");
    }

    public async Task UploadPhotoAsync(
        string projectId, string id, Stream photoStream, string fileName, CancellationToken ct)
    {
        var url = $"v1/projects/{Uri.EscapeDataString(projectId)}/assets/{Uri.EscapeDataString(id)}/attachments";

        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(photoStream);
        content.Add(streamContent, "file", fileName);

        var response = await _httpClient.PostAsync(url, content, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation(
            "Photo '{FileName}' uploaded for asset {Id} in project {ProjectId}",
            fileName, id, projectId);
    }
}
