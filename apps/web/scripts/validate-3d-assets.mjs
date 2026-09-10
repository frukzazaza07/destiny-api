import { readdir, readFile, stat } from "node:fs/promises";
import { join, relative } from "node:path";

const publicRoot = join(process.cwd(), "public");
const modelRoot = join(publicRoot, "models", "destiny-shop");
const mobileBudget = 6 * 1024 * 1024;
const desktopBudget = 10 * 1024 * 1024;
const allowed = new Set([".glb", ".ktx2", ".webp", ".json"]);

async function filesIn(directory) {
  try {
    const entries = await readdir(directory, { withFileTypes: true });
    const nested = await Promise.all(entries.map((entry) => entry.isDirectory()
      ? filesIn(join(directory, entry.name))
      : [join(directory, entry.name)]));
    return nested.flat();
  } catch (error) {
    if (error?.code === "ENOENT") return [];
    throw error;
  }
}

const files = await filesIn(modelRoot);
let initialBytes = 0;
let totalBytes = 0;
const errors = [];
const required = {
  "astrology-advisor.glb": ["idle", "greeting", "listening", "result"],
  "shop.glb": [],
  "advisor.glb": ["idle", "greeting", "seated", "listening", "shuffling", "dealing", "reveal", "result"],
  "visitor.glb": ["idle", "walk"],
  "tarot-back.glb": [],
};
const inspected = new Map();
for (const file of files) {
  const extension = file.slice(file.lastIndexOf(".")).toLowerCase();
  if (!allowed.has(extension)) errors.push(`Unsupported 3D asset format: ${relative(publicRoot, file)}`);
  const bytes = (await stat(file)).size;
  totalBytes += bytes;
  if (relative(modelRoot, file).split(/[\\/]/).includes("initial")) initialBytes += bytes;
  if (extension === ".glb") {
    try {
      const data = await readFile(file);
      if (data.readUInt32LE(0) !== 0x46546c67 || data.readUInt32LE(4) !== 2 || data.readUInt32LE(8) !== bytes) throw Error("Invalid GLB 2.0 header or length");
      if (data.readUInt32LE(16) !== 0x4e4f534a) throw Error("Missing JSON chunk");
      const jsonLength = data.readUInt32LE(12);
      const gltf = JSON.parse(data.subarray(20, 20 + jsonLength).toString("utf8"));
      const binaryStart = 20 + jsonLength;
      if (data.readUInt32LE(binaryStart + 4) !== 0x004e4942 || binaryStart + 8 + data.readUInt32LE(binaryStart) !== bytes) throw Error("Invalid embedded binary chunk");
      if (gltf.asset?.version !== "2.0" || !gltf.meshes?.length) throw Error("Missing glTF meshes");
      if (gltf.buffers?.some(buffer => buffer.uri) || gltf.images?.some(image => image.uri)) throw Error("External model dependencies are not allowed");
      for (const view of gltf.bufferViews ?? []) {
        if ((view.byteOffset ?? 0) + view.byteLength > data.readUInt32LE(binaryStart)) throw Error("Buffer view exceeds binary payload");
      }
      if (!gltf.materials?.every(material => material.pbrMetallicRoughness)) throw Error("Expected shared PBR materials");
      const clips = gltf.animations?.map(clip => clip.name) ?? [];
      let triangles = 0, drawCalls = 0;
      for (const node of gltf.nodes ?? []) if (node.mesh !== undefined) {
        for (const primitive of gltf.meshes[node.mesh].primitives) {
          if ((primitive.mode ?? 4) !== 4) throw Error("Only indexed triangle meshes are supported");
          if (primitive.indices === undefined) throw Error("Mesh must use welded, indexed vertices");
          triangles += gltf.accessors[primitive.indices].count / 3;
          drawCalls++;
        }
      }
      inspected.set(file.split(/[\\/]/).pop(), { bytes, triangles, drawCalls, clips });
    } catch (error) { errors.push(`${relative(publicRoot, file)}: ${error.message}`); }
  }
}

for (const [name, clips] of Object.entries(required)) {
  const asset = inspected.get(name);
  if (!asset) errors.push(`Required model missing or invalid: ${name}`);
  else for (const clip of clips) if (!asset.clips.includes(clip)) errors.push(`${name}: missing animation ${clip}`);
}
try {
  const manifest = JSON.parse(await readFile(join(modelRoot, "initial", "manifest.json"), "utf8"));
  if (manifest.assets.length !== Object.keys(required).length) errors.push("Asset manifest does not match the runtime model set");
  for (const asset of manifest.assets) {
    const actual = inspected.get(asset.file);
    if (!actual || ["bytes", "triangles", "drawCalls"].some(key => asset[key] !== actual[key]) || JSON.stringify(asset.animations) !== JSON.stringify(actual.clips)) errors.push(`Stale model manifest: ${asset.file}`);
  }
} catch (error) { errors.push(`Cannot read model manifest: ${error.message}`); }
// Walking renders all characters and three animated deck packets. Consultation
// hides the visitor and renders at most twelve card boxes plus four controls.
const walkingAssets = ["shop.glb", "advisor.glb", "visitor.glb", "astrology-advisor.glb"].map(name => inspected.get(name));
const walking = walkingAssets.reduce((sum, asset) => ({ triangles: sum.triangles + (asset?.triangles ?? 0), drawCalls: sum.drawCalls + (asset?.drawCalls ?? 0) }), { triangles: 36, drawCalls: 3 });
const consultationAssets = ["shop.glb", "advisor.glb", "astrology-advisor.glb"].map(name => inspected.get(name));
const consultation = consultationAssets.reduce((sum, asset) => ({ triangles: sum.triangles + (asset?.triangles ?? 0), drawCalls: sum.drawCalls + (asset?.drawCalls ?? 0) }), { triangles: 12 * 12 + 4 * 2, drawCalls: 12 + 4 });
const modelTotal = { triangles: Math.max(walking.triangles, consultation.triangles), drawCalls: Math.max(walking.drawCalls, consultation.drawCalls) };
if (modelTotal.triangles > 80_000 || modelTotal.drawCalls > 60) errors.push(`Base scene exceeds 80,000 triangles / 60 draw calls: ${JSON.stringify(modelTotal)}`);

if (initialBytes > mobileBudget) errors.push(`Shared initial 3D payload is ${(initialBytes / 1024 / 1024).toFixed(2)} MB; mobile budget is 6 MB. Move desktop-only detail to a progressive HIGH-quality bundle.`);
if (totalBytes > desktopBudget) errors.push(`Total 3D payload is ${(totalBytes / 1024 / 1024).toFixed(2)} MB; desktop budget is 10 MB.`);
if (errors.length > 0) {
  for (const error of errors) console.error(error);
  process.exitCode = 1;
} else {
  console.log(`Validated ${files.length} 3D asset files; initial payload ${(initialBytes / 1024 / 1024).toFixed(2)} MB (mobile ${mobileBudget / 1024 / 1024} MB, desktop ${desktopBudget / 1024 / 1024} MB budgets).`);
  console.log(`Walking / interactive table scene maximum: ${modelTotal.triangles} triangles, ${modelTotal.drawCalls} base draw calls (shadow passes additional).`);
}
