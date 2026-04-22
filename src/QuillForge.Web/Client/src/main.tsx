import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./index.css";
import App from "./App";
import StoryTourPage from "./StoryTourPage";

function resolveRootComponent() {
  const normalizedPath = window.location.pathname.replace(/\/+$/, "") || "/";
  return normalizedPath === "/tour" ? StoryTourPage : App;
}

const RootComponent = resolveRootComponent();

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <RootComponent />
  </StrictMode>,
);
