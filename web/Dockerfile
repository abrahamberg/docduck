# syntax=docker/dockerfile:1.7
###############################
# Dependencies stage (cachable)
###############################
FROM node:20-alpine AS deps
WORKDIR /app
# Install build tooling dependencies early (git for some packages, libc if needed)
RUN apk add --no-cache --virtual .build-deps python3 make g++
COPY package*.json ./
ENV CI=true
# Use npm ci when lockfile present for reproducible builds; leverage BuildKit cache (persisted by id)
RUN --mount=type=cache,id=npm-cache,target=/root/.npm \
	if [ -f package-lock.json ]; then npm ci --no-audit --no-fund --prefer-offline; else npm install --no-audit --no-fund --prefer-offline; fi

###############################
# Build stage
###############################
FROM node:20-alpine AS build
WORKDIR /app
ENV NODE_ENV=production
COPY --from=deps /app/node_modules ./node_modules
COPY package.json ./
COPY tsconfig.json index.html ./
COPY src ./src
ARG VITE_API_BASE
ENV VITE_API_BASE=${VITE_API_BASE}
# Build (cache Vite intermediate if possible)
RUN npm run build

###############################
# Runtime stage (minimal)
###############################
FROM nginx:1.27-alpine AS runtime
WORKDIR /usr/share/nginx/html
COPY --from=build /app/dist ./

# Replace default nginx conf with a lean SPA + API proxy config
RUN rm /etc/nginx/conf.d/default.conf && \
	printf 'server {\n  listen 5173;\n  server_name _;\n  root /usr/share/nginx/html;\n  index index.html;\n  # Proxy API requests to the backend service at /api -> http://api:8080\n  location /api/ {\n    proxy_pass http://api:8080/;\n    proxy_set_header Host $host;\n    proxy_set_header X-Real-IP $remote_addr;\n    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;\n    proxy_set_header X-Forwarded-Proto $scheme;\n    proxy_cache_bypass $http_upgrade;\n  }\n  # Serve SPA for all other paths\n  location / {\n    try_files $uri /index.html;\n  }\n  # Static assets long cache (hashed filenames)\n  location ~* \.(?:js|css|woff2?|svg|png|jpg|jpeg|gif|webp)$ {\n    add_header Cache-Control "public, max-age=31536000, immutable";\n    try_files $uri $uri/ /index.html;\n  }\n}\n' > /etc/nginx/conf.d/docduck-web.conf

EXPOSE 5173
CMD ["nginx", "-g", "daemon off;"]
