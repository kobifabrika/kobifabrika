# Tanıtım sitesi imajı (docs/canli-ortam.md → Geçici site sunucusu): Next.js standalone, root olmayan kullanıcı.
# Bu bilgisayarda derlenir, sunucuya `docker save | ssh kf docker load` ile aktarılır; sunucuda derleme yapılmaz.
# NEXT_PUBLIC_* değerleri derlemeye gömülür, bu yüzden build-arg olarak verilir.
FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

FROM node:22-alpine AS build
WORKDIR /app
ARG NEXT_PUBLIC_SITE_ONLY=1
ARG NEXT_PUBLIC_SITE_URL=https://kobifabrika.com
ARG NEXT_PUBLIC_API_URL=https://api.kobifabrika.com
ARG NEXT_PUBLIC_CF_ANALYTICS_TOKEN=
ENV NEXT_PUBLIC_SITE_ONLY=$NEXT_PUBLIC_SITE_ONLY \
    NEXT_PUBLIC_SITE_URL=$NEXT_PUBLIC_SITE_URL \
    NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL \
    NEXT_PUBLIC_CF_ANALYTICS_TOKEN=$NEXT_PUBLIC_CF_ANALYTICS_TOKEN \
    NEXT_OUTPUT=standalone \
    NEXT_TELEMETRY_DISABLED=1
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN npx next build

FROM node:22-alpine AS run
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    HOSTNAME=0.0.0.0 \
    PORT=3000 \
    TZ=Europe/Istanbul
RUN apk add --no-cache tzdata
COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static
COPY --from=build --chown=node:node /app/public ./public
USER node
EXPOSE 3000
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD wget -q -O /dev/null http://127.0.0.1:3000/robots.txt || exit 1
CMD ["node", "server.js"]
