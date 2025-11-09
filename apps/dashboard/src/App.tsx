import Home from "./pages/Home";
import WeatherPane from "./components/WeatherPane";
import { Footer, UserAvatar } from "@repo/ui";
import { AppShell, Group } from "@mantine/core";
import ApexLegendsMapsHorizontal from "./components/ApexLegends/ApexLegendsMapsHorizontal.tsx";
import { useCurrentUser, useLogout } from "./hooks/queries";

function App() {
  const { data: user, isLoading } = useCurrentUser();
  const logoutMutation = useLogout();

  return (
    <AppShell withBorder={false} padding="md" header={{ height: 60 }}>
      <AppShell.Header>
        <Group justify="space-between" m="xl">
          <UserAvatar
            hideLogin={false}
            user={user}
            isLoading={isLoading}
            onLogout={() => logoutMutation.mutateAsync()}
          />
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
