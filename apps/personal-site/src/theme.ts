import { createTheme } from "@mantine/core";
import type { MantineColorsTuple } from "@mantine/core";

const gold: MantineColorsTuple = [
  "#fdf8ec",
  "#f5ecd1",
  "#ebdba8",
  "#d4af37",
  "#c49a2a",
  "#b38821",
  "#96711a",
  "#785a14",
  "#5a430f",
  "#3c2d0a",
];

const emerald: MantineColorsTuple = [
  "#edf8f1",
  "#d4eddc",
  "#b0dfc2",
  "#50b87a",
  "#3ea366",
  "#328d54",
  "#287345",
  "#1f5936",
  "#174028",
  "#0f281a",
];

const ruby: MantineColorsTuple = [
  "#fceeed",
  "#f6d4d2",
  "#edb3b0",
  "#d4564f",
  "#c43c35",
  "#ac2e28",
  "#8f2521",
  "#721d1a",
  "#551513",
  "#380d0c",
];

const sapphire: MantineColorsTuple = [
  "#eff5fb",
  "#d7e6f4",
  "#b8d4ec",
  "#6fa0d8",
  "#5489c4",
  "#4275ad",
  "#355f8e",
  "#2a4b70",
  "#1f3752",
  "#142434",
];

const cream: MantineColorsTuple = [
  "#fefdfb",
  "#faf8f3",
  "#f7f3eb",
  "#f5f0e8",
  "#e8e0d2",
  "#d4c9b7",
  "#b8aa96",
  "#9c8d77",
  "#7f7159",
  "#63573e",
];

export const theme = createTheme({
  primaryColor: "emerald",
  colors: {
    gold,
    emerald,
    ruby,
    sapphire,
    cream,
    dark: [
      "#f5f0e8", // text/foreground (cream)
      "#d4c9b7", // dimmed text
      "#9c8d77", // muted
      "#5a4f3f", // border
      "#3d342a", // subtle border
      "#1f1b17", // elevated surface
      "#171411", // background (main)
      "#131110", // darker background
      "#0f0d0c", // darkest
      "#0a0908", // deepest
    ],
  },
  primaryShade: 3,
  fontFamily: '"Raleway", sans-serif',
  headings: {
    fontFamily: '"Raleway", sans-serif',
    fontWeight: "600",
  },
  components: {
    Title: {
      styles: {
        root: {
          letterSpacing: "0.08em",
          textTransform: "uppercase" as const,
        },
      },
    },
    Badge: {
      defaultProps: {
        variant: "outline",
        color: "emerald",
      },
    },
    Divider: {},
    ActionIcon: {
      defaultProps: {
        variant: "outline",
        color: "emerald.3",
      },
    },
  },
});
