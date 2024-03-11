#!/bin/bash

# Start the application
--mount=type=secret,id=auto-devops-build-secrets . /run/secrets/auto-devops-build-secrets && dotnet UrgentHub.dll
