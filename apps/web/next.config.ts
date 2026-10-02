import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Produces a self-contained server bundle (only the node_modules actually
  // required at runtime) so the production Docker image stays small.
  output: process.env.VERCEL ? undefined : "standalone",
};

export default nextConfig;
