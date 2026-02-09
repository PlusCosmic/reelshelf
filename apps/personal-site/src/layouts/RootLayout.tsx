import { Grid } from "@mantine/core";
import { Outlet, ScrollRestoration } from "react-router-dom";
import { ProfilePane } from "../components/ProfilePane";

export function RootLayout() {
  return (
    <>
    <ScrollRestoration />
    <Grid w="100%" align="flex-start">
      <Grid.Col
        span={{ base: 12, md: 6 }}
        className="profile-col"
      >
        <ProfilePane />
      </Grid.Col>
      <Grid.Col span={{ base: 12, md: 6 }}>
        <Outlet />
      </Grid.Col>
    </Grid>
    </>
  );
}
