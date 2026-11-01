FROM nginx:1.30.6-alpine-slim@sha256:3028fc1419f99b95c1c69f844bbaf49dc1b881750c51b497fc721ac4f02a14e6

ARG VERSION
ARG SOURCE_REVISION
LABEL org.opencontainers.image.title="NFL Blitz original303 browser game" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${SOURCE_REVISION}" \
      io.nflblitz.runtime="portable-csharp-browser-wasm"

COPY nginx.conf /etc/nginx/nginx.conf
COPY bundle-inventory.json /opt/nflblitz/bundle-inventory.json

USER 101:101
WORKDIR /opt/nflblitz
# Keep the large payload last to avoid copying it again for VFS filesystem layers.
COPY AppBundle/ /opt/nflblitz/www/
EXPOSE 8080
STOPSIGNAL SIGQUIT
HEALTHCHECK --interval=10s --timeout=3s --start-period=3s --retries=3 \
  CMD wget -q --spider http://127.0.0.1:8080/build-info.json \
   && wget -q --spider http://127.0.0.1:8080/data/manifest.json \
   && wget -q --spider http://127.0.0.1:8080/_framework/SourcePlayer.Core.wasm || exit 1
ENTRYPOINT ["nginx"]
CMD ["-g", "daemon off;"]
