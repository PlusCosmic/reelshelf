namespace Nucleus.Clips.Bunny.Models;

public record BunnyVideo(
    int VideoLibraryId,
    Guid Guid,
    string Title,
    DateTimeOffset DateUploaded,
    int Length,
    int Status,
    double Framerate,
    int ThumbnailCount,
    int EncodeProgress,
    long StorageSize,
    Guid CollectionId,
    string ThumbnailFileName,
    string ThumbnailBlurhash,
    string Category,
    List<Moment> Moments,
    List<MetaTag> MetaTags
);

public record Moment(string Label, int Timestamp);

public record MetaTag(string Property, string Value);
