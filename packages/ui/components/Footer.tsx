import { ActionIcon, Group } from "@mantine/core";
import { IconBrandGithub, IconBrandLinkedin } from "@tabler/icons-react";

export default function Footer() {
  return (
    <Group justify="center" align="center" h="100%">
      <ActionIcon
        component="a"
        href="https://github.com/PlusCosmic"
        target="_blank"
        rel="noopener noreferrer"
        variant="subtle"
        color="gray"
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
      >
        <IconBrandLinkedin size={20} color="#0072b1" />
      </ActionIcon>
    </Group>
  );
}
