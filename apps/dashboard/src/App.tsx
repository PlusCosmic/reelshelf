import Home from "./pages/Home";
import WeatherPane from "./components/WeatherPane";
import { Footer, UserAvatar } from "@repo/ui";
import { AppShell, Group } from "@mantine/core";
import ApexLegendsMapsHorizontal from "./components/ApexLegends/ApexLegendsMapsHorizontal.tsx";

function App() {
  return (
    <AppShell withBorder={false} padding="md" header={{ height: 60 }}>
      <AppShell.Header>
        <Group justify="space-between" m="xl">
          <UserAvatar hideLogin={false} />
          <WeatherPane />
        </Group>
      </AppShell.Header>

      <AppShell.Main>
        <Home />
      </AppShell.Main>
      <ApexLegendsMapsHorizontal />
      <AppShell.Footer>
        <Footer />
      </AppShell.Footer>
    </AppShell>
  );
}

export default App;
