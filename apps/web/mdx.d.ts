declare module "*.mdx" {
  import type { MDXContent } from "mdx/types";

  export const frontmatter: Readonly<Record<string, unknown>>;
  const content: MDXContent;
  export default content;
}
