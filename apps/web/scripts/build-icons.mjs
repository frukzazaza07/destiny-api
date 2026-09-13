import { readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import sharp from "sharp";

// The editable SVG is also the modern browser icon. Raster exports are committed
// so production never needs to generate them. Run from any working directory.
const app = new URL("../app/", import.meta.url);
const publicDir = new URL("../public/", import.meta.url);
const source = await readFile(new URL("icon.svg", app));
const sizes = [16, 32, 48];
const images = await Promise.all(sizes.map((size) =>
  sharp(source).resize(size, size).png().toBuffer()
));
const header = Buffer.alloc(6 + sizes.length * 16);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(sizes.length, 4);
let offset = header.length;
images.forEach((data, index) => {
  const entry = 6 + index * 16;
  header[entry] = sizes[index];
  header[entry + 1] = sizes[index];
  header.writeUInt16LE(1, entry + 4);
  header.writeUInt16LE(32, entry + 6);
  header.writeUInt32LE(data.length, entry + 8);
  header.writeUInt32LE(offset, entry + 12);
  offset += data.length;
});
await writeFile(new URL("favicon.ico", app), Buffer.concat([header, ...images]));
for (const [index, size] of sizes.entries()) {
  await writeFile(new URL(`favicon-${size}x${size}.png`, publicDir), images[index]);
}
await sharp(source).resize(180, 180).flatten({ background: "#101114" })
  .png().toFile(fileURLToPath(new URL("apple-icon.png", app)));
console.log("Exported 16/32/48px PNG and ICO icons, and opaque 180px Apple touch icon.");
