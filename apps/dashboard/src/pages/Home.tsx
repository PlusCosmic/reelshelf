import FrequentLinks from "../components/FrequentLinks";
import GoogleSearchBar from "../components/GoogleSearchBar.tsx";
import tempLogo from "../assets/transparent plus.png";
import { Center, Grid, Group, Stack, Title } from "@mantine/core";

export default function Home() {
  return (
    <div style={{ height: "calc(100vh - 60px)" }}>
      <Stack justify="center" h="100%" align="stretch">
        <Group justify="center" gap={"xs"}>
          <img
            src={tempLogo}
            width={40}
            height={40}
            referrerPolicy="no-referrer"
            alt=""
          />
          <Title>Dashboard</Title>
        </Group>
        <Grid>
          <Grid.Col span={6} offset={3}>
            <GoogleSearchBar />
          </Grid.Col>
        </Grid>
        <Grid>
          <Grid.Col span={10} offset={1}>
            <Center>
              <FrequentLinks />
            </Center>
          </Grid.Col>
        </Grid>
      </Stack>
    </div>
  );
}
