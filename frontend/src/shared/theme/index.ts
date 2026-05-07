import { createTheme, type MantineColorsTuple } from "@mantine/core";

export const reelshelfColour: MantineColorsTuple = [
  "#e4fff2",
  "#d1fae6",
  "#a6f3cc",
  "#84eeb8",
  "#51e699",
  "#38e38a",
  "#27e182",
  "#15c86f",
  "#00b261",
  "#009a51",
];

export const reelshelfTheme = createTheme({
  primaryColor: "reelshelfColour",
  colors: {
    reelshelfColour: reelshelfColour,
  },
  fontFamily: "Plus Jakarta Sans",
  headings: {
    fontFamily: "Plus Jakarta Sans",
  },
});
