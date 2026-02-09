import {
  Title,
  Text,
  Badge,
  Group,
  Anchor,
  Stack,
  Box,
  List,
  ActionIcon,
  Divider,
} from "@mantine/core";
import { IconArrowLeft, IconBrandGithub } from "@tabler/icons-react";
import { Link } from "react-router-dom";
import { projects } from "../data/projects";
import { ScreenshotGallery } from "../components/ScreenshotGallery";
import mcPanel1 from "../assets/mc-panel-1.png";
import mcPanel2 from "../assets/mc-panel-2.png";
import mcPanel3 from "../assets/mc-panel-3.png";
import mcPanel4 from "../assets/mc-panel-4.png";
import mcPanel5 from "../assets/mc-panel-5.png";

const screenshots = [mcPanel1, mcPanel2, mcPanel3, mcPanel4, mcPanel5];

const project = projects.find((p) => p.path === "/projects/minecraft-panel")!;

export function MinecraftPanelPage() {
  return (
    <Stack>
      <Anchor component={Link} to="/" size="sm">
        <Group gap={4}>
          <IconArrowLeft size={16} />
          Back
        </Group>
      </Anchor>

      <Title order={3}>{project.title}</Title>
      <Text c="dimmed">{project.subtitle}</Text>

      <Group gap="xs">
        {project.badges.map((badge) => (
          <Badge key={badge} variant="light">
            {badge}
          </Badge>
        ))}
      </Group>

      <Group gap="xs">
        <ActionIcon
          component="a"
          href={project.githubUrl}
          target="_blank"
          variant="subtle"
          size="lg"
        >
          <IconBrandGithub size={20} />
        </ActionIcon>
      </Group>

      <Divider />

      <Box>
        <Title order={5}>Overview</Title>
        <Text mt="sm">{project.overview}</Text>
      </Box>

      <Text size="sm" c="dimmed">
        {project.architecture}
      </Text>

      <Divider />

      <Box>
        <Title order={5}>Key Features</Title>
        <List size="sm" mt="sm" spacing="xs">
          {project.features.map((feature) => (
            <List.Item key={feature}>{feature}</List.Item>
          ))}
        </List>
      </Box>

      <Divider />

      <Box>
        <Title order={5}>Technical Highlights</Title>
        <List size="sm" mt="sm" spacing="xs">
          {project.highlights.map((highlight) => (
            <List.Item key={highlight}>{highlight}</List.Item>
          ))}
        </List>
      </Box>

      <Divider />

      <ScreenshotGallery screenshots={screenshots} />
    </Stack>
  );
}
