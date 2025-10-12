# Build stage
FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json* ./
RUN npm install --no-audit --no-fund
COPY tsconfig.json index.html ./
COPY src ./src
ARG VITE_API_BASE
ENV VITE_API_BASE=${VITE_API_BASE}
RUN npm run build

# Runtime stage
FROM nginx:1.27-alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
# SPA nginx config replacing default
RUN rm /etc/nginx/conf.d/default.conf && \
	printf 'server {\n  listen 5173;\n  server_name _;\n  root /usr/share/nginx/html;\n  index index.html;\n  # Proxy API requests to the backend service at /api -> http://api:8080\n  location /api/ {\n    proxy_pass http://api:8080/;\n    proxy_set_header Host $host;\n    proxy_set_header X-Real-IP $remote_addr;\n    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;\n    proxy_set_header X-Forwarded-Proto $scheme;\n  }\n  # Serve SPA for all other paths\n  location / {\n    try_files $uri /index.html;\n  }\n}\n' > /etc/nginx/conf.d/docduck-web.conf

EXPOSE 5173
CMD ["nginx", "-g", "daemon off;"]
