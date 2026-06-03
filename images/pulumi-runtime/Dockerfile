ARG SOURCE_IMAGE=pulumi/pulumi-nodejs-26
ARG SOURCE_IMAGE_VERSION=3.244.0

FROM ${SOURCE_IMAGE}:${SOURCE_IMAGE_VERSION}

ARG K8S_VERSION="1.35"
ARG HELM_VERSION="3.20"
ARG UID=1000
ARG GID=1000

ENV XDG_CONFIG_HOME=/home/pulumi/.config
ENV XDG_CACHE_HOME=/home/pulumi/.cache

RUN export DEBIAN_FRONTEND=noninteractive && \
    apt-get update -y && \
    apt-get install -y gnupg && \
    mkdir -p /etc/apt/keyrings && \
    curl -fsSL "https://pkgs.k8s.io/core:/stable:/v${K8S_VERSION}/deb/Release.key" | gpg --dearmor -o /etc/apt/keyrings/kubernetes.gpg && \
    echo "deb [signed-by=/etc/apt/keyrings/kubernetes.gpg] https://pkgs.k8s.io/core:/stable:/v${K8S_VERSION}/deb/ /" > /etc/apt/sources.list.d/kubernetes.list && \
    curl -fsSL https://packages.buildkite.com/helm-linux/helm-debian/gpgkey | gpg --dearmor -o /etc/apt/keyrings/helm.gpg && \
    echo "deb [signed-by=/etc/apt/keyrings/helm.gpg] https://packages.buildkite.com/helm-linux/helm-debian/any/ any main" > /etc/apt/sources.list.d/helm.list && \
    apt-get update -y && \
    apt-get install -y kubectl=${K8S_VERSION}.* helm=${HELM_VERSION}.* && \
    rm -rf /var/lib/apt/lists/*

# remove unnecessary user in conflict with pulumi user UID
RUN userdel -r node && \
    addgroup --gid $GID pulumi && \
    adduser --uid $UID --gid $GID --disabled-password --gecos "" pulumi

USER pulumi:pulumi
