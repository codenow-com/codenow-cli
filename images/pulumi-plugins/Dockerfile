# syntax=docker/dockerfile:1.7

# Plugin installation only needs the Pulumi CLI, without language runtimes.
ARG SOURCE_IMAGE=pulumi/pulumi-base
ARG SOURCE_IMAGE_VERSION=3.276.0

FROM ${SOURCE_IMAGE}:${SOURCE_IMAGE_VERSION} AS build

ARG COMMAND_PROVIDER_VERSION=1.2.1
ARG CRD2PULUMI_KUBERNETES_PROVIDER_VERSION=4.5.5
ARG KUBERNETES_PROVIDER_VERSION=4.31.1
ARG RANDOM_PROVIDER_VERSION=4.21.0
ARG TLS_PROVIDER_VERSION=5.5.0

RUN <<EOF
set -e
pulumi plugin install resource command "${COMMAND_PROVIDER_VERSION}"
pulumi plugin install resource kubernetes "${CRD2PULUMI_KUBERNETES_PROVIDER_VERSION}"
pulumi plugin install resource kubernetes "${KUBERNETES_PROVIDER_VERSION}"
pulumi plugin install resource random "${RANDOM_PROVIDER_VERSION}"
pulumi plugin install resource tls "${TLS_PROVIDER_VERSION}"
chmod -R go+rX /root/.pulumi/plugins/.
EOF

FROM debian:bookworm-slim

RUN mkdir --parents /data/.pulumi/plugins
COPY --from=build /root/.pulumi/plugins/ /data/.pulumi/plugins/
