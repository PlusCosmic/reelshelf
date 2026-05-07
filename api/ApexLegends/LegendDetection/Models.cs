using Nucleus.Clips.ApexLegends.Models;

namespace Nucleus.Clips.ApexLegends.LegendDetection;

public class DetectionResult
{
    public required string TaskId { get; set; }
    public required string VideoId { get; set; }
    public required string Status { get; set; } // pending, processing, completed, failed
    public List<CharacterDetection> Detections { get; set; } = new();
    public CharacterDetection? BestOverall { get; set; }
    public List<string> UniqueCharacters { get; set; } = new();
    public int TotalScreenshots { get; set; }
    public int SuccessfulDetections { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }

    public ClipDetectionStatus GetStatus()
    {
        switch (Status)
        {
            case "pending":
                return ClipDetectionStatus.NotStarted;
            case "processing":
                return ClipDetectionStatus.InProgress;
            case "completed":
                return ClipDetectionStatus.Completed;
            case "failed":
                return ClipDetectionStatus.Failed;
            default:
                throw new ArgumentException("Invalid status value");
        }
    }
}

public class CharacterDetection
{
    public required string CharacterName { get; set; }
    public float Confidence { get; set; }
    public int ScreenshotIndex { get; set; }
    public required string ScreenshotUrl { get; set; }
}