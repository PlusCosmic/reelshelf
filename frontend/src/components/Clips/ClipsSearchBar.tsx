import { TextInput } from "@mantine/core";
import { IconSearch } from "@tabler/icons-react";

interface ClipsSearchBarProps {
  searchQuery: string;
  onSearchChange: (query: string) => void;
}

export function ClipsSearchBar({
  searchQuery,
  onSearchChange,
}: ClipsSearchBarProps) {
  return (
    <TextInput
      size="lg"
      radius="xl"
      aria-label="Search clips"
      placeholder="Search clips by title, tags, or description..."
      leftSection={<IconSearch size={20} style={{ color: "#00d4ff" }} />}
      value={searchQuery}
      onChange={(e) => onSearchChange(e.target.value)}
      styles={{
        input: {
          paddingLeft: "2.5rem",
          fontSize: "0.95rem",
          fontWeight: 500,
          backgroundColor: "rgba(0, 212, 255, 0.03)",
          border: "1px solid rgba(0, 212, 255, 0.15)",
          transition: "all 0.2s ease",
          "&:focus": {
            backgroundColor: "rgba(0, 212, 255, 0.05)",
            border: "1px solid rgba(0, 212, 255, 0.4)",
            boxShadow: "0 0 20px rgba(0, 212, 255, 0.15)",
          },
          "&::placeholder": {
            color: "rgba(255, 255, 255, 0.4)",
          },
        },
      }}
    />
  );
}
