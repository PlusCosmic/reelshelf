namespace Reelshelf.Core.Models;

/// <summary>
/// One way to play a clip's video. Players try a clip's sources in order and play the first the device can.
/// </summary>
/// <param name="Url">Where the video is.</param>
/// <param name="Type">
/// The full MIME type, with codecs where the format has them (<c>video/mp4; codecs="av01.0.08M.08"</c>), so a
/// player can ask the browser whether it decodes it before loading anything. HLS is
/// <c>application/vnd.apple.mpegurl</c>.
/// </param>
/// <param name="Height">The video's height in pixels, when known; an HLS playlist leaves it to the player.</param>
public record PlaybackSource(string Url, string Type, int? Height = null);
