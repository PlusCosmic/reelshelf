namespace Reelshelf.ApexLegends.Models;

public record ApexClipDetection(Guid ClipId, ClipDetectionStatus Status, ApexLegend PrimaryDetection, ApexLegend SecondaryDetection)
{
}