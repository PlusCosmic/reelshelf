import { createTheme, type MantineColorsTuple, rem } from "@mantine/core";

// Cyberpunk Blue palette
const cyberBlue: MantineColorsTuple = [
  "#e6fcff",
  "#b8f4ff",
  "#85ecff",
  "#52e4ff",
  "#1fdcff",
  "#00d4ff", // Primary
  "#00aacc",
  "#008099",
  "#005566",
  "#002b33",
];

// Cyberpunk Purple palette
const cyberPurple: MantineColorsTuple = [
  "#f5f0ff",
  "#e9dbff",
  "#d4b5ff",
  "#c08fff",
  "#ab69ff",
  "#a855f7", // Primary
  "#8644c6",
  "#653395",
  "#442264",
  "#221133",
];

// Cyberpunk Pink palette
const cyberPink: MantineColorsTuple = [
  "#fff0f7",
  "#ffe0ee",
  "#ffc1dd",
  "#ffa2cc",
  "#ff83bb",
  "#ec4899", // Primary
  "#bd3a7a",
  "#8e2b5c",
  "#5f1d3d",
  "#300e1f",
];

export const minecraftTheme = createTheme({
  primaryColor: "cyberBlue",
  colors: {
    cyberBlue,
    cyberPurple,
    cyberPink,
  },
  fontFamily: "'Plus Jakarta Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif",
  fontFamilyMonospace: "'JetBrains Mono', 'Fira Code', 'Consolas', monospace",
  headings: {
    fontFamily: "'Plus Jakarta Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif",
    fontWeight: "700",
  },
  radius: {
    xs: rem(4),
    sm: rem(6),
    md: rem(10),
    lg: rem(14),
    xl: rem(20),
  },
  defaultRadius: "md",
  shadows: {
    xs: "0 1px 3px rgba(0, 0, 0, 0.4)",
    sm: "0 2px 8px rgba(0, 0, 0, 0.5)",
    md: "0 4px 16px rgba(0, 0, 0, 0.6)",
    lg: "0 8px 32px rgba(0, 0, 0, 0.7)",
    xl: "0 12px 48px rgba(0, 0, 0, 0.8)",
  },
  other: {
    cyberGlow: "0 0 20px rgba(0, 212, 255, 0.5), 0 0 40px rgba(0, 212, 255, 0.3)",
    cyberGradient: "linear-gradient(135deg, #00d4ff 0%, #a855f7 50%, #ec4899 100%)",
  },
  components: {
    Card: {
      styles: () => ({
        root: {
          backgroundColor: "rgba(15, 15, 25, 0.8)",
          borderColor: "rgba(0, 212, 255, 0.2)",
          backdropFilter: "blur(10px)",
        },
      }),
    },
    Paper: {
      styles: () => ({
        root: {
          backgroundColor: "rgba(15, 15, 25, 0.8)",
          borderColor: "rgba(0, 212, 255, 0.2)",
        },
      }),
    },
    Button: {
      styles: () => ({
        root: {
          fontWeight: 600,
          transition: "all 0.3s ease",
        },
      }),
    },
    Badge: {
      styles: () => ({
        root: {
          textTransform: "none" as const,
        },
      }),
    },
    NavLink: {
      styles: () => ({
        root: {
          borderRadius: rem(8),
          transition: "all 0.2s ease",
        },
      }),
    },
  },
});
