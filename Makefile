PROJECT = src/CodeNOW.Cli/CodeNOW.Cli.csproj
PULUMI_OPERATOR_VERSION = v2.3.0
PULUMI_RUNTIME_VERSION = 3.216.0-nonroot
PULUMI_PLUGINS_VERSION = 1.0.2
PULUMI_OPERATOR_MANIFESTS_TARGET_DIR := ./assets/vendor/generated/dataplane/operator
PULUMI_OPERATOR_SOURCE_URL = https://api.github.com/repos/pulumi/pulumi-kubernetes-operator/contents/operator/config
PULUMI_OPERATOR_MANIFESTS_SOURCE_DIRS := manager crd/bases rbac
PULUMI_OPERATOR_INFO_FILE := $(PULUMI_OPERATOR_MANIFESTS_TARGET_DIR)/operator-info.json
PULUMI_OPERATOR_IMAGE := pulumi/pulumi-kubernetes-operator
PULUMI_RUNTIME_IMAGE := pulumi/pulumi
PULUMI_PLUGINS_IMAGE := codenow/pulumi-operator-plugins

define download_pulumi_operator_manifests
  { \
    echo "📥 Downloading directory $(1)..."; \
    mkdir -p $(PULUMI_OPERATOR_MANIFESTS_TARGET_DIR)/$(1); \
    curl -s "$(PULUMI_OPERATOR_SOURCE_URL)/$(1)?ref=$(PULUMI_OPERATOR_VERSION)" \
      | jq -r '.[] | select(.name | ascii_downcase != "kustomization.yaml") | .name + " " + .download_url' \
      | while read filename url; do \
          echo " - $$filename"; \
          curl -sL "$$url" -o "$(PULUMI_OPERATOR_MANIFESTS_TARGET_DIR)/$(1)/$$filename"; \
        done; \
  }
endef

run:
	$(eval ARGS := $(wordlist 2, $(words $(MAKECMDGOALS)), $(MAKECMDGOALS)))
	@echo "Running: dotnet run --project $(PROJECT) -- $(ARGS)"
	@dotnet run --project $(PROJECT) -- $(ARGS)

build:
	@dotnet build $(PROJECT)

test:
	@dotnet test tests/CodeNOW.Cli.Tests/CodeNOW.Cli.Tests.csproj

restore:
	@dotnet restore $(PROJECT)
	@dotnet restore tests/CodeNOW.Cli.Tests/CodeNOW.Cli.Tests.csproj

format:
	@dotnet format CodeNOW.Cli.sln --no-restore

clean:
	@dotnet clean $(PROJECT)
	@dotnet clean tests/CodeNOW.Cli.Tests/CodeNOW.Cli.Tests.csproj

%:
	@:

update-pulumi-operator:
	@for dir in $(PULUMI_OPERATOR_MANIFESTS_SOURCE_DIRS); do \
		$(call download_pulumi_operator_manifests,$$dir); \
	done
	@mkdir -p $(PULUMI_OPERATOR_MANIFESTS_TARGET_DIR)
	@echo "📄 Writing operator metadata to $(PULUMI_OPERATOR_INFO_FILE)"
	@printf '{ "operator": { "image": "%s", "version": "%s" }, "runtime": { "image": "%s", "version": "%s" }, "plugins": { "image": "%s", "version": "%s" } }\n' \
		"$(PULUMI_OPERATOR_IMAGE)" "$(PULUMI_OPERATOR_VERSION)" \
		"$(PULUMI_RUNTIME_IMAGE)" "$(PULUMI_RUNTIME_VERSION)" \
		"$(PULUMI_PLUGINS_IMAGE)" "$(PULUMI_PLUGINS_VERSION)" \
		> $(PULUMI_OPERATOR_INFO_FILE)

.PHONY: run build test restore format clean update-pulumi-operator
