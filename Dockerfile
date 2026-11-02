FROM node:24-alpine AS client-build
WORKDIR /App/ClientApp
COPY ClientApp/package*.json ./
RUN npm ci
COPY ClientApp/ ./
# vite writes into ../wwwroot, so build on top of the repo wwwroot and copy the merged folder out.
COPY wwwroot/ /App/wwwroot/
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /App

COPY Hub.csproj Directory.Build.props Directory.Packages.props NuGet.Config global.json ./
RUN dotnet restore Hub.csproj

COPY . ./
COPY --from=client-build /App/wwwroot ./wwwroot
RUN dotnet build Hub.csproj -c Release -p:SkipSpaBuild=true --property:OutputPath=/app
RUN dotnet publish Hub.csproj -c Release -p:SkipSpaBuild=true --property:PublishDir=/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 as base
COPY --from=build-env /publish /app
WORKDIR /app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hub.dll"]
