import FrequentLinks from "../components/FrequentLinks";
import GoogleSearchBar from "../components/GoogleSearchBar.tsx";
import tempLogo from "../assets/transparent plus.png";
import { Box, Center, Container, Grid, Group, Stack, Text } from "@mantine/core";

export default function Home() {
  return (
    <Box
      style={{
        height: "calc(100vh - 100px)"
      }}
    >
      <Container size="xl" h="100%">
        <Stack justify="center" h="100%" align="stretch" gap="xl">
          {/* Header Section */}
          <Stack align="center" gap="md">
            <Group gap="sm" style={{ marginTop: '2rem' }}>
              <img
                src={tempLogo}
                width={50}
                height={50}
                referrerPolicy="no-referrer"
                alt="Plus Cosmic Logo"
                style={{
                  filter: 'drop-shadow(0 4px 8px rgba(0, 0, 0, 0.2))',
                }}
              />
              <Text
                size="2.5rem"
                fw={700}
                style={{
                  letterSpacing: '-1px',
                  background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.9) 0%, rgba(255, 255, 255, 0.6) 100%)',
                  WebkitBackgroundClip: 'text',
                  WebkitTextFillColor: 'transparent',
                  backgroundClip: 'text',
                }}
              >
                Home
              </Text>
            </Group>
          </Stack>

          {/* Search Bar Section */}
          <Grid>
            <Grid.Col span={{ base: 12, sm: 10, md: 8 }} offset={{ base: 0, sm: 1, md: 2 }}>
              <GoogleSearchBar />
            </Grid.Col>
          </Grid>

          {/* Frequent Links Section */}
          <Grid>
            <Grid.Col span={{ base: 12, sm: 10 }} offset={{ base: 0, sm: 1 }}>
              <Center>
                <FrequentLinks />
              </Center>
            </Grid.Col>
          </Grid>
        </Stack>
      </Container>
    </Box>
  );
}
