#!/bin/bash
set -e

# Load secrets
source /run/secrets/auto-devops-build-secrets

# Start the application
dotnet UrgentHub.dll
