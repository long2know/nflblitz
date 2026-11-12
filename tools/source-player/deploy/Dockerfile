FROM nginx:1.30.6-alpine-slim@sha256:3028fc1419f99b95c1c69f844bbaf49dc1b881750c51b497fc721ac4f02a14e6

ARG VERSION
ARG SOURCE_REVISION
ARG DEPLOYMENT_SHA256
LABEL org.opencontainers.image.title="NFL Blitz original303 browser game" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${SOURCE_REVISION}" \
      io.nflblitz.runtime="portable-csharp-browser-wasm" \
      io.nflblitz.deployment="runtime-base-url/v1" \
      io.nflblitz.deployment-sha256="${DEPLOYMENT_SHA256}"

COPY nginx.conf /etc/nginx/nginx.conf
COPY bundle-inventory.json /opt/nflblitz/bundle-inventory.json
COPY deployment.json /opt/nflblitz/deployment.json
COPY entrypoint.sh healthcheck.sh runtime-common.sh /opt/nflblitz/deploy/

USER 101:101
WORKDIR /opt/nflblitz
ENV BASE_URL=/
# Keep the large payload last to avoid copying it again for VFS filesystem layers.
COPY AppBundle/ /opt/nflblitz/www/
EXPOSE 8080
STOPSIGNAL SIGQUIT
HEALTHCHECK --interval=10s --timeout=3s --start-period=3s --retries=3 \
  CMD ["/opt/nflblitz/deploy/healthcheck.sh"]
ENTRYPOINT ["/opt/nflblitz/deploy/entrypoint.sh"]
CMD ["-g", "daemon off;"]
