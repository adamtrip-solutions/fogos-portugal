# Web (TanStack Start SSR) — build context is the REPO ROOT (pnpm workspace-aware).
#   docker build -f apps/web/Dockerfile -t fogos-portugal-web .
#
# Build stage installs the workspace slice for `web`, runs `vite build`, then uses
# `pnpm deploy` to produce a pruned production node_modules. The runtime stage carries
# only the built output + that node_modules + the server host, and runs on plain Node.
FROM node:24-slim AS build
RUN npm install -g pnpm@11
ENV CI=1
WORKDIR /repo

# Workspace manifests first for install-layer caching.
COPY pnpm-workspace.yaml pnpm-lock.yaml package.json ./
COPY apps/web/package.json apps/web/package.json
RUN pnpm install --frozen-lockfile --filter web...

# App sources, then build (emits apps/web/dist/{client,server}).
COPY apps/web/ apps/web/
RUN pnpm --filter web run build

# Pruned, self-contained production node_modules for the SSR server.
RUN pnpm --filter=web deploy --prod --legacy /out

FROM node:24-slim AS runtime
ENV NODE_ENV=production
ENV PORT=3000
WORKDIR /app
COPY --from=build /out/node_modules ./node_modules
COPY --from=build /repo/apps/web/dist ./dist
COPY --from=build /repo/apps/web/server.mjs ./server.mjs
COPY --from=build /repo/apps/web/package.json ./package.json
USER node
EXPOSE 3000
CMD ["node", "server.mjs"]
