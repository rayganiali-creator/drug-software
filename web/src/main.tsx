import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import "./components/ui/ui.css";
import "./design/base.css";
import "./layouts/shell.css";

const root = document.getElementById("root");
if (!root) throw new Error("#root not found");
createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
