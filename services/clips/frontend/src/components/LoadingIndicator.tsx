import { Center, Loader, Stack, Text } from "@mantine/core";

interface LoadingIndicatorProps {
  message?: string;
}

export function LoadingIndicator({ message = "Loading..." }: LoadingIndicatorProps) {
  return (
    <Center style={{ width: "100%", height: "100%", minHeight: "300px" }}>
      <Stack align="center" gap="md">
        <Loader size="lg" type="dots" />
        <Text c="dimmed" size="sm">
          {message}
        </Text>
      </Stack>
    </Center>
  );
}
