using Reelshelf.Uploader.ApiClient;
using Reelshelf.Uploader.ApiClient.Models;
using Reelshelf.Uploader.Core.Matching;

namespace Reelshelf.Uploader.Core.Api;

/// <summary>The calls the uploader makes, over the generated client (<see cref="ReelshelfApi"/>).</summary>
public sealed class UploaderApi(ReelshelfApi api)
{
    public ReelshelfClientHandler Handler => api.Handler;

    public Task<ApiResult<DeviceAuthorizationResponse>> AuthorizeAsync(string deviceName, CancellationToken cancellationToken) =>
        api.SendAsync(
            api.Client.Api.Devices.Authorize.ToPostRequestInformation(new DeviceAuthorizeRequest { DeviceName = deviceName }),
            DeviceAuthorizationResponse.CreateFromDiscriminatorValue, cancellationToken);

    public Task<ApiResult<DeviceTokenResponse>> PollTokenAsync(string deviceCode, CancellationToken cancellationToken) =>
        api.SendAsync(
            api.Client.Api.Devices.Token.ToPostRequestInformation(new DeviceTokenRequest { DeviceCode = deviceCode }),
            DeviceTokenResponse.CreateFromDiscriminatorValue, cancellationToken);

    /// <summary>Revokes this device's own token (S5's Sign out).</summary>
    public Task<ApiResult<object>> SignOutAsync(CancellationToken cancellationToken) =>
        api.SendAsync(api.Client.Api.Devices.Current.ToDeleteRequestInformation(), cancellationToken);

    public async Task<ApiResult<List<Book>>> GetBooksAsync(CancellationToken cancellationToken)
    {
        ApiResult<List<GameCategoryResponse>> result = await api.SendCollectionAsync(
            api.Client.Api.Games.Categories.ToGetRequestInformation(), GameCategoryResponse.CreateFromDiscriminatorValue,
            cancellationToken);
        List<Book>? books = result.Value?
            .Where(category => category.Id is not null && !string.IsNullOrEmpty(category.Name))
            .Select(category => new Book(category.Id!.Value, category.Name!, category.Slug, category.ClothColor))
            .ToList();
        return new ApiResult<List<Book>>(result.Status, books, result.Title, result.Detail, result.Error);
    }

    public Task<ApiResult<AccountPlanResponse>> GetPlanAsync(CancellationToken cancellationToken) =>
        api.SendAsync(api.Client.Api.Me.Plan.ToGetRequestInformation(),
            AccountPlanResponse.CreateFromDiscriminatorValue, cancellationToken);

    /// <summary>Reserves a clip for a file (<c>CreateVideo</c>) and answers where its upload goes.</summary>
    public Task<ApiResult<CreateClipResponse>> PrepareAsync(Guid categoryId, string title, long fileSize,
        DateTimeOffset createdAt, string md5, CancellationToken cancellationToken) =>
        api.SendAsync(
            api.Client.Api.Clips.Categories[categoryId].Videos.ToPostRequestInformation(configuration =>
            {
                configuration.QueryParameters.VideoTitle = title;
                configuration.QueryParameters.FileSize = fileSize;
                configuration.QueryParameters.CreatedAt = createdAt;
                configuration.QueryParameters.Md5Hash = md5;
            }),
            CreateClipResponse.CreateFromDiscriminatorValue, cancellationToken);

    /// <summary>Deletes a clip still waiting for its upload: how an upload that can't go on is given up.</summary>
    public Task<ApiResult<object>> DeleteClipAsync(Guid clipId, CancellationToken cancellationToken) =>
        api.SendAsync(api.Client.Api.Clips.Videos[clipId].ToDeleteRequestInformation(), cancellationToken);

    public Task<ApiResult<PresignedUploadParts>> PresignPartsAsync(Guid clipId, string uploadId,
        IReadOnlyList<int> partNumbers, CancellationToken cancellationToken) =>
        api.SendAsync(
            api.Client.Api.Clips.Videos[clipId].Upload.Parts.ToPostRequestInformation(new PresignUploadPartsRequest
            {
                UploadId = uploadId,
                PartNumbers = partNumbers.Select(number => (int?)number).ToList()
            }),
            PresignedUploadParts.CreateFromDiscriminatorValue, cancellationToken);

    public Task<ApiResult<UploadedParts>> ListPartsAsync(Guid clipId, string uploadId,
        CancellationToken cancellationToken) =>
        api.SendAsync(
            api.Client.Api.Clips.Videos[clipId].Upload.Parts.ToGetRequestInformation(configuration =>
                configuration.QueryParameters.UploadId = uploadId),
            UploadedParts.CreateFromDiscriminatorValue, cancellationToken);

    public Task<ApiResult<CompletedUpload>> CompleteAsync(Guid clipId, string uploadId,
        IReadOnlyDictionary<int, string> parts, CancellationToken cancellationToken) =>
        api.SendAsync(
            api.Client.Api.Clips.Videos[clipId].Upload.Complete.ToPostRequestInformation(new CompleteUploadRequest
            {
                UploadId = uploadId,
                Parts = parts.OrderBy(part => part.Key)
                    .Select(part => new CompletedUploadPart { PartNumber = part.Key, Etag = part.Value })
                    .ToList()
            }),
            CompletedUpload.CreateFromDiscriminatorValue, cancellationToken);
}
