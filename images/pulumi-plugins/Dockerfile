# syntax=docker/dockerfile:1.7

ARG SOURCE_IMAGE=pulumi/pulumi
ARG SOURCE_IMAGE_VERSION=3.244.0

FROM ${SOURCE_IMAGE}:${SOURCE_IMAGE_VERSION} AS build

ARG COMMAND_PROVIDER_VERSION=1.2.1
ARG KUBERNETES_PROVIDER_VERSION=4.31.1
ARG RANDOM_PROVIDER_VERSION=4.21.0
ARG TLS_PROVIDER_VERSION=5.5.0

RUN <<EOF
set -e
pulumi plugin install resource command "${COMMAND_PROVIDER_VERSION}"
pulumi plugin install resource kubernetes "${KUBERNETES_PROVIDER_VERSION}"
pulumi plugin install resource random "${RANDOM_PROVIDER_VERSION}"
pulumi plugin install resource tls "${TLS_PROVIDER_VERSION}"
chmod -R go+rX /root/.pulumi/plugins/.
EOF

FROM debian:bookworm-slim

RUN mkdir --parents /data/.pulumi/plugins
COPY --from=build /root/.pulumi/plugins/ /data/.pulumi/plugins/
