import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

const specs = [
  "services/clips/api/bin/Debug/net10.0/Nucleus.Clips.json",
  "services/minecraft/api/bin/Debug/net10.0/Nucleus.Minecraft.json",
];

const merged = {
  openapi: "3.0.1",
  info: {
    title: "Nucleus API",
    version: "1.0.0",
  },
  paths: {},
  components: {
    schemas: {},
    securitySchemes: {},
  },
  tags: [],
};

for (const specPath of specs) {
  const spec = JSON.parse(await readFile(specPath, "utf8"));
  Object.assign(merged.paths, spec.paths ?? {});
  Object.assign(merged.components.schemas, spec.components?.schemas ?? {});
  Object.assign(merged.components.securitySchemes, spec.components?.securitySchemes ?? {});

  for (const tag of spec.tags ?? []) {
    if (!merged.tags.some((existing) => existing.name === tag.name)) {
      merged.tags.push(tag);
    }
  }
}

await writeFile(
  join("packages", "nucleus-api-client", "Nucleus.json"),
  `${JSON.stringify(merged, null, 2)}\n`,
);
