import {
  Group,
  Stack,
  Title,
  Center,
  ActionIcon,
  Box,
  useComputedColorScheme,
  useMantineColorScheme,
} from "@mantine/core";
import {
  IconBrandGithub,
  IconBrandLinkedin,
  IconMail,
  IconSun,
  IconMoon,
} from "@tabler/icons-react";
import headshot from "../assets/head-shot.jpeg";

export function ProfilePane() {
  const { setColorScheme } = useMantineColorScheme();
  const computedColorScheme = useComputedColorScheme("dark");

  const toggleColorScheme = () => {
    setColorScheme(computedColorScheme === "dark" ? "light" : "dark");
  };

  return (
    <Center>
      <Group wrap="nowrap">
        <Box className="headshot-frame">
          <img src={headshot} alt="Harry Leach" />
        </Box>
        <Stack gap="xs">
          <Title order={3}>Harry Leach</Title>
          <Title order={5}>Software Engineer</Title>
          <Title order={6}>📍 London</Title>
          <Group gap="xs">
            <ActionIcon
              component="a"
              href="https://github.com/PlusCosmic"
              target="_blank"
              variant="subtle"
              size="lg"
            >
              <IconBrandGithub size={24} />
            </ActionIcon>
            <ActionIcon
              component="a"
              href="https://www.linkedin.com/in/harry-lovesey-leach-445075195/"
              target="_blank"
              variant="subtle"
              size="lg"
            >
              <IconBrandLinkedin size={24} />
            </ActionIcon>
            <ActionIcon
              component="a"
              href="mailto:harryloveseyleach@gmail.com"
              variant="subtle"
              size="lg"
            >
              <IconMail size={24} />
            </ActionIcon>
            <ActionIcon
              onClick={toggleColorScheme}
              variant="subtle"
              size="lg"
              aria-label="Toggle color scheme"
            >
              {computedColorScheme === "dark" ? (
                <IconSun size={24} />
              ) : (
                <IconMoon size={24} />
              )}
            </ActionIcon>
          </Group>
        </Stack>
      </Group>
    </Center>
  );
}
