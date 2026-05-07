import { ActionIcon, Group, Text, Divider } from "@mantine/core";
import {
  IconBrandGithub,
  IconBrandLinkedin,
  IconHome,
  IconMovie,
} from "@tabler/icons-react";

export default function Footer() {
  return (
    <Group justify="center" align="center" h="100%">
      <ActionIcon
        component="a"
        href="https://home.pluscosmic.dev"
        target="_blank"
        rel="noopener noreferrer"
        variant="subtle"
        color="gray"
        styles={{
          root: {
            transition: "all 0.2s ease",
            "&:hover": {
              transform: "translateY(-2px)",
              backgroundColor: "rgba(255, 255, 255, 0.05)",
            },
          },
        }}
      >
        <IconHome size={20} color={"var(--mantine-color-nucleusColour-3)"} />
      </ActionIcon>
      <ActionIcon
        component="a"
        href="https://clips.pluscosmic.dev"
        target="_blank"
        rel="noopener noreferrer"
        variant="subtle"
        color="gray"
        styles={{
          root: {
            transition: "all 0.2s ease",
            "&:hover": {
              transform: "translateY(-2px)",
              backgroundColor: "rgba(255, 255, 255, 0.05)",
            },
          },
        }}
      >
        <IconMovie size={20} color={"var(--mantine-color-nucleusColour-3)"} />
      </ActionIcon>
      <Divider orientation="vertical" opacity={0.3} />
      <Text size="xs" c="dimmed" fw={500}>
        Made by PlusCosmic
      </Text>
      <ActionIcon
        component="a"
        href="https://github.com/PlusCosmic"
        target="_blank"
        rel="noopener noreferrer"
        variant="subtle"
        color="gray"
        styles={{
          root: {
            transition: "all 0.2s ease",
            "&:hover": {
              transform: "translateY(-2px)",
              backgroundColor: "rgba(255, 255, 255, 0.05)",
            },
          },
        }}
      >
        <IconBrandGithub size={20} />
      </ActionIcon>
      <ActionIcon
        component="a"
        href="https://www.linkedin.com/in/harry-lovesey-leach-445075195/"
        target="_blank"
        rel="noopener noreferrer"
        variant="subtle"
        color="gray"
        styles={{
          root: {
            transition: "all 0.2s ease",
            "&:hover": {
              transform: "translateY(-2px)",
              backgroundColor: "rgba(255, 255, 255, 0.05)",
            },
          },
        }}
      >
        <IconBrandLinkedin size={20} color="#0072b1" />
      </ActionIcon>
    </Group>
  );
}
