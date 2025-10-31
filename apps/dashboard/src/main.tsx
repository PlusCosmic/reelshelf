import { createRoot } from "react-dom/client";
import { StrictMode } from "react";
import "@mantine/core/styles.css";
import {
  createTheme,
  type MantineColorsTuple,
  MantineProvider,
} from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import App from "./App";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
      staleTime: 0,
    },
  },
});

const nucleusColour: MantineColorsTuple = [
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

const nucleusTheme = createTheme({
  primaryColor: "nucleusColour",
  colors: {
    nucleusColour: nucleusColour,
  },
  fontFamily: "Plus Jakarta Sans",
  headings: {
    fontFamily: "Plus Jakarta Sans",
  },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <MantineProvider theme={nucleusTheme} defaultColorScheme="dark">
      <QueryClientProvider client={queryClient}>
        <App />
      </QueryClientProvider>
    </MantineProvider>
  </StrictMode>,
);
