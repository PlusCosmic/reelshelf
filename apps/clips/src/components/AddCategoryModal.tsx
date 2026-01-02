import {
  Modal,
  Stack,
  Tabs,
  TextInput,
  Button,
  Loader,
  Paper,
  Group,
  Image,
  Text,
  UnstyledButton,
  Center,
} from "@mantine/core";
import { useState } from "react";
import { useDebouncedValue } from "@mantine/hooks";
import { IconSearch, IconPlus, IconPhoto } from "@tabler/icons-react";
import { useGameSearch, useAddGameFromIgdb, useAddCustomCategory } from "@/hooks/queries";

interface AddCategoryModalProps {
  opened: boolean;
  onClose: () => void;
}

export function AddCategoryModal({ opened, onClose }: AddCategoryModalProps) {
  const [search, setSearch] = useState("");
  const [debouncedSearch] = useDebouncedValue(search, 300);
  const [customName, setCustomName] = useState("");
  const [customCoverUrl, setCustomCoverUrl] = useState("");

  const { data: searchResults, isLoading: isSearching } = useGameSearch(debouncedSearch);

  const addFromIgdbMutation = useAddGameFromIgdb();
  const addCustomMutation = useAddCustomCategory();

  const handleAddFromIgdb = (igdbId: number) => {
    addFromIgdbMutation.mutate(igdbId, {
      onSuccess: () => {
        setSearch("");
        onClose();
      },
    });
  };

  const handleAddCustom = () => {
    addCustomMutation.mutate(
      { name: customName, coverUrl: customCoverUrl || undefined },
      {
        onSuccess: () => {
          setCustomName("");
          setCustomCoverUrl("");
          onClose();
        },
      }
    );
  };

  return (
    <Modal
      opened={opened}
      onClose={onClose}
      title={
        <Text
          fw={600}
          style={{
            background: "linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)",
            WebkitBackgroundClip: "text",
            WebkitTextFillColor: "transparent",
          }}
        >
          Add Category
        </Text>
      }
      size="md"
      radius="lg"
      styles={{
        content: {
          background:
            "linear-gradient(135deg, rgba(15, 15, 25, 0.98) 0%, rgba(20, 20, 35, 0.95) 100%)",
          border: "1px solid rgba(0, 212, 255, 0.2)",
        },
        header: {
          background: "transparent",
          borderBottom: "1px solid rgba(0, 212, 255, 0.1)",
        },
      }}
    >
      <Tabs defaultValue="search">
        <Tabs.List
          style={{
            borderBottom: "1px solid rgba(0, 212, 255, 0.1)",
          }}
        >
          <Tabs.Tab
            value="search"
            style={{
              color: "#94a3b8",
              "&[data-active]": {
                color: "#00d4ff",
                borderColor: "#00d4ff",
              },
            }}
          >
            Search Games
          </Tabs.Tab>
          <Tabs.Tab
            value="custom"
            style={{
              color: "#94a3b8",
              "&[data-active]": {
                color: "#00d4ff",
                borderColor: "#00d4ff",
              },
            }}
          >
            Custom Category
          </Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="search" pt="md">
          <Stack>
            <TextInput
              placeholder="Search for a game..."
              leftSection={<IconSearch size={16} style={{ color: "#00d4ff" }} />}
              rightSection={isSearching ? <Loader size="xs" color="cyan" /> : null}
              value={search}
              onChange={(e) => setSearch(e.currentTarget.value)}
              styles={{
                input: {
                  background: "rgba(15, 15, 25, 0.8)",
                  border: "1px solid rgba(0, 212, 255, 0.2)",
                  color: "#f8fafc",
                  "&:focus": {
                    borderColor: "#00d4ff",
                  },
                },
              }}
            />

            {searchResults && searchResults.length > 0 && (
              <Paper
                p="xs"
                radius="md"
                style={{
                  background: "rgba(15, 15, 25, 0.6)",
                  border: "1px solid rgba(0, 212, 255, 0.1)",
                  maxHeight: "300px",
                  overflowY: "auto",
                }}
              >
                <Stack gap="xs">
                  {searchResults.map((game) => (
                    <UnstyledButton
                      key={game.igdbId}
                      onClick={() => handleAddFromIgdb(game.igdbId)}
                      disabled={addFromIgdbMutation.isPending}
                      style={{
                        padding: "8px",
                        borderRadius: "8px",
                        transition: "all 0.2s ease",
                        "&:hover": {
                          background: "rgba(0, 212, 255, 0.1)",
                        },
                      }}
                    >
                      <Group>
                        {game.coverUrl ? (
                          <Image
                            src={game.coverUrl}
                            w={40}
                            h={53}
                            radius="sm"
                            style={{ flexShrink: 0 }}
                          />
                        ) : (
                          <Center
                            w={40}
                            h={53}
                            style={{
                              background: "rgba(0, 212, 255, 0.1)",
                              borderRadius: "4px",
                              flexShrink: 0,
                            }}
                          >
                            <IconPhoto size={20} style={{ color: "#64748b" }} />
                          </Center>
                        )}
                        <Text size="sm" style={{ color: "#f8fafc", flex: 1 }}>
                          {game.name}
                        </Text>
                        <IconPlus size={16} style={{ color: "#00d4ff" }} />
                      </Group>
                    </UnstyledButton>
                  ))}
                </Stack>
              </Paper>
            )}

            {debouncedSearch.length >= 2 && !isSearching && searchResults?.length === 0 && (
              <Text size="sm" c="dimmed" ta="center">
                No games found. Try a different search or create a custom category.
              </Text>
            )}
          </Stack>
        </Tabs.Panel>

        <Tabs.Panel value="custom" pt="md">
          <Stack>
            <TextInput
              label="Category Name"
              placeholder="e.g., Snowboarding, Hiking"
              value={customName}
              onChange={(e) => setCustomName(e.currentTarget.value)}
              required
              styles={{
                label: { color: "#94a3b8" },
                input: {
                  background: "rgba(15, 15, 25, 0.8)",
                  border: "1px solid rgba(0, 212, 255, 0.2)",
                  color: "#f8fafc",
                  "&:focus": {
                    borderColor: "#00d4ff",
                  },
                },
              }}
            />
            <TextInput
              label="Cover Image URL (optional)"
              placeholder="https://..."
              value={customCoverUrl}
              onChange={(e) => setCustomCoverUrl(e.currentTarget.value)}
              leftSection={<IconPhoto size={16} style={{ color: "#a855f7" }} />}
              styles={{
                label: { color: "#94a3b8" },
                input: {
                  background: "rgba(15, 15, 25, 0.8)",
                  border: "1px solid rgba(168, 85, 247, 0.2)",
                  color: "#f8fafc",
                  "&:focus": {
                    borderColor: "#a855f7",
                  },
                },
              }}
            />
            <Button
              onClick={handleAddCustom}
              loading={addCustomMutation.isPending}
              disabled={!customName.trim()}
              style={{
                background: "linear-gradient(135deg, #00d4ff 0%, #a855f7 100%)",
                border: "none",
                fontWeight: 600,
              }}
            >
              Add Category
            </Button>
          </Stack>
        </Tabs.Panel>
      </Tabs>
    </Modal>
  );
}
