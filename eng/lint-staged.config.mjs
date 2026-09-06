export default {
  "{src,tests}/**/*.{cs,csx,csproj,props,targets,xml,config,resx}":
    "dotnet tool run csharpier check",
  "*.{props,targets,slnx}": "dotnet tool run csharpier check",
  "{*.md,docs/**/*.md}": [
    "bun run oxfmt --check",
    "bun run markdownlint-cli2 --config .markdownlint-cli2.jsonc --no-globs",
  ],
  "{*.json,.*.json,.*.jsonc,.config/**/*.json,.vscode/**/*.json,eng/**/*.mjs}":
    "bun run oxfmt --check",
};
