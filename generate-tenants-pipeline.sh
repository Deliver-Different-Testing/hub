#!/bin/bash

TENANTS_JSON="tenants.json"

echo "stages:"
echo "  - deploy"

jq -c '.[]' "$TENANTS_JSON" | while read -r tenant; do
  TENANT_NAME=$(echo "$tenant" | jq -r '.name')

  echo "$tenant" | jq -c '.environments[]' | while read -r env; do
    ENV_NAME=$(echo "$env" | jq -r '.env')
    CI_ENVIRONMENT_NAME=$(echo "$env" | jq -r '.ci_environment_name')
    ENABLED_VAR=$(echo "$env" | jq -r '.enabled_var')
    HOST_VAR=$(echo "$env" | jq -r '.host_var')
    ENV_FILE=$(echo "$env" | jq -r '.env_file')

    JOB_NAME="${TENANT_NAME}-${ENV_NAME}"
    STAGE_NAME="tenant-${ENV_NAME}"
    # Check if auto_deploy_values is present for the environment
    AUTO_DEPLOY_VALUES=$(echo "$env" | jq -c '.auto_deploy_values // empty')
    if [[ -z "$AUTO_DEPLOY_VALUES" ]]; then
      VALUES_FILE=".gitlab/auto-deploy-values.yaml"
    else
      VALUES_FILE=".gitlab/${TENANT_NAME}-${ENV_NAME}-auto-deploy-values.yaml"
    fi

    # Conditionally set needs block
    NEEDS_BLOCK=""
    if [[ "$ENV_NAME" == "production" ]]; then
      NEEDS_BLOCK=$(cat <<NEEDS
  needs:
    - job: build
    - job: ${TENANT_NAME}-staging
NEEDS
)
    fi

    cat <<EOF

$JOB_NAME:
  extends: $ENV_NAME
  stage: $STAGE_NAME
  variables:
    ENV_FILE: $ENV_FILE
  before_script:
    - *load-env-file
    - export HELM_UPGRADE_VALUES_FILE="$VALUES_FILE"
  environment:
    name: $CI_ENVIRONMENT_NAME
    url: \$$HOST_VAR
  rules:
    - if: '\$$HOST_VAR == null'
      when: never
    - if: '\$$ENABLED_VAR'
      when: manual
$NEEDS_BLOCK
EOF

  done
done
