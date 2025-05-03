#!/bin/bash

TENANTS_JSON="tenants.json"
mkdir -p .gitlab

jq -c '.[]' "$TENANTS_JSON" | while read -r tenant; do
  TENANT_NAME=$(echo "$tenant" | jq -r '.name')

  echo "$tenant" | jq -c '.environments[]' | while read -r env; do
    ENV_NAME=$(echo "$env" | jq -r '.env')

    # Check if auto_deploy_values is present for the environment
    AUTO_DEPLOY_VALUES=$(echo "$env" | jq -c '.auto_deploy_values // empty')

    if [[ -z "$AUTO_DEPLOY_VALUES" ]]; then
      echo "Skipping $TENANT_NAME-$ENV_NAME, auto_deploy_values not set."
      continue
    fi

    # Determine output file path
    if [[ "$ENV_NAME" == "production" ]]; then
      VALUES_FILE=".gitlab/${TENANT_NAME}-${ENV_NAME}-auto-deploy-values.yaml"
    else
      VALUES_FILE=".gitlab/${TENANT_NAME}-auto-deploy-values.yaml"
    fi

    echo "Generating $VALUES_FILE..."

    RESOURCES=$(echo "$AUTO_DEPLOY_VALUES" | jq -c '.resources // {}')

    REQUEST_MEM=$(echo "$RESOURCES" | jq -r '.requests.memory // "256Mi"')
    REQUEST_CPU=$(echo "$RESOURCES" | jq -r '.requests.cpu // "100m"')
    LIMIT_MEM=$(echo "$RESOURCES" | jq -r '.limits.memory // "512Mi"')
    LIMIT_CPU=$(echo "$RESOURCES" | jq -r '.limits.cpu // "500m"')

    {
      echo "resources:"
      echo "  requests:"
      echo "    memory: $REQUEST_MEM"
      echo "    cpu: $REQUEST_CPU"
      echo "  limits:"
      echo "    memory: $LIMIT_MEM"
      echo "    cpu: $LIMIT_CPU"
    } > "$VALUES_FILE"

  done
done
