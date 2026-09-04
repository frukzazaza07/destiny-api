import { readdir, readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const contentRoot = fileURLToPath(new URL("../content/guides/", import.meta.url));
const readinessMode = process.argv.includes("--ready");
const unsupportedArguments = process.argv
  .slice(2)
  .filter((argument) => argument !== "--ready");
const locales = ["en", "th"];
const slugs = [
  "tarot-for-self-reflection",
  "daily-one-card-reading",
  "three-card-tarot-spread",
  "upright-and-reversed-cards",
  "major-and-minor-arcana",
  "responsible-love-readings",
  "responsible-career-readings",
  "responsible-money-readings",
];
const requiredFields = [
  "title",
  "description",
  "slug",
  "locale",
  "translationKey",
  "author",
  "publishedAt",
  "updatedAt",
  "draft",
  "reviewStatus",
  "canonicalPath",
];

function parseScalar(value) {
  if (value === "true") return true;
  if (value === "false") return false;
  if (value === "null") return null;
  if (value.startsWith('"') && value.endsWith('"')) {
    return JSON.parse(value);
  }
  return value;
}

function parseMdx(source, filename) {
  const match = source.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n([\s\S]*)$/);
  if (!match) {
    throw new Error(filename + ": missing or malformed frontmatter.");
  }

  const metadata = {};
  for (const line of match[1].split(/\r?\n/)) {
    const separator = line.indexOf(":");
    if (separator === -1) {
      throw new Error(filename + ": malformed frontmatter line: " + line);
    }
    const key = line.slice(0, separator).trim();
    if (Object.hasOwn(metadata, key)) {
      throw new Error(filename + ": duplicate frontmatter key " + key + ".");
    }
    metadata[key] = parseScalar(line.slice(separator + 1).trim());
  }

  return { metadata, body: match[2] };
}

const errors = [];
const seen = new Set();
const metadataByKey = new Map();

if (unsupportedArguments.length > 0) {
  errors.push("Unsupported argument(s): " + unsupportedArguments.join(", ") + ".");
}

for (const locale of locales) {
  const localeDirectory = new URL("../content/guides/" + locale + "/", import.meta.url);
  const filenames = (await readdir(localeDirectory))
    .filter((filename) => filename.endsWith(".mdx"))
    .sort();

  for (const filename of filenames) {
    const fileUrl = new URL(filename, localeDirectory);
    let parsed;
    try {
      parsed = parseMdx(await readFile(fileUrl, "utf8"), filename);
    } catch (error) {
      errors.push(error instanceof Error ? error.message : String(error));
      continue;
    }

    const { metadata, body } = parsed;
    const key = locale + ":" + metadata.slug;
    seen.add(key);
    metadataByKey.set(key, metadata);

    for (const field of requiredFields) {
      if (!Object.hasOwn(metadata, field)) {
        errors.push(key + ": missing " + field + ".");
      }
    }
    if (metadata.locale !== locale) {
      errors.push(key + ": locale does not match its directory.");
    }
    if (metadata.slug !== filename.replace(/\.mdx$/, "")) {
      errors.push(key + ": slug does not match its filename.");
    }
    if (metadata.translationKey !== metadata.slug) {
      errors.push(key + ": translationKey must match slug.");
    }
    if (metadata.canonicalPath !== "/" + locale + "/guides/" + metadata.slug) {
      errors.push(key + ": canonicalPath is invalid.");
    }
    if (metadata.reviewStatus !== "reviewed" && metadata.draft !== true) {
      errors.push(key + ": unreviewed content must remain a draft.");
    }
    if (metadata.draft === true && metadata.publishedAt !== null) {
      errors.push(key + ": draft content cannot have a publication date.");
    }
    if (metadata.draft === false && metadata.reviewStatus !== "reviewed") {
      errors.push(key + ": published content must be owner-reviewed.");
    }
    if (metadata.draft === false && !/^\d{4}-\d{2}-\d{2}$/.test(metadata.publishedAt)) {
      errors.push(key + ": published content needs a valid publication date.");
    }
    if (!/^\d{4}-\d{2}-\d{2}$/.test(metadata.updatedAt)) {
      errors.push(key + ": updatedAt must use YYYY-MM-DD.");
    }
    if (typeof metadata.title !== "string" || metadata.title.length < 12) {
      errors.push(key + ": title is too short.");
    }
    if (
      typeof metadata.description !== "string" ||
      metadata.description.length < 80
    ) {
      errors.push(key + ": description is too short.");
    }

    const adCount = (body.match(/<GuideAd\s*\/>/g) || []).length;
    if (adCount !== 1) {
      errors.push(key + ": expected exactly one <GuideAd />, found " + adCount + ".");
    }
    const [beforeAd = "", afterAd = ""] = body.split(/<GuideAd\s*\/>/);
    if (beforeAd.length < 1_200 || afterAd.length < 500) {
      errors.push(key + ": the ad must follow substantial content with useful content after it.");
    }
    if ((body.match(/^## /gm) || []).length < 5) {
      errors.push(key + ": expected at least five substantive sections.");
    }
    if (body.length < 3_000) {
      errors.push(key + ": guide body is not substantial enough.");
    }
  }
}

for (const slug of slugs) {
  for (const locale of locales) {
    const key = locale + ":" + slug;
    if (!seen.has(key)) {
      errors.push("Missing guide: " + key + ".");
    }
  }
}

for (const key of seen) {
  const [locale, slug] = key.split(":");
  if (!locales.includes(locale) || !slugs.includes(slug)) {
    errors.push("Unexpected guide: " + key + ".");
  }
}

if (seen.size !== slugs.length * locales.length) {
  errors.push(
    "Expected exactly " +
      slugs.length * locales.length +
      " localized guides, found " +
      seen.size +
      ".",
  );
}

if (readinessMode) {
  let publishedPairCount = 0;

  for (const slug of slugs) {
    let pairIsPublished = true;
    for (const locale of locales) {
      const key = locale + ":" + slug;
      const metadata = metadataByKey.get(key);
      const isPublished =
        metadata?.draft === false &&
        metadata?.reviewStatus === "reviewed" &&
        /^\d{4}-\d{2}-\d{2}$/.test(metadata?.publishedAt);

      if (!isPublished) {
        pairIsPublished = false;
        errors.push(
          key +
            ": not publication-ready; owner review, draft removal, and a publication date are required.",
        );
      }
    }
    if (pairIsPublished) publishedPairCount += 1;
  }

  if (publishedPairCount !== slugs.length) {
    errors.push(
      "Expected exactly " +
        slugs.length +
        " publication-ready bilingual pairs, found " +
        publishedPairCount +
        ".",
    );
  }
}

if (errors.length > 0) {
  const gateName = readinessMode ? "publication readiness" : "structural";
  console.error(
    "Guide content " + gateName + " validation failed:\n- " + errors.join("\n- "),
  );
  process.exitCode = 1;
} else {
  if (readinessMode) {
    console.log(
      "Validated " +
        slugs.length +
        " publication-ready bilingual guide pairs in " +
        contentRoot +
        ".",
    );
  } else {
    console.log(
      "Structurally validated " +
        seen.size +
        " guide files in " +
        contentRoot +
        "; review-blocked drafts remain excluded from publication.",
    );
  }
}
