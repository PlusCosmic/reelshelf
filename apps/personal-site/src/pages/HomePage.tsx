import {
  Image,
  Group,
  Stack,
  Text,
  Title,
  Divider,
  Badge,
  Box,
  List,
} from "@mantine/core";
import { Link } from "react-router-dom";
import starling from "../assets/starling-logo.jpeg";
import rockstar from "../assets/rockstar-logo.jpeg";
import w2 from "../assets/w2-logo.jpeg";
import bbs from "../assets/bbs-logo.jpeg";
import bathUni from "../assets/bath-uni-logo.png";
import { projects } from "../data/projects";
import { skillCategories } from "../data/skills";

export function HomePage() {
  return (
    <>
      <Title order={5} className="section-heading">About Me</Title>
      <p>
        I'm a software engineer based in London with about four and a half years
        of experience under my belt. I've worked across the stack over the
        years, though these days I mostly live in the backend.
      </p>
      <p>
        Currently I'm a Software Engineer working at Starling in the Business
        Banking team, where I'm focused on building out the Tap to Pay offering
        and maintaining the accounting platform. Previously I worked in the
        Information Systems team at Rockstar Games.
      </p>
      <p>
        I got into programming because I genuinely love building things, and
        that hasn't changed since my Granddad taught me BASIC when I was 10
        years old. I'm ambitious, always learning, and lately I've been
        particularly drawn to what's happening in AI.
      </p>
      <p>
        When I'm not writing code, you'll probably find me snowboarding or
        playing video games.
      </p>

      <Divider my="xl" />
      <Title order={5} className="section-heading">Skills & Technologies</Title>
      <Stack gap="md" mt="md">
        {skillCategories.map((category) => (
          <Box key={category.category}>
            <Text size="sm" c="dimmed" mb={4}>
              {category.category}
            </Text>
            <Group gap="xs">
              {category.skills.map((skill) => (
                <Badge key={skill} variant="light">
                  {skill}
                </Badge>
              ))}
            </Group>
          </Box>
        ))}
      </Stack>

      <Divider my="xl" />
      <Title order={5} className="section-heading">Experience</Title>
      <Stack gap="lg" mt="md">
        {/* Starling */}
        <Box className="experience-entry">
          <Group justify="space-between" wrap="nowrap">
            <Group gap="sm" wrap="nowrap">
              <Image
                src={starling}
                w={40}
                h={40}
                radius="sm"
                fit="contain"
              />
              <Stack gap={0}>
                <Title order={5}>Starling</Title>
                <Text c="dimmed">Software Engineer</Text>
              </Stack>
            </Group>
            <Text size="sm" c="dimmed">
              September 2024 - Present
            </Text>
          </Group>
          <List size="sm" mt="sm" spacing="xs">
            <List.Item>
              Working across the backend platform in Java and PostgreSQL, as well
              as contributing to the internal management portal built in React
              and TypeScript.
            </List.Item>
            <List.Item>
              Maintaining and supporting refactors of the accounting platform,
              improving reliability and correctness of business toolkit
              operations.
            </List.Item>
            <List.Item>
              Greenfield development of the Tap to Pay offering, integrating with
              a third-party payment service provider to enable small business
              customers to accept payments via the Starling app.
            </List.Item>
            <List.Item>
              Took sole ownership of designing and building the suspension,
              closure, restriction, and offboarding flows end-to-end — including
              data model design, a state machine for navigating multi-step
              processes, and guaranteed-once ledger movements to prevent
              double-execution of payouts.
            </List.Item>
            <List.Item>
              Handled complex edge cases around fraud investigations, account
              closures with positive or negative third-party balances, and
              coordinating money movement between Starling accounts and external
              systems.
            </List.Item>
          </List>
          <Group gap="xs" mt="sm">
            <Badge variant="light">Java</Badge>
            <Badge variant="light">TypeScript</Badge>
            <Badge variant="light">React</Badge>
            <Badge variant="light">PostgreSQL</Badge>
          </Group>
        </Box>

        {/* Rockstar Games */}
        <Box className="experience-entry">
          <Group justify="space-between" wrap="nowrap">
            <Group gap="sm" wrap="nowrap">
              <Image
                src={rockstar}
                w={40}
                h={40}
                radius="sm"
                fit="contain"
              />
              <Stack gap={0}>
                <Title order={5}>Rockstar Games</Title>
                <Text c="dimmed">Information Systems Developer</Text>
              </Stack>
            </Group>
            <Text size="sm" c="dimmed">
              August 2021 - August 2024
            </Text>
          </Group>
          <List size="sm" mt="sm" spacing="xs">
            <List.Item>
              Integral member of the team that built Rockstar's latest internal
              task tracking and project management application, working across
              the full stack — C#/WPF frontend, Spring Boot Java backend,
              database, and RabbitMQ messaging.
            </List.Item>
            <List.Item>
              Built a real-time planning poker tool using C# and RabbitMQ to
              support the team's agile practices.
            </List.Item>
            <List.Item>
              Engaged directly with teams consuming our API to gather
              requirements, and led the implementation of new endpoints to
              support cross-team integrations.
            </List.Item>
            <List.Item>
              Developed an updated game communication layer, enabling tighter
              integration between the application and actively developed titles.
            </List.Item>
            <List.Item>
              Drove performance improvements by reducing unnecessary REST calls
              and profiling memory and CPU usage to identify and resolve
              bottlenecks.
            </List.Item>
          </List>
          <Group gap="xs" mt="sm">
            <Badge variant="light">C#</Badge>
            <Badge variant="light">.NET</Badge>
            <Badge variant="light">WPF</Badge>
            <Badge variant="light">Java</Badge>
            <Badge variant="light">Spring Boot</Badge>
            <Badge variant="light">MySQL</Badge>
            <Badge variant="light">RabbitMQ</Badge>
          </Group>
        </Box>

        {/* W2 Global Data */}
        <Box className="experience-entry">
          <Group justify="space-between" wrap="nowrap">
            <Group gap="sm" wrap="nowrap">
              <Image src={w2} w={40} h={40} radius="sm" fit="contain" />
              <Stack gap={0}>
                <Title order={5}>W2 Global Data</Title>
                <Text c="dimmed">Student Software Engineer</Text>
              </Stack>
            </Group>
            <Text size="sm" c="dimmed">
              June 2019 - September 2019
            </Text>
          </Group>
          <List size="sm" mt="sm" spacing="xs">
            <List.Item>
              Worked in an agile team maintaining deployed software with a focus
              on rapid response to bugs and security vulnerabilities.
            </List.Item>
            <List.Item>
              Collaborated with the Head of Tech to evaluate emerging
              technologies including machine learning applications and REST API
              additions to the core offering.
            </List.Item>
            <List.Item>
              Built a REST-to-SOAP middleware proof of concept, translating
              between API protocols to modernise access to legacy services.
            </List.Item>
            <List.Item>
              Rewrote outdated software libraries to improve reliability and
              maintainability.
            </List.Item>
          </List>
          <Group gap="xs" mt="sm">
            <Badge variant="light">C#</Badge>
            <Badge variant="light">.NET</Badge>
            <Badge variant="light">AngularJS</Badge>
            <Badge variant="light">Azure DevOps</Badge>
            <Badge variant="light">REST</Badge>
            <Badge variant="light">SOAP</Badge>
          </Group>
        </Box>

        {/* Bath Building Society */}
        <Box className="experience-entry">
          <Group justify="space-between" wrap="nowrap">
            <Group gap="sm" wrap="nowrap">
              <Image src={bbs} w={40} h={40} radius="sm" fit="contain" />
              <Stack gap={0}>
                <Title order={5}>Bath Building Society</Title>
                <Text c="dimmed">IT Administrator</Text>
              </Stack>
            </Group>
            <Text size="sm" c="dimmed">
              June 2018 - September 2018
            </Text>
          </Group>
          <List size="sm" mt="sm" spacing="xs">
            <List.Item>
              Provided IT support to colleagues, responding to tickets and
              resolving a range of technical issues.
            </List.Item>
            <List.Item>
              Assisted with infrastructure upgrades including new PC
              installations and the Windows 7 to 10 migration.
            </List.Item>
          </List>
        </Box>
      </Stack>

      <Divider my="xl" />
      <Title order={5} className="section-heading">Education</Title>
      <Stack gap="lg" mt="md">
        <Box className="experience-entry">
          <Group justify="space-between" wrap="nowrap">
            <Group gap="sm" wrap="nowrap">
              <Image
                src={bathUni}
                w={40}
                h={40}
                radius="sm"
                fit="contain"
              />
              <Stack gap={0}>
                <Title order={5}>University of Bath</Title>
                <Text c="dimmed">BSc Computer Science</Text>
              </Stack>
            </Group>
            <Text size="sm" c="dimmed">
              2018 — 2021
            </Text>
          </Group>
          <Text size="sm" mt="xs">
            Second Class, First Division (2:1)
          </Text>
          <Text size="sm" mt="xs">
            Covered a broad range of topics from low-level systems to applied
            research, with a particular interest in the modules below.
          </Text>
          <Text size="sm" mt="xs">
            Dissertation: built a mobile campus navigation app that combined
            outdoor routing via GPS and OpenStreetMap with indoor positioning
            using Dead Reckoning and custom floor maps.
          </Text>
          <Group gap="xs" mt="sm">
            <Badge variant="light">Compilers</Badge>
            <Badge variant="light">Cryptography</Badge>
            <Badge variant="light">Machine Learning</Badge>
            <Badge variant="light">Networking</Badge>
          </Group>
        </Box>
      </Stack>

      <Divider my="xl" />
      <Title order={5} className="section-heading">Projects</Title>
      <Stack gap="lg" mt="md">
        {projects.map((project) => (
          <Box
            key={project.path}
            component={Link}
            to={project.path}
            style={{
              textDecoration: "none",
              color: "inherit",
              padding: "var(--mantine-spacing-sm)",
              borderRadius: "var(--mantine-radius-sm)",
              transition: "background-color 150ms ease",
            }}
            className="project-card"
          >
            <Title order={5}>{project.title}</Title>
            <Text size="sm" c="dimmed">
              {project.subtitle}
            </Text>
            <Text mt="sm">{project.description}</Text>
            <Group gap="xs" mt="sm">
              {project.badges.map((badge) => (
                <Badge key={badge} variant="light">
                  {badge}
                </Badge>
              ))}
            </Group>
          </Box>
        ))}
      </Stack>
    </>
  );
}
