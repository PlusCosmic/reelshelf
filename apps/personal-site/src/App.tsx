import { createBrowserRouter, RouterProvider } from "react-router-dom";
import { RootLayout } from "./layouts/RootLayout";
import { HomePage } from "./pages/HomePage";
import { ClipsPage } from "./pages/ClipsPage";
import { MinecraftPanelPage } from "./pages/MinecraftPanelPage";
import { StarBattlesPage } from "./pages/StarBattlesPage";
import "./App.css";

const router = createBrowserRouter([
  {
    element: <RootLayout />,
    children: [
      { path: "/", element: <HomePage /> },
      { path: "/projects/clips", element: <ClipsPage /> },
      { path: "/projects/minecraft-panel", element: <MinecraftPanelPage /> },
      { path: "/projects/star-battles", element: <StarBattlesPage /> },
    ],
  },
]);

function App() {
  return <RouterProvider router={router} />;
}

export default App;
