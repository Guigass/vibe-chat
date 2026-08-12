# VibeChat Web — Angular build + nginx static serving
# Build from repository root: docker build -f apps/web/Dockerfile .
#
# Expects an Angular app under apps/web with `npm run build` producing
# dist/ in browser output (Angular application builder) or dist/<project>/browser.

ARG NODE_IMAGE=node:22.22.3-alpine
ARG NGINX_IMAGE=nginx:1.27.3-alpine

FROM ${NODE_IMAGE} AS build
WORKDIR /app

ARG API_BASE_URL=http://localhost:5080
ARG KEYCLOAK_URL=http://localhost:8080
ARG KEYCLOAK_PUBLIC_URL=
ARG KEYCLOAK_REALM=vibechat
ARG OIDC_WEB_CLIENT_ID=vibechat-web
ARG GRAFANA_URL=http://localhost:3000

ENV API_BASE_URL=${API_BASE_URL} \
    KEYCLOAK_URL=${KEYCLOAK_URL} \
    KEYCLOAK_PUBLIC_URL=${KEYCLOAK_PUBLIC_URL} \
    KEYCLOAK_REALM=${KEYCLOAK_REALM} \
    OIDC_WEB_CLIENT_ID=${OIDC_WEB_CLIENT_ID} \
    GRAFANA_URL=${GRAFANA_URL}

COPY apps/web/package.json apps/web/package-lock.json* apps/web/pnpm-lock.yaml* apps/web/yarn.lock* ./
RUN if [ -f package-lock.json ]; then npm ci; \
    elif [ -f pnpm-lock.yaml ]; then corepack enable && pnpm i --frozen-lockfile; \
    elif [ -f yarn.lock ]; then yarn install --frozen-lockfile; \
    else npm install; fi

COPY apps/web/ ./
RUN npm run build

# Normalize build output into /out (supports common Angular output layouts)
RUN mkdir -p /out \
    && if [ -d dist/browser ]; then cp -R dist/browser/. /out/; \
       elif [ -d dist ]; then \
         DIR="$(find dist -type f -name index.html | head -n1 | xargs dirname)"; \
         cp -R "$DIR"/. /out/; \
       else echo "Angular build output not found under dist/" >&2; exit 1; fi

FROM ${NGINX_IMAGE} AS final
COPY infra/nginx/security-headers.conf /etc/nginx/vibechat/security-headers.conf
COPY apps/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /out/ /usr/share/nginx/html/
EXPOSE 80
HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=6 \
  CMD wget -q -O /dev/null http://127.0.0.1/healthz || exit 1
