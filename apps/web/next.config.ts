import type { NextConfig } from "next";

const privateNetworkDevOrigins = [
  "10.*.*.*",
  ...Array.from({ length: 16 }, (_, index) => `172.${index + 16}.*.*`),
  "192.168.*.*"
];

const nextConfig: NextConfig = {
  allowedDevOrigins: privateNetworkDevOrigins,
  output: "standalone"
};

export default nextConfig;
