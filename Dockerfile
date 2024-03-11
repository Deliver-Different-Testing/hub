# syntax = docker/dockerfile:experimental

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
WORKDIR /App

COPY *.csproj ./
RUN --mount=type=secret,id=auto-devops-build-secrets . /run/secrets/auto-devops-build-secrets && dotnet restore

COPY . ./
RUN dotnet build -c Release --property:OutputPath=/app
RUN dotnet publish -c Release --property:PublishDir=/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 as base
COPY --from=build-env /publish /app
WORKDIR /app
EXPOSE 8080
COPY entrypoint.sh entrypoint.sh
RUN chmod +x entrypoint.sh
ENTRYPOINT ["./entrypoint.sh"]
