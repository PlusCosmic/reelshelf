import { Box, Text, ThemeIcon } from "@mantine/core";
import { IconMovie, IconSearch } from "@tabler/icons-react";
import { VideoUpload } from "../VideoUpload";

interface ClipsEmptyStateProps {
  categoryId: string;
  hasFilters: boolean;
}

export function ClipsEmptyState({
  categoryId,
  hasFilters,
}: ClipsEmptyStateProps) {
  return (
    <Box
      style={{
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        padding: "3rem",
      }}
    >
      <Box style={{ position: "relative", marginBottom: "1.5rem" }}>
        <ThemeIcon
          size={100}
          radius="xl"
          variant="gradient"
          gradient={{ from: "cyberBlue", to: "cyberPurple", deg: 135 }}
          style={{
            boxShadow:
              "0 0 40px rgba(0, 212, 255, 0.3), 0 0 80px rgba(168, 85, 247, 0.15)",
          }}
        >
          {hasFilters ? (
            <IconSearch size={50} stroke={1.5} />
          ) : (
            <IconMovie size={50} stroke={1.5} />
          )}
        </ThemeIcon>
        <Box
          style={{
            position: "absolute",
            inset: -10,
            border: "2px solid rgba(0, 212, 255, 0.3)",
            borderRadius: "50%",
            animation: "pulse-ring 2s ease-out infinite",
          }}
        />
      </Box>
      <Text
        size="xl"
        fw={700}
        mb="xs"
        style={{
          letterSpacing: "-0.3px",
          background: "linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)",
          WebkitBackgroundClip: "text",
          WebkitTextFillColor: "transparent",
          backgroundClip: "text",
        }}
      >
        {hasFilters ? "No clips match your filters" : "No clips found"}
      </Text>
      <Text size="sm" c="dimmed" ta="center" maw={400} mb="xl">
        {hasFilters
          ? "Try adjusting your search or filter criteria to find more clips."
          : "Start uploading your Apex Legends clips to build your collection. Your epic moments deserve to be remembered!"}
      </Text>
      {!hasFilters && <VideoUpload categoryId={categoryId} />}
      <style>
        {`
          @keyframes pulse-ring {
            0% { transform: scale(0.9); opacity: 1; }
            100% { transform: scale(1.5); opacity: 0; }
          }
        `}
      </style>
    </Box>
  );
}
