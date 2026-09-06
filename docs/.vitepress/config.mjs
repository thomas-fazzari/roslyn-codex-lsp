import { defineConfig } from "vitepress";

export default defineConfig({
  title: "Roslyn Codex LSP",
  description: "C# diagnostics, navigation and refactoring in Codex.",
  base: "/roslyn-codex-lsp/",
  lang: "en-US",
  themeConfig: {
    nav: [
      { text: "Get started", link: "/getting-started" },
      { text: "Development", link: "/development" },
    ],
    sidebar: [
      { text: "Overview", link: "/" },
      { text: "Get started", link: "/getting-started" },
      { text: "Usage", link: "/usage" },
      { text: "Development", link: "/development" },
    ],
    search: { provider: "local" },
    outline: [2, 3],
    socialLinks: [{ icon: "github", link: "https://github.com/thomas-fazzari/roslyn-codex-lsp" }],
  },
});
