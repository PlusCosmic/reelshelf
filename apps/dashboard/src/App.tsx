import Home from "./pages/Home";
import WeatherPane from "./components/WeatherPane";
import UserAvatar from "./components/UserAvatar";
import { AppShell, Group, ActionIcon } from "@mantine/core";
import { IconBrandGithub, IconBrandLinkedin } from "@tabler/icons-react";
import ApexLegendsMapsHorizontal from "./components/ApexLegends/ApexLegendsMapsHorizontal.tsx";

function App() {
  return (
    <AppShell withBorder={false} padding="md" header={{ height: 60 }}>
      <AppShell.Header>
        <Group justify="space-between" m="xl">
          <UserAvatar />
          <WeatherPane />
        </Group>
      </AppShell.Header>

      <AppShell.Main>
        <Home />
      </AppShell.Main>
      <ApexLegendsMapsHorizontal />
      <AppShell.Footer>
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
      </AppShell.Footer>
    </AppShell>
  );
}

export default App;
