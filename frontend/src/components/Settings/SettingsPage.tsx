/**
 * Settings Page
 * Route: /settings
 *
 * User preferences and account settings
 */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Badge,
  Card,
  Center,
  Container,
  Divider,
  Group,
  Loader,
  Stack,
  Switch,
  Text,
  Title,
} from "@mantine/core";
import { IconBell, IconBrandDiscord } from "@tabler/icons-react";
import { fetchUserPreferences, updateUserPreferences } from "@/shared";
import { notifications } from "@mantine/notifications";

export function SettingsPage() {
  const queryClient = useQueryClient();

  // Fetch user preferences
  const { data: preferences, isLoading } = useQuery({
    queryKey: ["user-preferences"],
    queryFn: fetchUserPreferences,
  });

  // Update preferences mutation
  const updateMutation = useMutation({
    mutationFn: updateUserPreferences,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["user-preferences"] });
      notifications.show({
        title: "Settings updated",
        message: "Your preferences have been saved",
        color: "green",
      });
    },
    onError: () => {
      notifications.show({
        title: "Error",
        message: "Failed to update settings",
        color: "red",
      });
    },
  });

  const handleToggleDiscordNotifications = (enabled: boolean) => {
    updateMutation.mutate({
      discordNotificationsEnabled: enabled,
    });
  };

  if (isLoading) {
    return (
      <Container size="md" py="xl">
        <Center py="xl">
          <Loader size="lg" />
        </Center>
      </Container>
    );
  }

  return (
    <Container size="md" py="xl">
      {/* Header */}
      <Stack gap="xs" mb="xl">
        <Title order={1}>Settings</Title>
        <Text c="dimmed" size="sm">
          Manage your account preferences and notifications
        </Text>
      </Stack>

      {/* Notifications Section */}
      <Stack gap="lg">
        <Card
          radius="lg"
          p="lg"
          style={{ background: "rgba(255, 255, 255, 0.02)" }}
        >
          <Stack gap="md">
            <Group gap="xs">
              <IconBell size={24} />
              <Title order={3}>Notifications</Title>
            </Group>

            <Divider />

            {/* Discord Notifications */}
            <Card
              p="md"
              radius="md"
              style={{
                background: "rgba(255, 255, 255, 0.02)",
                border: "1px solid rgba(255, 255, 255, 0.05)",
              }}
            >
              <Group justify="space-between" wrap="nowrap">
                <Stack gap="xs" style={{ flex: 1 }}>
                  <Group gap="sm">
                    <IconBrandDiscord
                      size={20}
                      color="var(--mantine-color-violet-4)"
                    />
                    <Text fw={600} size="sm">
                      Discord Notifications
                    </Text>
                    <Badge size="sm" variant="light" color="blue">
                      Playlists
                    </Badge>
                  </Group>
                  <Text size="sm" c="dimmed">
                    Receive Discord DMs when you're added as a collaborator to a
                    playlist
                  </Text>
                  {!preferences?.discordNotificationsEnabled && (
                    <Text size="xs" c="orange" fw={500}>
                      ⚠️ You won't be notified when added to playlists
                    </Text>
                  )}
                </Stack>

                <Switch
                  size="lg"
                  checked={preferences?.discordNotificationsEnabled ?? true}
                  onChange={(event) =>
                    handleToggleDiscordNotifications(
                      event.currentTarget.checked,
                    )
                  }
                  disabled={updateMutation.isPending}
                  styles={{
                    track: {
                      cursor: updateMutation.isPending
                        ? "not-allowed"
                        : "pointer",
                    },
                  }}
                />
              </Group>
            </Card>

            <Text size="xs" c="dimmed">
              Note: Discord notifications are sent only when other users add you
              as a collaborator. You won't receive notifications for your own
              actions.
            </Text>
          </Stack>
        </Card>
      </Stack>
    </Container>
  );
}
