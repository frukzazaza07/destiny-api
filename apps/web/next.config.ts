import type { NextConfig } from "next";
import createMDX from "@next/mdx";

const privateNetworkDevOrigins = [
  "10.*.*.*",
  ...Array.from({ length: 16 }, (_, index) => `172.${index + 16}.*.*`),
  "192.168.*.*"
];

const nextConfig: NextConfig = {
  allowedDevOrigins: privateNetworkDevOrigins,
  pageExtensions: ["js", "jsx", "md", "mdx", "ts", "tsx"],
  output: "standalone",
  trailingSlash: false,
  skipTrailingSlashRedirect: true
};

const withMDX = createMDX({
  options: {
    remarkPlugins: [
      "remark-frontmatter",
      ["remark-mdx-frontmatter", { name: "frontmatter" }]
    ]
  }
});

export default withMDX(nextConfig);
