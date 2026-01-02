import { Box, Image } from "@mantine/core";
import { apiConfig } from "@repo/shared";

interface ApexMetadataProps {
  detectedLegend: number | null | undefined;
  detectedLegendCard: string | null | undefined;
}

/**
 * Displays Apex Legends-specific metadata (detected legend card).
 * Only renders when a valid legend is detected (not "None" which is value 27).
 */
export function ApexMetadata({ detectedLegend, detectedLegendCard }: ApexMetadataProps) {
  // Legend value 27 represents "None" - no legend detected
  if (detectedLegend === 27 || !detectedLegendCard) {
    return null;
  }

  return (
    <Box style={{ flexShrink: 0 }}>
      <Image
        radius="md"
        src={`${apiConfig.baseUrl}${detectedLegendCard}`}
        h={70}
        w={62}
        alt="Detected Legend"
      />
    </Box>
  );
}
