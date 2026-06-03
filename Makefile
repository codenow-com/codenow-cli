PROJECT = src/CodeNOW.Cli/CodeNOW.Cli.csproj
PULUMI_OPERATOR_VERSION = v2.7.0
FLUXCD_VERSION = v2.7.5
FLUXCD_SOURCE_CONTROLLER_VERSION = v1.7.4
PULUMI_OPERATOR_MANIFESTS_TARGET_DIR := ./assets/vendor/generated/dataplane/pulumi-operator
FLUXCD_MANIFESTS_TARGET_DIR := ./assets/vendor/generated/dataplane/fluxcd
PULUMI_OPERATOR_SOURCE_URL = https://api.github.com/repos/pulumi/pulumi-kubernetes-operator/contents/operator/config
FLUXCD_MANIFESTS_URL = https://github.com/fluxcd/flux2/releases/download/$(FLUXCD_VERSION)/manifests.tar.gz
PULUMI_OPERATOR_MANIFESTS_SOURCE_DIRS := manager crd/bases rbac
PULUMI_OPERATOR_INFO_FILE := $(PULUMI_OPERATOR_MANIFESTS_TARGET_DIR)/pulumi-operator-info.json
FLUXCD_INFO_FILE := $(FLUXCD_MANIFESTS_TARGET_DIR)/fluxcd-info.json
PULUMI_OPERATOR_IMAGE := pulumi/pulumi-kubernetes-operator
FLUXCD_SOURCE_CONTROLLER_IMAGE := fluxcd/source-controller

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
	@echo "📄 Updating operator metadata in $(PULUMI_OPERATOR_INFO_FILE)"
	@tmp=$$(mktemp); \
		jq --arg image "$(PULUMI_OPERATOR_IMAGE)" \
		   --arg version "$(PULUMI_OPERATOR_VERSION)" \
		   '.operator.image = $$image | .operator.version = $$version' \
		   "$(PULUMI_OPERATOR_INFO_FILE)" > $$tmp; \
		mv $$tmp "$(PULUMI_OPERATOR_INFO_FILE)"

update-fluxcd:
	@tmp=$$(mktemp -d); \
		echo "📥 Downloading FluxCD manifests $(FLUXCD_VERSION)..."; \
		curl -sL "$(FLUXCD_MANIFESTS_URL)" -o $$tmp/manifests.tar.gz; \
		tar -xzf $$tmp/manifests.tar.gz -C $$tmp; \
	source_file=$$(find $$tmp -type f -name source-controller.yaml | head -n 1); \
	rbac_file=$$(find $$tmp -type f -name rbac.yaml | head -n 1); \
		if [ -z "$$source_file" ]; then \
			echo "❌ source-controller.yaml not found in archive"; \
			rm -rf $$tmp; \
			exit 1; \
		fi; \
		if [ -z "$$rbac_file" ]; then \
			echo "❌ rbac.yaml not found in archive"; \
			rm -rf $$tmp; \
			exit 1; \
		fi; \
	mkdir -p $(FLUXCD_MANIFESTS_TARGET_DIR); \
	cp "$$source_file" $(FLUXCD_MANIFESTS_TARGET_DIR)/source-controller.yaml; \
	echo "✅ Wrote $(FLUXCD_MANIFESTS_TARGET_DIR)/source-controller.yaml"; \
	cp "$$rbac_file" $(FLUXCD_MANIFESTS_TARGET_DIR)/rbac.yaml; \
	echo "✅ Wrote $(FLUXCD_MANIFESTS_TARGET_DIR)/rbac.yaml"; \
	echo "📄 Writing FluxCD metadata to $(FLUXCD_INFO_FILE)"; \
	printf '{ "sourceController": { "image": "%s", "version": "%s" } }\n' \
		"$(FLUXCD_SOURCE_CONTROLLER_IMAGE)" "$(FLUXCD_SOURCE_CONTROLLER_VERSION)" \
		> $(FLUXCD_INFO_FILE); \
	rm -rf $$tmp

.PHONY: run build test restore format clean update-pulumi-operator update-fluxcd
