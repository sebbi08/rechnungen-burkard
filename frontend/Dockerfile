# syntax=docker/dockerfile:1

# The frontend as its own deployable image: nginx serves the UI5 build and
# forwards /odata to the backend, which runs as a separate container. The
# combined image in the repository root stays available for single-container
# deployments.
FROM node:22 AS build
ARG VERSION=0.0.0
WORKDIR /app/frontend
COPY frontend/package.json frontend/package-lock.json* ./
RUN npm ci
COPY frontend/ ./
# stamp the released version into the manifest via ui5's replaceVersion task
RUN npm pkg set version="${VERSION}" && npm run build
# The runtime bundle ships debug copies, source maps and test pages that a
# deployment never serves; dropping them keeps the image a fraction of the size.
RUN rm -rf dist/test-resources \
 && find dist/resources -name '*-dbg.js' -delete \
 && find dist/resources -name '*.js.map' -delete \
 && find dist/resources -name '*.less' -delete
# OpenUI5 is Apache-2.0 and is redistributed inside the image, so its license
# travels with it (constitution VI).
RUN cp "$(find /root/.ui5/framework/packages/@openui5/sap.ui.core -maxdepth 2 -name LICENSE.txt | head -1)" \
      dist/OPENUI5-LICENSE.txt

FROM nginx:1.27-alpine AS runtime
ENV BACKEND_URL="http://backend:8080"
COPY --from=build /app/frontend/dist /usr/share/nginx/html
COPY frontend/nginx.conf.template /etc/nginx/templates/default.conf.template
EXPOSE 8080
