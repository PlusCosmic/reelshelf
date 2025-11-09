import Home from "./pages/Home";
import WeatherPane from "./components/WeatherPane";
import { Footer, UserAvatar } from "@repo/ui";
import { Affix, AppShell, Group } from "@mantine/core";
import ApexLegendsMapsHorizontal from "./components/ApexLegends/ApexLegendsMapsHorizontal.tsx";
import { useCurrentUser, useLogout } from "./hooks/queries";

function App() {
  const { data: user, isLoading } = useCurrentUser();
  const logoutMutation = useLogout();

  return (
    <AppShell
      withBorder={false}
      padding="md"
      header={{ height: 60 }}
      footer={{ height: 40 }}
      styles={{
        header: {
          background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
          backdropFilter: 'blur(10px)',
          borderBottom: '1px solid rgba(255, 255, 255, 0.1)',
        },
        footer: {
          background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
          backdropFilter: 'blur(10px)',
          borderTop: '1px solid rgba(255, 255, 255, 0.1)',
        }
      }}
    >
      <AppShell.Header>
        <Group justify="space-between" m={"sm"}>
          <UserAvatar
            hideLogin={false}
            user={user}
            isLoading={isLoading}
            onLogout={() => logoutMutation.mutateAsync()}
          />
        </Group>
      </AppShell.Header>

      <AppShell.Main>
        <Home />
      </AppShell.Main>

      <Affix position={{ bottom: 60, right: 20 }}>
        <WeatherPane />
      </Affix>

      <ApexLegendsMapsHorizontal />
      <AppShell.Footer>
        <Footer />
      </AppShell.Footer>
    </AppShell>
  );
}

export default App;
