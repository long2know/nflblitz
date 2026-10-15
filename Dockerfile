FROM nginx:1.30.6-alpine-slim@sha256:3028fc1419f99b95c1c69f844bbaf49dc1b881750c51b497fc721ac4f02a14e6

RUN rm -f /usr/share/nginx/html/index.html /usr/share/nginx/html/50x.html
COPY hosting/nginx.conf /etc/nginx/nginx.conf
COPY --chmod=755 scripts/verify-webgl.sh /usr/local/bin/verify-webgl
COPY --chmod=755 hosting/entrypoint.sh /usr/local/bin/serve-webgl
COPY --chown=101:101 Build/WebGL/ /usr/share/nginx/html/

# This opt-in exists only for the clearly labeled, disposable hosting smoke test.
ARG WEBGL_ALLOW_TEST_STUB=0
RUN WEBGL_ALLOW_TEST_STUB="$WEBGL_ALLOW_TEST_STUB" /usr/local/bin/verify-webgl /usr/share/nginx/html

USER 101:101
WORKDIR /usr/share/nginx/html
EXPOSE 8080
STOPSIGNAL SIGQUIT
HEALTHCHECK --interval=30s --timeout=3s --start-period=3s --retries=3 \
    CMD ["wget", "-q", "--spider", "http://127.0.0.1:8080/healthz"]
ENTRYPOINT ["/usr/local/bin/serve-webgl"]
